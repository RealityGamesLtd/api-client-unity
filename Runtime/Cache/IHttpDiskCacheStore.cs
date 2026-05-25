using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ApiClient.Runtime.Cache
{
    /// <summary>
    /// Persistent on-disk store for HTTP responses keyed by an opaque string
    /// (typically <see cref="DiskCacheKey.Compute"/>). Implementations must be
    /// safe for concurrent use across thread-pool requests.
    /// </summary>
    public interface IHttpDiskCacheStore : IDisposable
    {
        /// <summary>
        /// Read the sidecar metadata for <paramref name="key"/>. Returns null on miss
        /// or any IO failure (the caller should fall back to a network fetch).
        /// </summary>
        Task<DiskCacheEntry> TryReadMetaAsync(string key, CancellationToken ct);

        /// <summary>
        /// Open the body file for <paramref name="key"/>. Caller disposes the stream.
        /// Returns null if no body is present (e.g. metadata vanished between calls).
        /// </summary>
        Task<Stream> OpenBodyAsync(string key, CancellationToken ct);

        /// <summary>
        /// Atomically write body + meta. Crash-safe (.tmp + rename). Eviction may
        /// run synchronously on overrun.
        /// </summary>
        Task WriteAsync(string key, DiskCacheEntry entry, byte[] body, CancellationToken ct);

        /// <summary>Remove a single entry (best-effort).</summary>
        Task InvalidateAsync(string key);

        /// <summary>Wipe the entire store (best-effort).</summary>
        Task ClearAsync();

        /// <summary>Approximate on-disk size in bytes (sum of tracked entry body sizes).</summary>
        long ApproxSizeBytes { get; }
    }
}
