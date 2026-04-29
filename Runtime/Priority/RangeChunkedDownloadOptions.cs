namespace ApiClient.Runtime.Priority
{
    /// <summary>
    /// Configuration for the chunked HTTP Range asset download path. Only takes effect
    /// when <see cref="ApiClientOptions.PriorityCoordinator"/> is non-null.
    /// </summary>
    public class RangeChunkedDownloadOptions
    {
        /// <summary>
        /// Size of each Range chunk in bytes. Sets preemption granularity (asset workers
        /// gate-check between chunks). Default 256 KiB — sub-second on 3G, large enough to
        /// keep TCP windows useful.
        /// </summary>
        public int ChunkSizeBytes { get; set; } = 256 * 1024;

        /// <summary>
        /// Master switch for chunked Range mode. When false the byte-array path keeps the
        /// legacy single-GET behaviour (still gated between buffer reads when
        /// <see cref="FallbackPreemptInBufferLoop"/> is true).
        /// </summary>
        public bool UseRangeRequests { get; set; } = true;

        /// <summary>
        /// Per-chunk retry budget for transient network errors. Distinct from the outer
        /// Polly retry pipeline so a mid-transfer hiccup does not throw away the bytes
        /// already received. Default 3.
        /// </summary>
        public int MaxChunkRetries { get; set; } = 3;

        /// <summary>
        /// When the server returns 200 to the Range probe (Range not honoured), still
        /// gate-check between buffer reads in the legacy loop. Less effective than
        /// chunk-level preemption (server keeps sending) but TCP back-pressure does
        /// reclaim some bandwidth. Default true.
        /// </summary>
        public bool FallbackPreemptInBufferLoop { get; set; } = true;
    }
}
