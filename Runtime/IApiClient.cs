using System;
using System.Threading.Tasks;
using ApiClient.Runtime.Requests;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Cache;

namespace ApiClient.Runtime
{
    public interface IApiClient
    {
        /// <summary>
        /// Gets the total number of bytes received in compressed format from HTTP responses.
        /// If the compressed size is unavailable, this property will return 0.
        /// </summary>
        long ResponseTotalCompressedBytes { get; }

        /// <summary>
        /// Gets the total number of bytes received in uncompressed format from HTTP responses.
        /// This property always reflects the uncompressed size, even if compression was not applied.
        /// </summary>
        long ResponseTotalUncompressedBytes { get; }

        UrlCache Cache { get; }

        Task<IHttpResponse> SendHttpRequest(HttpClientRequest req);
        Task<IHttpResponse> SendHttpRequest<E>(HttpClientRequest<E> req);
        Task<IHttpResponse> SendHttpRequest<T, E>(HttpClientRequest<T, E> req);
        Task<IHttpResponse> SendHttpHeadersRequest(HttpClientHeadersRequest req);
        Task<IHttpResponse> SendByteArrayRequest(HttpClientByteArrayRequest req, Action<ByteArrayRequestProgress> progressCallback = null);
        Task SendStreamRequest<T>(HttpClientStreamRequest<T> request, Action<IHttpResponse> OnStreamResponse, Action<TimeSpan> readDelta);
    }
}