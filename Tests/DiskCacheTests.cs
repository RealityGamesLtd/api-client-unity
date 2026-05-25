using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime;
using ApiClient.Runtime.Cache;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Requests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ApiClient.Tests
{
    public class DiskCacheTests
    {
        private string _tempRoot;
        private FileSystemHttpDiskCacheStore _store;

        [SetUp]
        public void Setup()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "ApiClientDiskCacheTests_" + Guid.NewGuid().ToString("N"));
            _store = new FileSystemHttpDiskCacheStore(_tempRoot, maxBytes: 4 * 1024 * 1024);
        }

        [TearDown]
        public void TearDown()
        {
            _store?.Dispose();
            try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, true); } catch { }
        }

        [Test]
        [Author("Mateusz Młynek")]
        [Description("Write then read meta + body — round trip should match.")]
        public async Task WriteReadRoundtrip()
        {
            const string key = "abc123";
            var bytes = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");
            var entry = new DiskCacheEntry
            {
                ETag = "\"v1\"",
                LastModified = "Wed, 21 Oct 2026 07:28:00 GMT",
                ContentType = "application/json",
                StatusCode = 200,
                SelectedHeaders = new Dictionary<string, string>
                {
                    { "ETag", "\"v1\"" },
                    { "Content-Type", "application/json" },
                },
            };

            await _store.WriteAsync(key, entry, bytes, CancellationToken.None);

            var readMeta = await _store.TryReadMetaAsync(key, CancellationToken.None);
            Assert.IsNotNull(readMeta);
            Assert.AreEqual("\"v1\"", readMeta.ETag);
            Assert.AreEqual(bytes.LongLength, readMeta.BodyLength);

            using var bodyStream = await _store.OpenBodyAsync(key, CancellationToken.None);
            Assert.IsNotNull(bodyStream);
            using var ms = new MemoryStream();
            await bodyStream.CopyToAsync(ms);
            Assert.AreEqual(bytes, ms.ToArray());
        }

        [Test]
        [Author("Mateusz Młynek")]
        [Description("Missing key returns null meta — no throw.")]
        public async Task MissingReturnsNull()
        {
            var meta = await _store.TryReadMetaAsync("does-not-exist", CancellationToken.None);
            Assert.IsNull(meta);
        }

        [Test]
        [Author("Mateusz Młynek")]
        [Description("Overwrite with new ETag swaps meta atomically.")]
        public async Task OverwriteSwapsEtag()
        {
            const string key = "swap1";
            var b1 = Encoding.UTF8.GetBytes("v1");
            var b2 = Encoding.UTF8.GetBytes("v2-longer");

            await _store.WriteAsync(key, new DiskCacheEntry { ETag = "\"e1\"", ContentType = "application/json" }, b1, CancellationToken.None);
            await _store.WriteAsync(key, new DiskCacheEntry { ETag = "\"e2\"", ContentType = "application/json" }, b2, CancellationToken.None);

            var meta = await _store.TryReadMetaAsync(key, CancellationToken.None);
            Assert.AreEqual("\"e2\"", meta.ETag);
            using var bodyStream = await _store.OpenBodyAsync(key, CancellationToken.None);
            using var ms = new MemoryStream();
            await bodyStream.CopyToAsync(ms);
            Assert.AreEqual(b2, ms.ToArray());
        }

        [Test]
        [Author("Mateusz Młynek")]
        [Description("Concurrent writes to same key serialise without corruption.")]
        public async Task ConcurrentSameKeyWritesNoCorruption()
        {
            const string key = "concur";
            var bytes = new byte[64 * 1024];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i & 0xFF);

            var tasks = new List<Task>();
            for (int i = 0; i < 8; i++)
            {
                var idx = i;
                tasks.Add(_store.WriteAsync(key, new DiskCacheEntry { ETag = "\"e" + idx + "\"", ContentType = "application/json" }, bytes, CancellationToken.None));
            }
            await Task.WhenAll(tasks);

            var meta = await _store.TryReadMetaAsync(key, CancellationToken.None);
            Assert.IsNotNull(meta);
            using var bodyStream = await _store.OpenBodyAsync(key, CancellationToken.None);
            using var ms = new MemoryStream();
            await bodyStream.CopyToAsync(ms);
            Assert.AreEqual(bytes.Length, ms.Length);
        }

        [UnityTest]
        [Author("Mateusz Młynek")]
        [Description("Writing past the cap triggers LRU eviction down to ~90%.")]
        public IEnumerator LruEvictionUnderCap()
        {
            // 1 MB cap, 200 KB entries → expect ~4–5 entries surviving (90% high-water).
            _store.Dispose();
            _store = new FileSystemHttpDiskCacheStore(_tempRoot, maxBytes: 1024 * 1024);

            var payload = new byte[200 * 1024];

            for (int i = 0; i < 10; i++)
            {
                var t = _store.WriteAsync("k" + i, new DiskCacheEntry { ETag = "\"e\"", ContentType = "application/json" }, payload, CancellationToken.None);
                while (!t.IsCompleted) yield return null;
            }

            // give the background eviction a moment to run
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_store.ApproxSizeBytes > (long)(1024 * 1024 * 0.95) && sw.ElapsedMilliseconds < 2000)
            {
                yield return null;
            }

            Assert.LessOrEqual(_store.ApproxSizeBytes, 1024L * 1024);
        }

        [Test]
        [Author("Mateusz Młynek")]
        [Description("UrlCache stamps If-None-Match and rehydrates a 304 response from disk.")]
        public async Task UrlCache_304_RehydratesFromDisk()
        {
            var urlCache = new UrlCache();
            var bridge = new RecordingBridge();
            urlCache.AttachDiskCache(_store, bridge);

            var uri = new Uri("https://example.test/data");
            var policy = new HttpCachePolicy { UseConditionalRequests = true, PersistToDisk = true };

            // First request — 200 OK with ETag, body cached
            var firstReq = MakeRequest(uri);
            var firstResp = await urlCache.Process(
                firstReq,
                policy,
                () => Task.FromResult<IHttpResponse>(new TestHttpResponse(uri, HttpStatusCode.OK, "\"v1\"", "{\"a\":1}")),
                (m, b) => bridge.RehydrateAsync<int, int>(null, m, b));

            Assert.IsFalse(((ICachedHttpResponse)firstResp).IsFromCache);

            // Allow fire-and-forget disk write to settle
            for (int i = 0; i < 50 && _store.ApproxSizeBytes == 0; i++) await Task.Delay(20);
            Assert.Greater(_store.ApproxSizeBytes, 0);

            // Second request — verify If-None-Match header is stamped and 304 hydrates body
            var secondReq = MakeRequest(uri);
            var secondResp = await urlCache.Process(
                secondReq,
                policy,
                () =>
                {
                    Assert.IsTrue(secondReq.RequestMessage.Headers.TryGetValues("If-None-Match", out var vals));
                    return Task.FromResult<IHttpResponse>(new TestHttpResponse(uri, HttpStatusCode.NotModified, null, null));
                },
                (m, b) => bridge.RehydrateAsync(uri, m, b));

            Assert.IsTrue(((ICachedHttpResponse)secondResp).IsFromCache);
            Assert.IsTrue(((ICachedHttpResponse)secondResp).IsConditionalHit);
            Assert.IsTrue(bridge.WasCalled);
        }

        private static HttpClientByteArrayRequest MakeRequest(Uri uri)
        {
            // ByteArray request is the simplest IHttpRequest impl with a usable ctor.
            return new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, uri),
                null,
                CancellationToken.None);
        }

        // Minimal IHttpResponse stand-in carrying status, body, ETag header.
        private sealed class TestHttpResponse : IHttpResponse, IHttpResponseStatusCode, IHttpResponseBody, ICachedHttpResponse
        {
            public TestHttpResponse(Uri uri, HttpStatusCode status, string etag, string body)
            {
                RequestUri = uri;
                StatusCode = status;
                Body = body;
                Headers = new Dictionary<string, string>();
                if (etag != null) Headers["ETag"] = etag;
                ContentHeaders = new Dictionary<string, string>
                {
                    { "Content-Type", "application/json" },
                };
            }

            public bool IsClientError => (int)StatusCode >= 400 && (int)StatusCode < 500;
            public bool IsServerError => (int)StatusCode >= 500;
            public bool IsContentParsingError => false;
            public bool IsNetworkError => false;
            public bool IsAborted => false;
            public bool IsTimeout => false;
            public HttpMethod RequestMethod => HttpMethod.Get;
            public Uri RequestUri { get; }
            public HttpStatusCode StatusCode { get; }
            public Dictionary<string, string> Headers { get; }
            public Dictionary<string, string> ContentHeaders { get; }
            public string Body { get; }
            bool ICachedHttpResponse.IsFromCache { get; set; }
            bool ICachedHttpResponse.IsConditionalHit { get; set; }
            public long CacheContentSize() => 1;
        }

        private sealed class RecordingBridge : IHttpCacheBridge
        {
            public bool WasCalled;

            public Task<IHttpResponse> RehydrateAsync(Uri uri, DiskCacheEntry meta, Stream body)
            {
                WasCalled = true;
                using var ms = new MemoryStream();
                body.CopyTo(ms);
                IHttpResponse resp = new TestHttpResponse(uri, HttpStatusCode.OK, meta.ETag, Encoding.UTF8.GetString(ms.ToArray()));
                return Task.FromResult(resp);
            }

            public Task<IHttpResponse> RehydrateAsync<T, E>(HttpClientRequest<T, E> req, DiskCacheEntry meta, Stream body)
            {
                WasCalled = true;
                IHttpResponse resp = new TestHttpResponse(req?.Uri ?? new Uri("https://example.test/x"), HttpStatusCode.OK, meta.ETag, "");
                return Task.FromResult(resp);
            }

            public Task<IHttpResponse> RehydrateAsync<E>(HttpClientRequest<E> req, DiskCacheEntry meta, Stream body) =>
                RehydrateAsync<E, E>(null, meta, body);

            public Task<IHttpResponse> RehydrateAsync(HttpClientRequest req, DiskCacheEntry meta, Stream body) =>
                RehydrateAsync<int, int>(null, meta, body);
        }
    }
}
