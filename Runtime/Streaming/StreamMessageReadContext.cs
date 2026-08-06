using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ApiClient.Runtime.Streaming
{
    /// <summary>
    /// The collaborators a transport hands to an <see cref="IStreamMessageReader"/>: the
    /// source <see cref="StreamReader"/> plus callbacks for emitting a framed JSON message,
    /// reporting a framing-level parsing error and signalling read progress. A reader frames
    /// the stream and forwards through these callbacks; it never deserializes or builds
    /// responses itself.
    /// </summary>
    public sealed class StreamMessageReadContext
    {
        public StreamReader Reader { get; }
        public HttpResponseMessage ResponseMessage { get; }
        public int BufferSize { get; }
        public CancellationToken CancellationToken { get; }

        private readonly Func<string, Task> _emitMessage;
        private readonly Func<FramedMessageTextReader, Task> _emitMessageReader;
        private readonly Func<string, string, Task> _emitParsingError;
        private readonly Action _notifyRead;

        public StreamMessageReadContext(
            StreamReader reader,
            HttpResponseMessage responseMessage,
            int bufferSize,
            CancellationToken cancellationToken,
            Func<string, Task> emitMessage,
            Func<string, string, Task> emitParsingError,
            Action notifyRead)
            : this(reader, responseMessage, bufferSize, cancellationToken, emitMessage, null, emitParsingError, notifyRead)
        {
        }

        public StreamMessageReadContext(
            StreamReader reader,
            HttpResponseMessage responseMessage,
            int bufferSize,
            CancellationToken cancellationToken,
            Func<string, Task> emitMessage,
            Func<FramedMessageTextReader, Task> emitMessageReader,
            Func<string, string, Task> emitParsingError,
            Action notifyRead)
        {
            Reader = reader;
            ResponseMessage = responseMessage;
            BufferSize = bufferSize;
            CancellationToken = cancellationToken;
            _emitMessage = emitMessage;
            _emitMessageReader = emitMessageReader;
            _emitParsingError = emitParsingError;
            _notifyRead = notifyRead;
        }

        /// <summary>
        /// Whether the transport can consume a framed message as a <see cref="FramedMessageTextReader"/>,
        /// skipping the per-message string. Readers that can frame without materialising the text should
        /// prefer <see cref="EmitMessageAsync(FramedMessageTextReader)"/> when this is true.
        /// </summary>
        public bool SupportsReaderEmit => _emitMessageReader != null;

        /// <summary>Forwards a single framed JSON message to the transport callback, which deserializes and dispatches it.</summary>
        public Task EmitMessageAsync(string json) => _emitMessage(json);

        /// <summary>
        /// Forwards a framed message without materialising it as a string. The reader is only
        /// valid until the returned task completes — the framing reader reuses its buffers for
        /// the next message. Callers must check <see cref="SupportsReaderEmit"/> first.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The transport has no reader-emit callback (<see cref="SupportsReaderEmit"/> is false).
        /// </exception>
        public Task EmitMessageAsync(FramedMessageTextReader messageReader)
        {
            // Fail with the invariant rather than an NRE from a null callback: a reader that
            // emits without checking SupportsReaderEmit is a bug in that reader, and the
            // string overload is always available as the fallback.
            if (_emitMessageReader == null)
            {
                throw new InvalidOperationException(
                    "This transport does not support reader-emit. Check SupportsReaderEmit before " +
                    "calling EmitMessageAsync(FramedMessageTextReader), or use EmitMessageAsync(string).");
            }

            return _emitMessageReader(messageReader);
        }

        /// <summary>Reports a framing-level parsing failure for the given raw content.</summary>
        public Task EmitParsingErrorAsync(string rawContent, string message) => _emitParsingError(rawContent, message);

        /// <summary>Signals that bytes were just read, refreshing the read-delta watchdog.</summary>
        public void NotifyRead() => _notifyRead();
    }
}
