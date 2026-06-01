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
        public void CancelledTokenThrowsWithoutEmitting()
        {
            var messages = new List<string>();

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":\"a\"}\n{\"id\":\"b\"}\n"));
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var response = new HttpResponseMessage();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var context = CreateContext(reader, response, 4096, cts.Token, messages, null);

            Assert.CatchAsync<OperationCanceledException>(
                () => NewlineDelimitedJsonStreamMessageReader.Instance.ReadAsync(context));
            CollectionAssert.IsEmpty(messages);
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
