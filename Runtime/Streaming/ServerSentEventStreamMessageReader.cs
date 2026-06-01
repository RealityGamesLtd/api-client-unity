using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine.Profiling;

namespace ApiClient.Runtime.Streaming
{
    /// <summary>
    /// Frames a Server-Sent-Events style stream where a complete message is delimited by a
    /// blank line ("\n\n") and the JSON payload is extracted from the framed text with a
    /// regular expression. Chunks that do not yet end a message are buffered until the
    /// delimiter arrives.
    /// </summary>
    public sealed class ServerSentEventStreamMessageReader : IStreamMessageReader
    {
        public static readonly ServerSentEventStreamMessageReader Instance = new();

        private static readonly Regex JsonExtractorRegex = new(@"({.*})", RegexOptions.Compiled | RegexOptions.Multiline);

        public async Task ReadAsync(StreamMessageReadContext context)
        {
            var reader = context.Reader;
            var buffer = new char[context.BufferSize];
            var partialMessageBuilder = new StringBuilder();

            do
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                int charsRead = await reader.ReadAsync(buffer, context.CancellationToken);
                var readString = new string(buffer, 0, charsRead);

                context.NotifyRead();

                if (readString.EndsWith("\n\n") == false)
                {
                    partialMessageBuilder.Append(readString);
                    continue;
                }

                if (partialMessageBuilder.Length > 0)
                {
                    partialMessageBuilder.Append(readString);
                    readString = partialMessageBuilder.ToString();
                    partialMessageBuilder.Clear();
                }

                context.ResponseMessage.Content.Headers.ContentLength = readString.Length;

                MatchCollection matches = null;
                try
                {
                    Profiler.BeginSample("Api Client Stream Regex Extraction");
                    matches = JsonExtractorRegex.Matches(readString);
                }
                catch (Exception ex)
                {
                    await context.EmitParsingErrorAsync(readString, ex.Message);
                }
                finally
                {
                    Profiler.EndSample();
                }

                if (matches != null && matches.Count > 0)
                {
                    for (int i = 0; i < matches.Count; i++)
                    {
                        context.CancellationToken.ThrowIfCancellationRequested();

                        var jsonString = matches[i].Value;
                        if (!string.IsNullOrEmpty(jsonString))
                        {
                            await context.EmitMessageAsync(jsonString);
                        }
                        else
                        {
                            await context.EmitParsingErrorAsync(readString, "JSON string is null");
                        }
                    }
                }
                else
                {
                    await context.EmitParsingErrorAsync(readString, "Couldn't get valid JSON string that is matching regex pattern");
                }
            }
            while (!reader.EndOfStream && !context.CancellationToken.IsCancellationRequested);
        }
    }
}
