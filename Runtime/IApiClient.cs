using System;
using System.Threading.Tasks;
using ApiClient.Runtime.Requests;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Cache;

namespace ApiClient.Runtime
{
    /// <summary>
    /// Represents an HTTP API client capable of sending requests and tracking response metrics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface inherits from <see cref="IDisposable"/>. Callers are responsible for disposing
    /// of any <see cref="IApiClient"/> instances that they create and own, for example by using a
    /// <c>using</c> or <c>await using</c> statement where appropriate.
    /// </para>
    /// <para>
    /// Implementations of <see cref="IApiClient"/> must implement <see cref="IDisposable.Dispose"/>
    /// to release any unmanaged or managed resources they hold (such as underlying HTTP handlers,
    /// network connections, or caches). After an instance has been disposed, it should not be used
    /// to send further requests.
    /// </para>
    /// </remarks>
    public interface IApiClient : IDisposable
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