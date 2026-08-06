using System;
using System.IO;
using System.Text;

namespace ApiClient.Runtime.Streaming
{
    /// <summary>
    /// A <see cref="TextReader"/> over one framed stream message, handed to the transport's
    /// reader-emit path so it can deserialize straight from the framing buffers without
    /// materialising the message as a string. Instances are reused: they are valid only for
    /// the duration of the emit call, because the framing reader repositions them onto the
    /// next message as soon as the emit completes. <see cref="CaptureBounded"/> reads from
    /// the original message bounds regardless of how much has been consumed, so a failed
    /// parse can still report the raw text.
    /// </summary>
    public abstract class FramedMessageTextReader : TextReader
    {
        /// <summary>
        /// The framed message's text (from its original bounds, independent of read position),
        /// truncated to <paramref name="maxChars"/>. Only for error reporting — calling this on
        /// the happy path would reintroduce the very string this type exists to avoid.
        /// </summary>
        public abstract string CaptureBounded(int maxChars);

        /// <summary>
        /// Reused across messages, so disposal must not tear anything down. JsonTextReader
        /// disposes its input when CloseInput is left set; surviving that keeps the contract
        /// independent of every caller remembering to clear it.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
        }
    }

    /// <summary>A framed message lying wholly inside a read chunk: a <c>char[]</c> segment.</summary>
    public sealed class CharSegmentTextReader : FramedMessageTextReader
    {
        private char[] _buffer;
        private int _start;
        private int _end;
        private int _position;

        /// <summary>Repositions the reader onto a new segment. The array is not copied.</summary>
        public void Reset(char[] buffer, int offset, int count)
        {
            _buffer = buffer;
            _start = offset;
            _end = offset + count;
            _position = offset;
        }

        public override int Peek() => _position < _end ? _buffer[_position] : -1;

        public override int Read() => _position < _end ? _buffer[_position++] : -1;

        public override int Read(char[] buffer, int index, int count)
        {
            int available = _end - _position;
            if (available <= 0)
            {
                return 0;
            }

            int copied = count < available ? count : available;
            Array.Copy(_buffer, _position, buffer, index, copied);
            _position += copied;
            return copied;
        }

        public override string CaptureBounded(int maxChars)
        {
            int length = _end - _start;
            if (length > maxChars)
            {
                length = maxChars;
            }

            return length > 0 ? new string(_buffer, _start, length) : string.Empty;
        }
    }

    /// <summary>A framed message that straddled read chunks and was reassembled in a <see cref="StringBuilder"/>.</summary>
    public sealed class StringBuilderTextReader : FramedMessageTextReader
    {
        private StringBuilder _builder;
        private int _start;
        private int _end;
        private int _position;

        /// <summary>Repositions the reader onto <paramref name="builder"/>'s [start, end) range. The builder is not copied.</summary>
        public void Reset(StringBuilder builder, int start, int end)
        {
            _builder = builder;
            _start = start;
            _end = end;
            _position = start;
        }

        /// <summary>
        /// Drops the builder reference once the emit completes. Without this the wrapper —
        /// which lives as long as the stream — would pin whatever builder it last lent out,
        /// even after the framing loop has moved on to a different one.
        /// </summary>
        public void Release()
        {
            _builder = null;
            _start = 0;
            _end = 0;
            _position = 0;
        }

        public override int Peek() => _position < _end ? _builder[_position] : -1;

        public override int Read() => _position < _end ? _builder[_position++] : -1;

        public override int Read(char[] buffer, int index, int count)
        {
            int available = _end - _position;
            if (available <= 0)
            {
                return 0;
            }

            // Block copy via CopyTo — the indexer walks the builder's chunk list per char.
            int copied = count < available ? count : available;
            _builder.CopyTo(_position, buffer, index, copied);
            _position += copied;
            return copied;
        }

        public override string CaptureBounded(int maxChars)
        {
            int length = _end - _start;
            if (length > maxChars)
            {
                length = maxChars;
            }

            return length > 0 ? _builder.ToString(_start, length) : string.Empty;
        }
    }
}
