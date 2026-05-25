using System;
using Polly.Retry;
using ApiClient.Runtime.Cache;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Priority;
using System.Net;
using Polly.Wrap;

namespace ApiClient.Runtime
{
    public class ApiClientOptions
    {
        /// <summary>
        /// Optional pre-built <see cref="UrlCache"/> to share across multiple
        /// <see cref="ApiClient"/> instances (e.g. gameplay + asset clients backed
        /// by a single disk cache). When null, the <see cref="ApiClient"/> creates
        /// its own. Either way, <see cref="DiskCacheStore"/> (if non-null) is
        /// attached to whatever <see cref="UrlCache"/> ends up on the client.
        /// </summary>
        public UrlCache UrlCache { get; set; }

        /// <summary>
        /// Optional disk-backed HTTP cache. When set, every request issued through
        /// this <see cref="ApiClient"/> whose <c>cachePolicy</c> is a
        /// <see cref="HttpCachePolicy"/> participates in ETag / Last-Modified
        /// conditional GETs and persistent storage. Wire one shared store across
        /// all client instances in the connection so they don't fight over files.
        /// </summary>
        public IHttpDiskCacheStore DiskCacheStore { get; set; }

        /// <summary>
        /// Set how long to wait for the response until it's terminated.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Custom retry policy to set the rules of what happens when requests fail.
        /// </summary>
        public AsyncPolicyWrap<IHttpResponse> RetryPolicies { get; set; }

        /// <summary>
        /// Middleware allows to inject some logic before the response is returned.
        /// Potential use cases: Logging, Error handling, Simulating of delayed responses.
        /// </summary>
        public IApiClientMiddleware Middleware { get; set; }

        /// <summary>
        /// Stream buffer size in bytes. Default = 4096 bytes
        /// </summary>
        public int StreamBufferSize { get; set; } = 4096;

        /// <summary>
        /// Buffer size for byte array requests in bytes. Default = 65536 bytes (64 KB).
        /// Larger buffers cut the number of <c>ReadAsync</c> iterations per response,
        /// which directly reduces progress-callback churn and allocation rate on mobile.
        /// </summary>
        public int ByteArrayBufferSize { get; set; } = 65536;

        /// <summary>
        /// Minimum bytes between byte-array progress callbacks. Default = 65536 bytes (64 KB).
        /// Callback fires when either this OR <see cref="ProgressReportThrottleMs"/>
        /// threshold is crossed. First and final progress callbacks always fire.
        /// </summary>
        public int ProgressReportThresholdBytes { get; set; } = 64 * 1024;

        /// <summary>
        /// Minimum milliseconds between byte-array progress callbacks. Default = 100ms.
        /// Callback fires when either this OR <see cref="ProgressReportThresholdBytes"/>
        /// threshold is crossed. First and final progress callbacks always fire.
        /// </summary>
        public int ProgressReportThrottleMs { get; set; } = 100;

        /// <summary>
        /// Specify version of <see cref="HttpVersion"/>
        /// </summary>
        /// <value></value>
        public Version Version { get; set; } = HttpVersion.Version20;

        /// <summary>
        /// How often update & notify about stream read delta
        /// In Miliseconds
        /// </summary>
        /// <value></value>
        public int StreamReadDeltaUpdateTime { get; set; } = 1000;

        public bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// Enable gathering body for logging purposes.
        /// </summary>
        /// <value></value>
        public bool BodyLogging { get; set; } = false;

        /// <summary>
        /// Optional shared priority coordinator. When null (default) the legacy
        /// behaviour applies: requests run as before with no per-lane bulkhead, yield
        /// gate, or chunked Range downloads. When non-null, requests carrying a
        /// non-null <see cref="ApiClient.Runtime.Requests.IHttpRequest.PriorityLane"/>
        /// are coordinated against the lanes registered on this coordinator.
        /// </summary>
        public RequestPriorityCoordinator PriorityCoordinator { get; set; } = null;

        /// <summary>
        /// Per-instance configuration of the chunked HTTP Range download path. Only
        /// takes effect for byte-array requests on a lane whose
        /// <see cref="LaneConfig.ChunkedRangeDownloads"/> is true.
        /// </summary>
        public RangeChunkedDownloadOptions RangeDownload { get; set; } = new RangeChunkedDownloadOptions();

        /// <summary>
        /// Automatic decompression policy applied to the underlying
        /// <see cref="System.Net.Http.HttpClientHandler"/>. Default
        /// <see cref="DecompressionMethods.GZip"/> + <see cref="DecompressionMethods.Deflate"/>.
        /// Set to <see cref="DecompressionMethods.None"/> when this <see cref="ApiClient"/>
        /// services byte-array requests on a lane with
        /// <see cref="LaneConfig.ChunkedRangeDownloads"/> enabled — Range over a gzipped
        /// entity makes byte offsets undefined.
        /// </summary>
        public DecompressionMethods AutomaticDecompression { get; set; } =
            DecompressionMethods.GZip | DecompressionMethods.Deflate;
    }
}