using System;
using ApiClient.Runtime.Requests;

namespace ApiClient.Runtime.Cache
{
    /// <summary>
    /// Extends <see cref="CachePolicy"/> with HTTP-conditional (ETag / Last-Modified)
    /// and disk-persistence opt-ins. Passed to a request via <c>cachePolicy</c>
    /// argument on <c>ApiClientConnection.CreateGet*</c>. Existing callers that pass
    /// a plain <see cref="CachePolicy"/> keep the legacy in-memory TTL behaviour.
    ///
    /// On a populated entry the request gets <c>If-None-Match</c> and/or
    /// <c>If-Modified-Since</c> stamped; on a 304 response the body is hydrated
    /// from disk and returned to the caller as a typed success response.
    /// </summary>
    public class HttpCachePolicy : CachePolicy
    {
        /// <summary>
        /// Inject <c>If-None-Match</c> / <c>If-Modified-Since</c> when a disk entry exists.
        /// </summary>
        public bool UseConditionalRequests { get; set; } = true;

        /// <summary>
        /// Write fresh 200 responses (with <c>ETag</c> or <c>Last-Modified</c>) to disk.
        /// </summary>
        public bool PersistToDisk { get; set; } = true;

        /// <summary>
        /// Optional per-request key segment to isolate caches per user / tenant. The
        /// returned string is mixed into the disk cache key; null/empty = no scope.
        /// Wire this for any endpoint whose response depends on the authenticated
        /// principal — otherwise user A could be served user B's cached entry on a
        /// shared device.
        /// </summary>
        public Func<IHttpRequest, string> VaryKey { get; set; }
    }
}
