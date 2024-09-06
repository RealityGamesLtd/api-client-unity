using ApiClient.Runtime.Cache;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Requests;
using NUnit.Framework;
using System;
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

namespace ApiClient.Tests
{
    public class UrlCacheTests
    {
        private UrlCache urlCache;

        [OneTimeSetUp]
        public void CreateUrlCache()
        {
            urlCache = new UrlCache();
        }

        [Test(
            Author = "Mateusz Młynek",
            Description = @"Make request, cache response, return cached data for another attempt")]
        public void GetDataFromCache()
        {
            // create request
            var request1 = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, "url_1"),
                null,
                new System.Threading.CancellationTokenSource().Token);

            // 200 response should be cached
            var response1 = urlCache.Process(
                request1,
                new CachePolicy(),
                () => GetResponseFor(request1.Uri, HttpStatusCode.OK)).Result;

            // That response shouldn't be from cache
            Assert.IsFalse((response1 as ICachedHttpResponse).IsFromCache);

            // simulate making new request for the same Uri
            var request2 = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, "url_1"),
                null,
                new System.Threading.CancellationTokenSource().Token);

            var response2 = urlCache.Process(
                request2,
                null,
                () => GetResponseFor(request2.Uri, HttpStatusCode.OK)).Result;

            // That response should be from cache
            Assert.IsTrue((response2 as ICachedHttpResponse).IsFromCache);
        }

        [Test(
            Author = "Mateusz Młynek",
            Description = @"Make request, don't cache response, return fresh data for another attempt")]
        public void DontGetDataFromCache()
        {
            // create request
            var request1 = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, "url_1"),
                null,
                new System.Threading.CancellationTokenSource().Token);

            // 200 response, but don't cache
            var response1 = urlCache.Process(
                request1,
                null,
                () => GetResponseFor(request1.Uri, HttpStatusCode.OK)).Result;

            // That response shouldn't be from cache
            Assert.IsFalse((response1 as ICachedHttpResponse).IsFromCache);

            // simulate making new request for the same Uri
            var request2 = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, "url_1"),
                null,
                new System.Threading.CancellationTokenSource().Token);

            var response2 = urlCache.Process(
                request2,
                null,
                () => GetResponseFor(request2.Uri, HttpStatusCode.OK)).Result;

            // That response shouldn't be from cache
            Assert.IsFalse((response2 as ICachedHttpResponse).IsFromCache);
        }

        [UnityTest]
        [Author("Mateusz Młynek")]
        [Description(@"Check if expiration works properly. Make a request, cache response for 1s, 
                        immediately make another attempt which should be from cache, wait for cache 
                        to expire and make another attempt")]
        public IEnumerator Expiration()
        {
            // REQUEST NO 1
            // create request
            var request1 = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, "url_1"),
                null,
                new System.Threading.CancellationTokenSource().Token);

            // 200 response, but don't cache
            var response1 = urlCache.Process(
                request1,
                new CachePolicy() { Expiration = TimeSpan.FromSeconds(1) }, // cache for one second
                () => GetResponseFor(request1.Uri, HttpStatusCode.OK)).Result;

            // That response shouldn't be from cache
            Assert.IsFalse((response1 as ICachedHttpResponse).IsFromCache);

            // REQUEST NO 2
            // simulate making new request for the same Uri
            var request2 = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, "url_1"),
                null,
                new System.Threading.CancellationTokenSource().Token);
            var response2 = urlCache.Process(
                request2,
                null,
                () => GetResponseFor(request2.Uri, HttpStatusCode.OK)).Result;

            // That response should be from cache
            Assert.IsTrue((response2 as ICachedHttpResponse).IsFromCache);

            // wait for over one second for the cache to expire
            yield return new WaitForSecondsRealtime(2);

            // REQUEST NO 3
            // simulate making new request for the same Uri
            var request3 = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, "url_1"),
                null,
                new System.Threading.CancellationTokenSource().Token);
            var response3 = urlCache.Process(
                request3,
                null,
                () => GetResponseFor(request3.Uri, HttpStatusCode.OK)).Result;

            // That response shouldn't be from cache
            Assert.IsFalse((response3 as ICachedHttpResponse).IsFromCache);
        }

        [UnityTest]
        [Author("Mateusz Młynek")]
        [Description(@"Check if force expire works properly. Make a request, cache response for 1s, 
                        immediately make another attempt which should be from cache, then make another request
                        with force expire.")]
        public IEnumerator ForceExpiration()
        {
            // REQUEST NO 1
            // create request
            var request1 = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, "url_1"),
                null,
                new System.Threading.CancellationTokenSource().Token);

            // 200 response, but don't cache
            var response1 = urlCache.Process(
                request1,
                new CachePolicy() { Expiration = TimeSpan.FromSeconds(60) }, // cache for one second
                () => GetResponseFor(request1.Uri, HttpStatusCode.OK)).Result;

            // That response shouldn't be from cache
            Assert.IsFalse((response1 as ICachedHttpResponse).IsFromCache);

            // At this point we have cached response which should be returned when we make this request again.

            // REQUEST NO 2
            // simulate making new request for the same Uri
            var request2 = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, "url_1"),
                null,
                new System.Threading.CancellationTokenSource().Token);
            var response2 = urlCache.Process(
                request2,
                new CachePolicy() { ForceExpire = true }, // mark as force expire
                () => GetResponseFor(request2.Uri, HttpStatusCode.NotFound)).Result;

            // That response should not be from cache because we ForceExpire
            // The previous response should be dropped, as there is an error the new one shouldn't be cached.
            Assert.IsFalse((response2 as ICachedHttpResponse).IsFromCache);
            Assert.IsTrue((response2 as IHttpResponseStatusCode).StatusCode == HttpStatusCode.NotFound);

            // REQUEST NO 3
            // simulate making new request for the same Uri
            var request3 = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, "url_1"),
                null,
                new System.Threading.CancellationTokenSource().Token);
            var response3 = urlCache.Process(
                request3,
                null,
                () => GetResponseFor(request3.Uri, HttpStatusCode.OK)).Result;

            // That response shouldn't be from cache
            Assert.IsFalse((response3 as ICachedHttpResponse).IsFromCache);
            // And it should have OK status code
            Assert.IsTrue((response3 as IHttpResponseStatusCode).StatusCode == HttpStatusCode.OK);

            // wait for over one second for the cache to expire
            yield return new WaitForSecondsRealtime(2);
        }

        /// <summary>
        /// Helper method that will prepare response object
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="httpStatusCode"></param>
        /// <returns></returns>
        private static Task<IHttpResponse> GetResponseFor(Uri uri, HttpStatusCode httpStatusCode)
        {
            var t = new TaskCompletionSource<IHttpResponse>();
            t.SetResult(new HttpResponse<byte[]>(
                    new byte[] { 2, 155, 3 },
                    null,
                    null,
                    null,
                    uri,
                    httpStatusCode));
            return t.Task;
        }
    }
}
