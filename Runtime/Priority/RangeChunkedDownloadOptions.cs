namespace ApiClient.Runtime.Priority
{
    /// <summary>
    /// Per-instance configuration of the chunked HTTP Range path used by
    /// <see cref="ApiClient.SendByteArrayRequest"/>. Only takes effect for byte-array
    /// requests on a lane whose <see cref="LaneConfig.ChunkedRangeDownloads"/> is true.
    /// </summary>
    public class RangeChunkedDownloadOptions
    {
        /// <summary>
        /// Size of each Range chunk in bytes. Sets preemption granularity (asset workers
        /// gate-check between chunks). Default 256 KiB — sub-second on 3G, large enough
        /// to keep TCP windows useful.
        /// </summary>
        public int ChunkSizeBytes { get; set; } = 256 * 1024;

        /// <summary>
        /// Per-chunk retry budget for transient network errors. Distinct from the outer
        /// Polly retry pipeline so a mid-transfer hiccup does not throw away the bytes
        /// already received. Default 3.
        /// </summary>
        public int MaxChunkRetries { get; set; } = 3;

        /// <summary>
        /// When the server returns 200 to the Range probe (Range not honoured), still
        /// gate-check between buffer reads in the legacy loop. Less effective than
        /// chunk-level preemption but TCP back-pressure does reclaim some bandwidth.
        /// Default true.
        /// </summary>
        public bool FallbackPreemptInBufferLoop { get; set; } = true;
    }
}
