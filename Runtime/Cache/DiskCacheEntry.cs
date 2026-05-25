using System;
using System.Collections.Generic;

namespace ApiClient.Runtime.Cache
{
    /// <summary>
    /// Sidecar metadata persisted next to a cached body. Holds the validators
    /// (ETag / Last-Modified) needed to send a conditional GET and enough
    /// header context to reconstruct a typed response on a 304 hit.
    /// </summary>
    [Serializable]
    public class DiskCacheEntry
    {
        /// <summary>Raw ETag value including any weak prefix (<c>W/"..."</c>).</summary>
        public string ETag;

        /// <summary>Raw <c>Last-Modified</c> header value (RFC 1123 date string).</summary>
        public string LastModified;

        /// <summary>UTC timestamp the entry was written.</summary>
        public DateTimeOffset StoredAt;

        /// <summary>Original Content-Type media type (e.g. <c>application/json</c>).</summary>
        public string ContentType;

        /// <summary>Original response status code (typically 200).</summary>
        public int StatusCode;

        /// <summary>Body length in bytes (mirror; authoritative is the .body file).</summary>
        public long BodyLength;

        /// <summary>Headers preserved verbatim for rehydration / observability.</summary>
        public Dictionary<string, string> SelectedHeaders;
    }
}
