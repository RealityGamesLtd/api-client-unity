using System;
using System.Threading.Tasks;
using ApiClient.Runtime.Requests;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Cache;

namespace ApiClient.Runtime
{
    public class ApiClientMock : IApiClient
    {
        public long ResponseTotalCompressedBytes { get; set; }
        public long ResponseTotalUncompressedBytes { get; set; }

        // Custom delegates for mocking responses
        public Func<HttpClientRequest, Task<IHttpResponse>> SendHttpRequestFunc { get; set; }
        public Func<HttpClientRequest<object>, Task<IHttpResponse>> SendHttpRequestGenericFunc { get; set; }
        public Func<HttpClientRequest<object, object>, Task<IHttpResponse>> SendHttpRequestDoubleGenericFunc { get; set; }
        public Func<HttpClientHeadersRequest, Task<IHttpResponse>> SendHttpHeadersRequestFunc { get; set; }
        public Func<HttpClientByteArrayRequest, Action<ByteArrayRequestProgress>, Task<IHttpResponse>> SendByteArrayRequestFunc { get; set; }
        public Func<HttpClientStreamRequest<object>, Action<IHttpResponse>, Action<TimeSpan>, Task> SendStreamRequestFunc { get; set; }

        public UrlCache Cache { get; } = new UrlCache();

        public Task<IHttpResponse> SendHttpRequest(HttpClientRequest req)
        {
            if (SendHttpRequestFunc != null)
                return SendHttpRequestFunc(req);
            throw new NotImplementedException("Mock response not set for SendHttpRequest");
        }

        public Task<IHttpResponse> SendHttpHeadersRequest(HttpClientHeadersRequest req)
        {
            if (SendHttpHeadersRequestFunc != null)
                return SendHttpHeadersRequestFunc(req);
            throw new NotImplementedException("Mock response not set for SendHttpHeadersRequest");
        }

        public Task<IHttpResponse> SendByteArrayRequest(HttpClientByteArrayRequest req, Action<ByteArrayRequestProgress> progressCallback = null)
        {
            if (SendByteArrayRequestFunc != null)
                return SendByteArrayRequestFunc(req, progressCallback);
            throw new NotImplementedException("Mock response not set for SendByteArrayRequest");
        }

        public Task SendStreamRequest<T>(HttpClientStreamRequest<T> request, Action<IHttpResponse> OnStreamResponse, Action<TimeSpan> readDelta)
        {
            if (SendStreamRequestFunc != null)
                return SendStreamRequestFunc(request as HttpClientStreamRequest<object>, OnStreamResponse, readDelta);
            throw new NotImplementedException("Mock response not set for SendStreamRequest");
        }

        public Task<IHttpResponse> SendHttpRequest<E>(HttpClientRequest<E> req)
        {
            if (SendHttpRequestGenericFunc != null)
                return SendHttpRequestGenericFunc(req as HttpClientRequest<object>);
            throw new NotImplementedException("Mock response not set for SendHttpRequest<E>");
        }

        public Task<IHttpResponse> SendHttpRequest<T, E>(HttpClientRequest<T, E> req)
        {
            if (SendHttpRequestDoubleGenericFunc != null)
                return SendHttpRequestDoubleGenericFunc(req as HttpClientRequest<object, object>);
            throw new NotImplementedException("Mock response not set for SendHttpRequest<T, E>");
        }
    }
}
