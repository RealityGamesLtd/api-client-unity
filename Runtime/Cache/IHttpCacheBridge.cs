using System.IO;
using System.Threading.Tasks;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Requests;

namespace ApiClient.Runtime.Cache
{
    /// <summary>
    /// Implemented by <see cref="ApiClient"/>. Exists so <see cref="UrlCache"/>
    /// can reconstruct a typed <see cref="IHttpResponse"/> from cached bytes
    /// without depending on the JSON / HttpClient internals living on
    /// <see cref="ApiClient"/>. The single concrete implementation re-runs the
    /// same deserialisation pipeline used for live 200 responses, so a 304 hit
    /// is observationally identical to a fresh 200 for the caller.
    /// </summary>
    public interface IHttpCacheBridge
    {
        Task<IHttpResponse> RehydrateAsync<T, E>(HttpClientRequest<T, E> req, DiskCacheEntry meta, Stream body);
        Task<IHttpResponse> RehydrateAsync<E>(HttpClientRequest<E> req, DiskCacheEntry meta, Stream body);
        Task<IHttpResponse> RehydrateAsync(HttpClientRequest req, DiskCacheEntry meta, Stream body);
    }
}
