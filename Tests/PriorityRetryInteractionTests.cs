using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Priority;
using NUnit.Framework;
using Polly;
using Polly.Contrib.WaitAndRetry;

namespace ApiClient.Tests
{
    /// <summary>
    /// Asserts the priority handshake (bulkhead slot + lane scope) is acquired and
    /// released per Polly retry attempt rather than once per request. The slot must
    /// be free during backoff intervals so other lanes / queued requests can make
    /// progress, and the slot must stay continuously held across all chunks of a
    /// chunked Range download within a single attempt.
    /// </summary>
    [TestFixture]
    public class PriorityRetryInteractionTests
    {
        private HttpListener _listener;
        private int _port;
        private Func<HttpListenerContext, Task> _responder;
        private CancellationTokenSource _acceptCts;
        private Task _acceptLoop;

        [SetUp]
        public void SetUp()
        {
            _port = GetFreeTcpPort();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
            _acceptCts = new CancellationTokenSource();
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                _acceptCts?.Cancel();
                _listener?.Stop();
                _listener?.Close();
            }
            catch { }
            try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _acceptCts?.Dispose();
        }

        // ──────────────────── 1. Slot released during backoff ────────────────────

        [Test]
        public async Task SlotReleasedDuringBackoff_AllowsCrossLaneProgress()
        {
            using var coord = new RequestPriorityCoordinator(new[]
            {
                new LaneConfig("a") { MaxConcurrent = 1, FairnessMaxPause = TimeSpan.FromSeconds(1) }
            });

            var options = BuildOptions(coord, transientBackoff: TimeSpan.FromMilliseconds(500), transientRetries: 1);
            using var apiClient = new ApiClient.Runtime.ApiClient(options);
            var conn = new ApiClientConnection(options, apiClient);

            var aFirstRequestReached = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var aRequestCount = 0;
            _responder = async ctx =>
            {
                var path = ctx.Request.Url.AbsolutePath;
                if (path == "/a")
                {
                    var n = Interlocked.Increment(ref aRequestCount);
                    if (n == 1)
                    {
                        aFirstRequestReached.TrySetResult(true);
                        ctx.Response.StatusCode = 502;
                    }
                    else
                    {
                        ctx.Response.StatusCode = 200;
                    }
                }
                else if (path == "/b")
                {
                    ctx.Response.StatusCode = 200;
                }
                ctx.Response.Close();
                await Task.CompletedTask;
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Fire A first. A will: attempt 1 → 502, backoff ~500ms, attempt 2 → 200.
            var reqA = conn.CreateGet($"http://127.0.0.1:{_port}/a", cts.Token, priorityLane: "a");
            var aTask = reqA.Send();

            // Wait until A's first attempt has hit the server (so we know A is in backoff).
            await aFirstRequestReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Fire B during A's backoff. Pre-refactor B would wait for A's entire retry
            // chain (~500ms + RTT). Post-refactor B should grab the slot during backoff.
            var bSw = Stopwatch.StartNew();
            var reqB = conn.CreateGet($"http://127.0.0.1:{_port}/b", cts.Token, priorityLane: "a");
            var bResp = await reqB.Send();
            bSw.Stop();

            var aResp = await aTask;

            Assert.That(bResp, Is.Not.Null);
            Assert.That((bResp as IHttpResponseStatusCode)?.StatusCode, Is.EqualTo(HttpStatusCode.OK), "B should succeed");
            Assert.That((aResp as IHttpResponseStatusCode)?.StatusCode, Is.EqualTo(HttpStatusCode.OK), "A should succeed after retry");

            // Slot-released-during-backoff guarantee: B completes well within the 500ms
            // backoff window. Generous bound to absorb scheduling jitter.
            Assert.That(bSw.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(400)),
                $"B took {bSw.ElapsedMilliseconds}ms — slot was not released during A's backoff.");

            Assert.That(coord.InFlight("a"), Is.EqualTo(0), "no slot leak after both complete");
        }

        // ──────────────────── 2. Slot held across chunked Range download ─────────

        [Test]
        public async Task SlotHeldAcrossChunkedRangeDownload_WithinSingleAttempt()
        {
            using var coord = new RequestPriorityCoordinator(new[]
            {
                new LaneConfig("asset")
                {
                    MaxConcurrent = 1,
                    ChunkedRangeDownloads = true,
                    FairnessMaxPause = TimeSpan.FromSeconds(1)
                }
            });

            var options = BuildOptions(coord);
            options.AutomaticDecompression = DecompressionMethods.None;
            options.RangeDownload = new RangeChunkedDownloadOptions { ChunkSizeBytes = 256 };
            using var apiClient = new ApiClient.Runtime.ApiClient(options);
            var conn = new ApiClientConnection(options, apiClient);

            var payload = new byte[1024];
            for (var i = 0; i < payload.Length; i++) payload[i] = (byte)(i & 0xFF);

            _responder = async ctx =>
            {
                if (ctx.Request.Url.AbsolutePath == "/asset")
                {
                    await ServeRangeAsync(ctx, payload, chunkDelayMs: 50);
                }
                ctx.Response.Close();
            };

            var samples = new ConcurrentQueue<int>();
            var stopWatcher = false;
            var watcher = Task.Run(async () =>
            {
                while (!stopWatcher)
                {
                    samples.Enqueue(coord.InFlight("asset"));
                    await Task.Delay(5);
                }
            });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var req = conn.CreateGetByteArrayRequest($"http://127.0.0.1:{_port}/asset", cts.Token, priorityLane: "asset");
            var resp = await req.Send(null);

            stopWatcher = true;
            await watcher;

            Assert.That(resp, Is.Not.Null);
            Assert.That(resp.HasNoErrors, Is.True, "chunked download should succeed");
            var body = (resp as HttpResponse<byte[]>)?.Content;
            Assert.That(body, Is.Not.Null);
            Assert.That(body.Length, Is.EqualTo(payload.Length));
            Assert.That(body.SequenceEqual(payload), Is.True);

            // Trim leading samples taken before the download acquired its slot, and
            // trailing samples taken after disposal — the active window in between must
            // never drop to 0, proving the slot is held continuously across all chunks.
            var arr = samples.ToArray();
            var firstActive = Array.FindIndex(arr, x => x > 0);
            var lastActive = Array.FindLastIndex(arr, x => x > 0);
            Assert.That(firstActive, Is.GreaterThanOrEqualTo(0), "watcher should have seen at least one active sample");
            var middle = new int[lastActive - firstActive + 1];
            Array.Copy(arr, firstActive, middle, 0, middle.Length);
            Assert.That(middle.All(x => x == 1), Is.True,
                $"slot count not stable at 1 during download: [{string.Join(",", middle)}]");
            Assert.That(coord.InFlight("asset"), Is.EqualTo(0), "slot released after download");
        }

        // ──────────────────── 3. Cancellation during backoff ─────────────────────

        [Test]
        public async Task HandshakeDisposedOnCancellationDuringBackoff()
        {
            using var coord = new RequestPriorityCoordinator(new[]
            {
                new LaneConfig("a") { MaxConcurrent = 1, FairnessMaxPause = TimeSpan.FromSeconds(1) }
            });

            // 2s backoff so we have a comfortable window to cancel and observe.
            var options = BuildOptions(coord, transientBackoff: TimeSpan.FromSeconds(2), transientRetries: 2);
            using var apiClient = new ApiClient.Runtime.ApiClient(options);
            var conn = new ApiClientConnection(options, apiClient);

            var firstHit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _responder = async ctx =>
            {
                firstHit.TrySetResult(true);
                ctx.Response.StatusCode = 502;
                ctx.Response.Close();
                await Task.CompletedTask;
            };

            using var cts = new CancellationTokenSource();
            var req = conn.CreateGet($"http://127.0.0.1:{_port}/a", cts.Token, priorityLane: "a");
            var sendTask = req.Send();

            await firstHit.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // Give the callback time to return and Polly to enter backoff.
            await Task.Delay(200);

            Assert.That(coord.InFlight("a"), Is.EqualTo(0),
                "slot must be released while Polly is sleeping in DecorrelatedJitter backoff.");

            cts.Cancel();

            var resp = await sendTask;
            Assert.That(resp.IsAborted, Is.True, "cancelled request should yield AbortedHttpResponse");
            Assert.That(coord.InFlight("a"), Is.EqualTo(0), "no slot leak after cancellation");
        }

        // ──────────────────── 4. Polly context flow survives the refactor ────────

        [Test]
        public async Task AuthHeaderSwapPersistsAcrossPerAttemptHandshake()
        {
            using var coord = new RequestPriorityCoordinator(new[]
            {
                new LaneConfig("a") { MaxConcurrent = 1, FairnessMaxPause = TimeSpan.FromSeconds(1) }
            });

            var newAuth = new AuthenticationHeaderValue("Bearer", "new-token");

            // Wrap a no-op transient policy with a real 401 policy that swaps the auth header
            // via Polly's context — the exact path ApiClient relies on at the
            // newAuthenticationHeaderValue context key.
            var transientNoop = Policy
                .Handle<HttpRequestException>()
                .OrResult<IHttpResponse>(_ => false)
                .RetryAsync(0);

            var authRetry = Policy
                .Handle<HttpRequestException>()
                .OrResult<IHttpResponse>(r =>
                    r is IHttpResponseStatusCode s && s.StatusCode == HttpStatusCode.Unauthorized)
                .WaitAndRetryAsync(
                    new[] { TimeSpan.FromMilliseconds(10) },
                    (response, delay, retryAttempt, context) =>
                    {
                        context["newAuthenticationHeaderValue"] = newAuth;
                    });

            var options = BuildOptions(coord);
            options.RetryPolicies = Policy.WrapAsync(transientNoop, authRetry);
            using var apiClient = new ApiClient.Runtime.ApiClient(options);
            var conn = new ApiClientConnection(options, apiClient);

            var seenAuth = new ConcurrentQueue<string>();
            _responder = async ctx =>
            {
                var auth = ctx.Request.Headers["Authorization"] ?? string.Empty;
                seenAuth.Enqueue(auth);
                if (auth.Contains("new-token"))
                {
                    ctx.Response.StatusCode = 200;
                }
                else
                {
                    ctx.Response.StatusCode = 401;
                }
                ctx.Response.Close();
                await Task.CompletedTask;
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var req = conn.CreateGet($"http://127.0.0.1:{_port}/a", cts.Token, priorityLane: "a");
            var resp = await req.Send();

            var observations = seenAuth.ToArray();
            Assert.That(observations.Length, Is.EqualTo(2),
                "exactly two server hits expected: 401 then retry with new auth.");
            Assert.That(observations[0], Does.Not.Contain("new-token"), "first attempt has no swapped auth");
            Assert.That(observations[1], Does.Contain("new-token"), "second attempt carries swapped auth header");
            Assert.That((resp as IHttpResponseStatusCode)?.StatusCode, Is.EqualTo(HttpStatusCode.OK),
                "retry after auth swap must succeed");
            Assert.That(coord.InFlight("a"), Is.EqualTo(0), "no slot leak after retry chain ends");
        }

        // ──────────────────── helpers ────────────────────────────────────────────

        private static int GetFreeTcpPort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            try { return ((IPEndPoint)l.LocalEndpoint).Port; }
            finally { l.Stop(); }
        }

        private async Task AcceptLoopAsync()
        {
            while (!_acceptCts.IsCancellationRequested && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { return; }
                catch (HttpListenerException) { return; }
                catch (InvalidOperationException) { return; }

                var responder = _responder;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        if (responder != null) await responder(ctx).ConfigureAwait(false);
                        else { ctx.Response.StatusCode = 404; ctx.Response.Close(); }
                    }
                    catch
                    {
                        try { ctx.Response.Abort(); } catch { }
                    }
                });
            }
        }

        private static async Task ServeRangeAsync(HttpListenerContext ctx, byte[] payload, int chunkDelayMs)
        {
            var rangeHeader = ctx.Request.Headers["Range"];
            if (string.IsNullOrEmpty(rangeHeader) || !rangeHeader.StartsWith("bytes="))
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentLength64 = payload.Length;
                await ctx.Response.OutputStream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
                return;
            }

            var spec = rangeHeader.Substring("bytes=".Length);
            var parts = spec.Split('-');
            var from = long.Parse(parts[0]);
            var to = parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) ? long.Parse(parts[1]) : payload.Length - 1;
            if (to >= payload.Length) to = payload.Length - 1;
            var len = (int)(to - from + 1);

            ctx.Response.StatusCode = 206;
            ctx.Response.Headers["Content-Range"] = $"bytes {from}-{to}/{payload.Length}";
            ctx.Response.ContentLength64 = len;

            if (chunkDelayMs > 0) await Task.Delay(chunkDelayMs).ConfigureAwait(false);
            await ctx.Response.OutputStream.WriteAsync(payload, (int)from, len).ConfigureAwait(false);
        }

        private static ApiClientOptions BuildOptions(
            RequestPriorityCoordinator coord,
            TimeSpan? transientBackoff = null,
            int transientRetries = 0)
        {
            var transientRetryCodes = new[]
            {
                HttpStatusCode.RequestTimeout,
                HttpStatusCode.InternalServerError,
                HttpStatusCode.BadGateway,
                HttpStatusCode.GatewayTimeout
            };

            var backoffs = transientBackoff.HasValue
                ? Enumerable.Repeat(transientBackoff.Value, transientRetries)
                : Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromMilliseconds(1), transientRetries);

            var transient = Policy
                .Handle<HttpRequestException>()
                .OrResult<IHttpResponse>(r =>
                    r.IsTimeout
                    || r.IsNetworkError
                    || (r is IHttpResponseStatusCode s && transientRetryCodes.Contains(s.StatusCode)))
                .WaitAndRetryAsync(backoffs, (_, __, ___, ____) => { });

            var auth = Policy
                .Handle<HttpRequestException>()
                .OrResult<IHttpResponse>(_ => false)
                .RetryAsync(0);

            return new ApiClientOptions
            {
                Timeout = TimeSpan.FromSeconds(10),
                RetryPolicies = Policy.WrapAsync(transient, auth),
                PriorityCoordinator = coord,
                VerboseLogging = false,
                BodyLogging = false
            };
        }
    }

    // Backport of Task.WaitAsync(TimeSpan) for older targets — added so the tests
    // do not depend on net6+.
    internal static class TaskTimeoutExtensions
    {
        public static async Task WaitAsync(this Task task, TimeSpan timeout)
        {
            var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != task) throw new TimeoutException($"Task did not complete within {timeout}.");
            await task.ConfigureAwait(false);
        }
    }
}
