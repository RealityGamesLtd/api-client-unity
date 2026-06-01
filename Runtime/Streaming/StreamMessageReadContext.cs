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
        {
            Reader = reader;
            ResponseMessage = responseMessage;
            BufferSize = bufferSize;
            CancellationToken = cancellationToken;
            _emitMessage = emitMessage;
            _emitParsingError = emitParsingError;
            _notifyRead = notifyRead;
        }

        /// <summary>Deserializes and dispatches a single framed JSON message to the caller.</summary>
        public Task EmitMessageAsync(string json) => _emitMessage(json);

        /// <summary>Reports a framing-level parsing failure for the given raw content.</summary>
        public Task EmitParsingErrorAsync(string rawContent, string message) => _emitParsingError(rawContent, message);

        /// <summary>Signals that bytes were just read, refreshing the read-delta watchdog.</summary>
        public void NotifyRead() => _notifyRead();
    }
}
