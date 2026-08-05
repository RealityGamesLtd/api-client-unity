using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime.Streaming;
using NUnit.Framework;

namespace ApiClient.Tests
{
    [TestFixture]
    public class NewlineDelimitedJsonStreamMessageReaderTests
    {
        [Test]
        public async Task EmitsEachCompleteLineInOrder()
        {
            var messages = await RunReaderAsync("{\"id\":\"a\"}\n{\"id\":\"b\"}\n{\"id\":\"c\"}\n");

            CollectionAssert.AreEqual(
                new[] { "{\"id\":\"a\"}", "{\"id\":\"b\"}", "{\"id\":\"c\"}" },
                messages);
        }

        [Test]
        public async Task SkipsBlankAndWhitespaceOnlyLines()
        {
            var messages = await RunReaderAsync("{\"id\":\"a\"}\n\n   \n{\"id\":\"b\"}\n");

            CollectionAssert.AreEqual(
                new[] { "{\"id\":\"a\"}", "{\"id\":\"b\"}" },
                messages);
        }

        [Test]
        public async Task FlushesTrailingLineWithoutNewline()
        {
            var messages = await RunReaderAsync("{\"id\":\"a\"}\n{\"id\":\"b\"}");

            CollectionAssert.AreEqual(
                new[] { "{\"id\":\"a\"}", "{\"id\":\"b\"}" },
                messages);
        }

        [Test]
        public async Task TrimsCarriageReturnFromCrlfLines()
        {
            var messages = await RunReaderAsync("{\"id\":\"a\"}\r\n{\"id\":\"b\"}\r\n");

            CollectionAssert.AreEqual(
                new[] { "{\"id\":\"a\"}", "{\"id\":\"b\"}" },
                messages);
        }

        [Test]
        public async Task ReassemblesLineLongerThanBuffer()
        {
            var longLine = "{\"id\":\"" + new string('x', 5000) + "\"}";

            var messages = await RunReaderAsync(longLine + "\n", bufferSize: 16);

            CollectionAssert.AreEqual(new[] { longLine }, messages);
        }

        [Test]
        public async Task RecoversLineBoundariesAcrossTinyChunks()
        {
            var messages = await RunReaderAsync("aa\nbbb\ncccc\n", bufferSize: 4);

            CollectionAssert.AreEqual(new[] { "aa", "bbb", "cccc" }, messages);
        }

        [Test]
        public async Task EmptyStreamEmitsNothing()
        {
            var messages = await RunReaderAsync(string.Empty);

            CollectionAssert.IsEmpty(messages);
        }

        [Test]
        public async Task NotifiesReadProgress()
        {
            var readCount = 0;
            var messages = new List<string>();

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":\"a\"}\n"));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var response = new HttpResponseMessage();
            var context = CreateContext(reader, response, 4096, CancellationToken.None, messages, () => readCount++);

            await NewlineDelimitedJsonStreamMessageReader.Instance.ReadAsync(context);

            Assert.GreaterOrEqual(readCount, 1);
        }

        [Test]
        public async Task CancelledTokenThrowsWithoutEmitting()
        {
            var messages = new List<string>();

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":\"a\"}\n{\"id\":\"b\"}\n"));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var response = new HttpResponseMessage();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var context = CreateContext(reader, response, 4096, cts.Token, messages, null);

            var threw = false;
            try
            {
                await NewlineDelimitedJsonStreamMessageReader.Instance.ReadAsync(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "Expected OperationCanceledException to be thrown.");
            CollectionAssert.IsEmpty(messages);
        }

        // --- Reader-emit path: the transport consumes a FramedMessageTextReader, no string is built ---

        [Test]
        public async Task ReaderEmit_EmitsEachCompleteLineInOrder()
        {
            var messages = await RunReaderEmitAsync("{\"id\":\"a\"}\n{\"id\":\"b\"}\n{\"id\":\"c\"}\n");

            CollectionAssert.AreEqual(
                new[] { "{\"id\":\"a\"}", "{\"id\":\"b\"}", "{\"id\":\"c\"}" },
                messages);
        }

        [Test]
        public async Task ReaderEmit_SkipsBlankAndWhitespaceOnlyLines()
        {
            var messages = await RunReaderEmitAsync("{\"id\":\"a\"}\n\n   \n{\"id\":\"b\"}\n");

            CollectionAssert.AreEqual(
                new[] { "{\"id\":\"a\"}", "{\"id\":\"b\"}" },
                messages);
        }

        [Test]
        public async Task ReaderEmit_FlushesTrailingLineWithoutNewline()
        {
            var messages = await RunReaderEmitAsync("{\"id\":\"a\"}\n{\"id\":\"b\"}");

            CollectionAssert.AreEqual(
                new[] { "{\"id\":\"a\"}", "{\"id\":\"b\"}" },
                messages);
        }

        [Test]
        public async Task ReaderEmit_TrimsCarriageReturnFromCrlfLines()
        {
            var messages = await RunReaderEmitAsync("{\"id\":\"a\"}\r\n{\"id\":\"b\"}\r\n");

            CollectionAssert.AreEqual(
                new[] { "{\"id\":\"a\"}", "{\"id\":\"b\"}" },
                messages);
        }

        [Test]
        public async Task ReaderEmit_ReassemblesLineLongerThanBuffer()
        {
            var longLine = "{\"id\":\"" + new string('x', 5000) + "\"}";

            var messages = await RunReaderEmitAsync(longLine + "\n", bufferSize: 16);

            CollectionAssert.AreEqual(new[] { longLine }, messages);
        }

        [Test]
        public async Task ReaderEmit_RecoversLineBoundariesAcrossTinyChunks()
        {
            var messages = await RunReaderEmitAsync("aa\nbbb\ncccc\n", bufferSize: 4);

            CollectionAssert.AreEqual(new[] { "aa", "bbb", "cccc" }, messages);
        }

        [Test]
        public async Task ReaderEmit_PartiallyConsumedMessageDoesNotCorruptTheNext()
        {
            var consumed = new List<string>();
            var first = true;

            await RunReaderEmitCustomAsync("{\"id\":\"aaaa\"}\n{\"id\":\"b\"}\n", 4096, reader =>
            {
                if (first)
                {
                    first = false;
                    // Consume a single char and abandon the rest of the message.
                    consumed.Add(((char)reader.Read()).ToString());
                }
                else
                {
                    consumed.Add(reader.ReadToEnd());
                }

                return Task.CompletedTask;
            });

            CollectionAssert.AreEqual(new[] { "{", "{\"id\":\"b\"}" }, consumed);
        }

        [Test]
        public async Task ReaderEmit_CaptureBoundedReturnsMessageStartEvenAfterConsumption()
        {
            var captures = new List<string>();

            // With bufferSize 8 the first line ("abcdefg\n") lies wholly inside the first chunk
            // and goes through the char-segment reader; the second line straddles chunks and
            // goes through the StringBuilder reader. Both must capture from the original bounds
            // even though the reader was already consumed.
            await RunReaderEmitCustomAsync("abcdefg\nijklmnopqrstuvwxyz\n", 8, reader =>
            {
                reader.ReadToEnd();
                captures.Add(reader.CaptureBounded(5));
                return Task.CompletedTask;
            });

            CollectionAssert.AreEqual(new[] { "abcde", "ijklm" }, captures);
        }

        [Test]
        public async Task ReaderEmit_CaptureBoundedLargerThanMessageReturnsWholeMessage()
        {
            var captures = new List<string>();

            await RunReaderEmitCustomAsync("{\"id\":\"a\"}\n", 4096, reader =>
            {
                captures.Add(reader.CaptureBounded(4096));
                return Task.CompletedTask;
            });

            CollectionAssert.AreEqual(new[] { "{\"id\":\"a\"}" }, captures);
        }

        [Test]
        public async Task ReaderEmit_NeverFallsBackToStringEmit()
        {
            var messages = new List<string>();

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":\"a\"}\n{\"id\":\"b\"}"));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var response = new HttpResponseMessage();
            var context = new StreamMessageReadContext(
                reader,
                response,
                4096,
                CancellationToken.None,
                _ =>
                {
                    Assert.Fail("String emit must not be used when the reader emit is available.");
                    return Task.CompletedTask;
                },
                messageReader =>
                {
                    messages.Add(messageReader.ReadToEnd());
                    return Task.CompletedTask;
                },
                (rawContent, message) => Task.CompletedTask,
                () => { });

            await NewlineDelimitedJsonStreamMessageReader.Instance.ReadAsync(context);

            CollectionAssert.AreEqual(new[] { "{\"id\":\"a\"}", "{\"id\":\"b\"}" }, messages);
        }

        private static async Task<List<string>> RunReaderAsync(string content, int bufferSize = 4096)
        {
            var messages = new List<string>();

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var response = new HttpResponseMessage();
            var context = CreateContext(reader, response, bufferSize, CancellationToken.None, messages, null);

            await NewlineDelimitedJsonStreamMessageReader.Instance.ReadAsync(context);

            return messages;
        }

        private static async Task<List<string>> RunReaderEmitAsync(string content, int bufferSize = 4096)
        {
            var messages = new List<string>();

            await RunReaderEmitCustomAsync(content, bufferSize, messageReader =>
            {
                messages.Add(messageReader.ReadToEnd());
                return Task.CompletedTask;
            });

            return messages;
        }

        private static async Task RunReaderEmitCustomAsync(string content, int bufferSize, Func<FramedMessageTextReader, Task> emitMessageReader)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var response = new HttpResponseMessage();
            var context = new StreamMessageReadContext(
                reader,
                response,
                bufferSize,
                CancellationToken.None,
                json => Task.CompletedTask,
                emitMessageReader,
                (rawContent, message) => Task.CompletedTask,
                () => { });

            await NewlineDelimitedJsonStreamMessageReader.Instance.ReadAsync(context);
        }

        private static StreamMessageReadContext CreateContext(
            StreamReader reader,
            HttpResponseMessage response,
            int bufferSize,
            CancellationToken cancellationToken,
            List<string> messages,
            Action onRead)
        {
            return new StreamMessageReadContext(
                reader,
                response,
                bufferSize,
                cancellationToken,
                message =>
                {
                    messages.Add(message);
                    return Task.CompletedTask;
                },
                (rawContent, message) => Task.CompletedTask,
                onRead ?? (() => { }));
        }
    }
}
