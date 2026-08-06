using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Threading;
using ApiClient.Runtime.Cache;
using ApiClient.Runtime.Requests;

namespace ApiClient.Runtime
{
    public interface IApiClientConnection
    {
        IApiClient APIClient { get; }
        void SetDefaultHeader(string key, string value);
        HttpClientStreamRequest<T> CreateGetStreamRequest<T>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, string priorityLane = null);
        /// <param name="allowCompressedResponse">
        /// Opt in to <c>Accept-Encoding: gzip</c> for this stream. Off by default: it trades wire
        /// bytes for delivery shape (a proxy that buffers gzip output batches messages instead of
        /// streaming them) and, on Mono, gzipped bodies are pulled off the TLS stream in 4 KB
        /// pieces, which multiplies SslStream record-buffer churn — see the note on
        /// <c>StreamReadBufferSizeBytes</c> in <see cref="ApiClient"/>. Measure both before enabling.
        /// </param>
        HttpClientStreamRequest<T> CreatePutStreamRequest<T>(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, string priorityLane = null, bool allowCompressedResponse = false);
        HttpClientByteArrayRequest CreateGetByteArrayRequest(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, CachePolicy cachePolicy = null, string priorityLane = null);
        HttpClientRequest<T, E> CreateGet<T, E>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, CachePolicy cachePolicy = null, string priorityLane = null);
        HttpClientRequest<T> CreateGet<T>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, CachePolicy cachePolicy = null, string priorityLane = null);
        HttpClientRequest CreateGet(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, CachePolicy cachePolicy = null, string priorityLane = null);
        HttpClientHeadersRequest CreateGetHeadersOnlyRequest(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, CachePolicy cachePolicy = null, string priorityLane = null);
        HttpClientRequest<T> CreatePost<T>(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, string priorityLane = null);
        HttpClientRequest<T, E> CreatePost<T, E>(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, string priorityLane = null);
        HttpClientRequest CreatePost(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, string priorityLane = null);
        HttpClientRequest<T> CreatePut<T>(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, string priorityLane = null);
        HttpClientRequest<T, E> CreatePut<T, E>(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, string priorityLane = null);
        HttpClientRequest CreatePut(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, string priorityLane = null);
        HttpClientRequest<T> CreateDelete<T>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, string priorityLane = null);
        HttpClientRequest<T, E> CreateDelete<T, E>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, string priorityLane = null);
        HttpClientRequest CreateDelete(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true, string priorityLane = null);
    }
}
