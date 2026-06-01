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

                for (int i = 0; i < charsRead; i++)
                {
                    var character = buffer[i];
                    if (character == '\n')
                    {
                        await EmitLineAsync(context, lineBuilder);
                    }
                    else
                    {
                        lineBuilder.Append(character);
                    }
                }
            }
            while (!reader.EndOfStream && !context.CancellationToken.IsCancellationRequested);

            await EmitLineAsync(context, lineBuilder);
        }

        private static async Task EmitLineAsync(StreamMessageReadContext context, StringBuilder lineBuilder)
        {
            if (lineBuilder.Length == 0)
            {
                return;
            }

            var line = lineBuilder.ToString().Trim();
            lineBuilder.Clear();

            if (line.Length > 0)
            {
                await context.EmitMessageAsync(line);
            }
        }
    }
}
