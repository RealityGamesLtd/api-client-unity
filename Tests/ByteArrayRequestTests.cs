using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime;
using ApiClient.Runtime.Cache;
using ApiClient.Runtime.HttpResponses;
using NUnit.Framework;
using Polly;
using Polly.Contrib.WaitAndRetry;
using UnityEngine.TestTools;

namespace ApiClient.Tests
{
    public class ByteArrayRequestTests
    {
        private static readonly HttpStatusCode[] _httpStatusCodesWorthRetrying = {
                HttpStatusCode.RequestTimeout, // 408
                HttpStatusCode.InternalServerError, // 500
                HttpStatusCode.BadGateway, // 502
                HttpStatusCode.GatewayTimeout // 504
            };

        public readonly IApiClientConnection ApiClientConnecton = new ApiClientConnection(
            new ApiClientOptions()
            {
                Timeout = TimeSpan.FromSeconds(10),
                Middleware = null,
                RetryPolicy = Policy
                    .Handle<HttpRequestException>()
                    .OrResult<IHttpResponse>(r =>
                    {
                        var validStatusCode = false;
                        if (r is IHttpResponseStatusCode responseWithStatusCode)
                        {
                            validStatusCode = _httpStatusCodesWorthRetrying.Contains(responseWithStatusCode.StatusCode);
                        }
                        return r.IsTimeout ||
                            r.IsNetworkError ||
                            validStatusCode;
                    })
                    // Exponential Backoff
                    .WaitAndRetryAsync(
                        Backoff.DecorrelatedJitterBackoffV2(medianFirstRetryDelay: TimeSpan.FromSeconds(1), retryCount: 2),
                        (response, delay, retryAttempt, context) =>
                        {
                            // Logic to be executed before each retry
                            context["RetryAttempt"] = retryAttempt;
                        }),
                VerboseLogging = false,
            });

        private CancellationTokenSource _cts;

        [SetUp]
        public void Setup()
        {
            _cts = new();
        }

        [TearDown]
        public void TearDown()
        {
            _cts?.Cancel();
        }

        [UnityTest]
        public IEnumerator FetchImageOnce()
        {
            var task = FetchImageTask(null);

            yield return task.AsCoroutine();

            Assert.IsNotNull(task.Result);
            Assert.IsTrue(task.Result.HasNoErrors, $"Request failed!");

            var responseContent = task.Result as HttpResponse<byte[]>;

            Assert.IsNotNull(responseContent.Content);
            Assert.That(responseContent.Content.Length, Is.GreaterThan(0));
            Assert.That(responseContent.Content.Length, Is.EqualTo(35588));
        }

        [UnityTest]
        public IEnumerator FetchImageTwiceWithCache()
        {
            // First request
            var task = FetchImageTask(new CachePolicy() { Expiration = TimeSpan.FromSeconds(60) });

            yield return task.AsCoroutine();

            Assert.IsNotNull(task.Result);
            Assert.IsTrue(task.Result.HasNoErrors, $"Request failed!");
            Assert.IsFalse((task.Result as ICachedHttpResponse).IsFromCache, "Request is expected to not be from cache!");

            var responseContent = task.Result as HttpResponse<byte[]>;


            Assert.IsNotNull(responseContent.Content);
            Assert.That(responseContent.Content.Length, Is.GreaterThan(0));
            Assert.That(responseContent.Content.Length, Is.EqualTo(35588));


            // Second request
            var task2 = FetchImageTask(new CachePolicy() { Expiration = TimeSpan.FromSeconds(60) });

            yield return task2.AsCoroutine();
            Assert.IsTrue((task2.Result as ICachedHttpResponse).IsFromCache, "Request is expected to be from cache!");
        }

        private async Task<IHttpResponse> FetchImageTask(CachePolicy cachePolicy)
        {
            var request = ApiClientConnecton.CreateGetByteArrayRequest(
                "https://www.learningcontainer.com/wp-content/uploads/2020/07/Large-Sample-Image-download-for-Testing.jpg",
                _cts.Token,
                cachePolicy: cachePolicy,
                authentication: new("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIwMTkxYTI3OS05MjI5LTQ3ZjQtYzFlYS00NjVkZmZiODQ2ZjUiLCJyb2xlIjoidGVzdGVyIiwidG9rZW5UeXBlIjoibXdvLWFjY2VzcyIsImV4cCI6MTcyNTU0NDc4NCwiaWF0IjoxNzI1NTQxMTg0fQ.eapdZjyq0-XppLv4jDvgc71RvAumbxhETEJVJY9C2dKdsabIfWvMHgHadXzpomHHXAidJfHP746o9gwoj22mQiToKe7ZtHv0zNCryN6Zf14Elu7i-XaIWPIHtHbM9wfJUg9m9We4eVL7zr0B0zdCzwpKbXVH-Oz08UAM6JZkH2QaT5IQV39cS6HGnMk6McWDn7MtuWNaxaW3tvoDwXF-A7-O1nSMHXoRc2tkd2BcbWQrDADPD9Mq73szvQE7A1G4TrPtyZlkmuNN_UwOmNqB7f2H-llnLmVbuCj7N_idCCb6aW4XB0F5Dx9zU0-YT1EVKWZSN0hBQN0ma33VA70kQNKAXNEFhPwfMFlqqzzmF_mWt8-yy1WBi65yTPLREifkdOGFgub2VUNz-VlH7gnW3xDU22PjTz_DK-50PKc3nc22mBfG8k39qVN17dlSbOrPahefu-gV8dM-dztGZBEOnVEGWbHTvO4bzLwyWb3kxgaqCO9kKnmtr7eXDYgK2a5yY9yu3AJuoZMEhbQtM6xZRtgj2-hnIotpbzTOKS8cb7RQn5EK9i4mqZvopAwW0AgWZGZHIgg4DOZMSHcv4BVHMwOHUULReyCEhU4NHQBZYS_YjwiEX5xv29RS4H0Xu1ZL7osUjhbEwpDbZmIOGCx8Z1fYSOkXXPNqEbkUJDBW2Ns"));

            UnityEngine.Debug.Log($"{nameof(FetchImageTask)}: will start to download image from:{request.Uri.AbsolutePath}");

            var httpResponse = await request.Send((progress) =>
            {
                UnityEngine.Debug.Log($"{nameof(FetchImageTask)}: progress:{(float)progress.TotalBytesRead/progress.ContentSize}");
            });

            UnityEngine.Debug.Log($"{nameof(FetchImageTask)}: finished downloading image from:{request.Uri.AbsolutePath}");

            var responseContent = httpResponse as HttpResponse<byte[]>;
            return responseContent;
        }
    }
}