using System.Text;
using System.Threading.Tasks;

namespace ApiClient.Runtime.Streaming
{
    /// <summary>
    /// Frames a newline-delimited JSON (NDJSON) stream where every non-empty line is a
    /// complete JSON message. Lines are emitted as soon as their terminating newline is
    /// read so consumers receive data progressively; a trailing line without a newline is
    /// flushed when the stream ends. Reads are performed in cancellable character chunks so
    /// line boundaries are recovered independently of how the transport chunks the body.
    /// </summary>
    public sealed class NewlineDelimitedJsonStreamMessageReader : IStreamMessageReader
    {
        public static readonly NewlineDelimitedJsonStreamMessageReader Instance = new();

        /// <summary>
        /// A builder that has grown past this is replaced rather than cleared. Mono's StringBuilder is
        /// copy-on-write: ToString() marks the char buffer as shared, so the next mutation — including
        /// Clear() — reallocates the whole capacity. Without this, one oversized message makes every
        /// later Clear() on that stream pay for the peak.
        /// </summary>
        private const int MaxRetainedBuilderCapacity = 64 * 1024;

        public async Task ReadAsync(StreamMessageReadContext context)
        {
            var reader = context.Reader;
            var buffer = new char[context.BufferSize];
            var lineBuilder = new StringBuilder();

            do
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                int charsRead = await reader.ReadAsync(buffer, context.CancellationToken);
                context.NotifyRead();

                // Walk newline to newline rather than character by character. Appending one char at a
                // time made the builder the app's third-largest allocator (8.4 MB of the 185 MB in the
                // 2026-08-05 deep-profile capture) because every growth step reallocates its buffer.
                int lineStart = 0;
                for (int i = 0; i < charsRead; i++)
                {
                    if (buffer[i] != '\n') continue;

                    if (lineBuilder.Length == 0)
                    {
                        // Nothing pending, so this line lies wholly inside the chunk and can go
                        // straight out as one string — no builder involvement at all.
                        await EmitSpanAsync(context, buffer, lineStart, i - lineStart);
                    }
                    else
                    {
                        lineBuilder.Append(buffer, lineStart, i - lineStart);
                        lineBuilder = await EmitBuilderAsync(context, lineBuilder);
                    }

                    lineStart = i + 1;
                }

                // Whatever trails the last newline continues into the next read.
                if (charsRead > lineStart)
                {
                    lineBuilder.Append(buffer, lineStart, charsRead - lineStart);
                }
            }
            while (!reader.EndOfStream);

            // A final line with no terminating newline.
            if (lineBuilder.Length > 0)
            {
                await EmitBuilderAsync(context, lineBuilder);
            }
        }

        /// <summary>Emits one line held entirely in <paramref name="buffer"/>, trimmed, as a single string.</summary>
        private static Task EmitSpanAsync(StreamMessageReadContext context, char[] buffer, int start, int length)
        {
            int end = start + length;
            while (start < end && char.IsWhiteSpace(buffer[start])) start++;
            while (end > start && char.IsWhiteSpace(buffer[end - 1])) end--;

            return end > start
                ? context.EmitMessageAsync(new string(buffer, start, end - start))
                : Task.CompletedTask;
        }

        /// <summary>
        /// Emits the buffered line, trimmed, and returns the builder to keep using — a fresh one when
        /// the old grew past <see cref="MaxRetainedBuilderCapacity"/>. Trimming is applied to the
        /// builder's bounds so only one string is produced, where ToString().Trim() produced two.
        /// </summary>
        private static async Task<StringBuilder> EmitBuilderAsync(StreamMessageReadContext context, StringBuilder lineBuilder)
        {
            int start = 0;
            int end = lineBuilder.Length;
            while (start < end && char.IsWhiteSpace(lineBuilder[start])) start++;
            while (end > start && char.IsWhiteSpace(lineBuilder[end - 1])) end--;

            if (end > start)
            {
                await context.EmitMessageAsync(lineBuilder.ToString(start, end - start));
            }

            if (lineBuilder.Capacity > MaxRetainedBuilderCapacity)
            {
                // Sized to the message just emitted, NOT dropped to nothing. Dropping it meant a stream
                // whose messages are consistently large re-doubled a builder from 16 chars every single
                // time, which cost more than the Clear() it avoided — visible in the 13-38 capture as
                // ExpandByABlock rising while set_Length fell. Starting at the last message's length
                // avoids both the copy-on-write Clear and the regrowth.
                return new StringBuilder(end - start > 0 ? end - start : 0);
            }

            lineBuilder.Clear();
            return lineBuilder;
        }
    }
}
