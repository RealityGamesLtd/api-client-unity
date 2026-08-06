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

                context.NotifyRead();

                // Test the delimiter on the chunk itself. Materialising the whole chunk as a string
                // first allocated one copy per read even when the chunk was only going to be buffered.
                bool endsMessage = charsRead >= 2
                                   && buffer[charsRead - 1] == '\n'
                                   && buffer[charsRead - 2] == '\n';

                if (endsMessage == false)
                {
                    partialMessageBuilder.Append(buffer, 0, charsRead);
                    continue;
                }

                string readString;
                if (partialMessageBuilder.Length > 0)
                {
                    partialMessageBuilder.Append(buffer, 0, charsRead);
                    readString = partialMessageBuilder.ToString();
                    partialMessageBuilder.Clear();
                }
                else
                {
                    readString = new string(buffer, 0, charsRead);
                }

                if (context.ResponseMessage.Content != null)
                {
                    context.ResponseMessage.Content.Headers.ContentLength = readString.Length;
                }

                // The parsing-error emit is awaited AFTER the sample closes: an await between
                // BeginSample and EndSample can resume on a later frame, which breaks the
                // per-frame pairing rule and logs Missing/Non-matching EndSample errors.
                MatchCollection matches = null;
                string regexError = null;
                Profiler.BeginSample("Api Client Stream Regex Extraction");
                try
                {
                    matches = JsonExtractorRegex.Matches(readString);
                }
                catch (Exception ex)
                {
                    regexError = ex.Message;
                }
                finally
                {
                    Profiler.EndSample();
                }

                if (regexError != null)
                {
                    await context.EmitParsingErrorAsync(readString, regexError);
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
            while (!reader.EndOfStream);
        }
    }
}
