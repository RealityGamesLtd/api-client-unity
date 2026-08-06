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
            // Seeded rather than default-16: a straddling line rebuilds the builder from scratch on
            // every stream, and the doubling steps up from 16 are pure ExpandByABlock garbage for
            // any workload that opens streams repeatedly.
            var lineBuilder = new StringBuilder(8 * 1024);

            // When the transport can deserialize straight from a TextReader, no line is ever
            // materialised as a string: an in-chunk line is wrapped where it lies in the read
            // buffer, a straddling line is wrapped over the builder. One reusable wrapper each
            // for the life of the stream. Lines can run to hundreds of KB, so the string this
            // path skips is the stream's dominant allocation.
            CharSegmentTextReader segmentReader = null;
            StringBuilderTextReader builderReader = null;
            if (context.SupportsReaderEmit)
            {
                segmentReader = new CharSegmentTextReader();
                builderReader = new StringBuilderTextReader();
            }

            do
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                int charsRead = await reader.ReadAsync(buffer, context.CancellationToken);
                context.NotifyRead();

                // Walk newline to newline rather than character by character. Appending one char at a
                // time made the builder one of the heaviest allocators on the stream path, because
                // every growth step reallocates its buffer.
                int lineStart = 0;
                for (int i = 0; i < charsRead; i++)
                {
                    if (buffer[i] != '\n') continue;

                    if (lineBuilder.Length == 0)
                    {
                        // Nothing pending, so this line lies wholly inside the chunk and can go
                        // straight out where it sits — no builder involvement at all.
                        await EmitSpanAsync(context, buffer, lineStart, i - lineStart, segmentReader);
                    }
                    else
                    {
                        lineBuilder.Append(buffer, lineStart, i - lineStart);
                        lineBuilder = await EmitBuilderAsync(context, lineBuilder, builderReader);
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
                await EmitBuilderAsync(context, lineBuilder, builderReader);
            }
        }

        /// <summary>
        /// Emits one line held entirely in <paramref name="buffer"/>, trimmed — through
        /// <paramref name="segmentReader"/> when the transport takes readers, otherwise as a
        /// single string.
        /// </summary>
        private static Task EmitSpanAsync(StreamMessageReadContext context, char[] buffer, int start, int length, CharSegmentTextReader segmentReader)
        {
            int end = start + length;
            while (start < end && char.IsWhiteSpace(buffer[start])) start++;
            while (end > start && char.IsWhiteSpace(buffer[end - 1])) end--;

            if (end <= start)
            {
                return Task.CompletedTask;
            }

            if (segmentReader != null)
            {
                // Safe to lend the read buffer: the emit is awaited before the scan continues,
                // so the buffer is not refilled while the transport is consuming it.
                segmentReader.Reset(buffer, start, end - start);
                return context.EmitMessageAsync(segmentReader);
            }

            return context.EmitMessageAsync(new string(buffer, start, end - start));
        }

        /// <summary>
        /// Emits the buffered line, trimmed, and returns the builder to keep using. On the string
        /// path that is a fresh builder when the old grew past <see cref="MaxRetainedBuilderCapacity"/>
        /// (ToString() marks the buffer shared, so a big builder would reallocate on every Clear);
        /// the reader path never produces the string, so it always keeps the builder. Trimming is
        /// applied to the builder's bounds so at most one string is produced, where
        /// ToString().Trim() produced two.
        /// </summary>
        private static async Task<StringBuilder> EmitBuilderAsync(StreamMessageReadContext context, StringBuilder lineBuilder, StringBuilderTextReader builderReader)
        {
            int start = 0;
            int end = lineBuilder.Length;
            while (start < end && char.IsWhiteSpace(lineBuilder[start])) start++;
            while (end > start && char.IsWhiteSpace(lineBuilder[end - 1])) end--;

            if (end > start)
            {
                if (builderReader != null)
                {
                    // The builder is only cleared below, after the emit completes, so lending
                    // it out here is safe.
                    builderReader.Reset(lineBuilder, start, end);
                    await context.EmitMessageAsync(builderReader);
                    builderReader.Release();

                    // This path never calls ToString(), so the builder's buffer is never
                    // marked shared and Clear() is allocation-free at ANY capacity — the
                    // copy-on-write hazard the capacity trim below exists for cannot happen.
                    // Keeping the builder at peak size trades stream-lifetime retention for
                    // zero per-message allocation, the right trade for the wave-shaped NDJSON
                    // streams this path serves: a stream of large lines reuses one buffer
                    // instead of allocating a message-sized builder per message. (After a
                    // parse error CaptureBounded does call ToString(), so the next Clear()
                    // reallocates once — rare and bounded.)
                    lineBuilder.Clear();
                    return lineBuilder;
                }

                await context.EmitMessageAsync(lineBuilder.ToString(start, end - start));
            }

            if (lineBuilder.Capacity > MaxRetainedBuilderCapacity)
            {
                // Sized to the message just emitted, NOT dropped to nothing. Dropping it means a stream
                // whose messages are consistently large re-doubles a builder from 16 chars every single
                // time, which costs more than the Clear() it avoids (ExpandByABlock rises while
                // set_Length falls). Starting at the last message's length avoids both the
                // copy-on-write Clear and the regrowth.
                return new StringBuilder(end - start > 0 ? end - start : 0);
            }

            lineBuilder.Clear();
            return lineBuilder;
        }
    }
}
