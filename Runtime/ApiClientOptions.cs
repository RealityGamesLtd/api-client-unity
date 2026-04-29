using System;
using Polly.Retry;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Priority;
using System.Net;
using Polly.Wrap;

namespace ApiClient.Runtime
{
    public class ApiClientOptions
    {
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
        /// Buffer size for byte array requests in bytes. Default = 4096 bytes
        /// </summary>
        public int ByteArrayBufferSize { get; set; } = 4096;

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
        /// Optional shared coordinator that gives gameplay HTTP traffic priority over
        /// bulk asset downloads. When null (default) the legacy behaviour applies: a
        /// single shared connection pool for every request, no concurrency cap on
        /// asset downloads, no Range chunking. When non-null, an additional dedicated
        /// asset <see cref="System.Net.Http.HttpClient"/> is built inside the
        /// <see cref="ApiClient"/> and the byte-array path is dispatched through the
        /// coordinator.
        /// </summary>
        public RequestPriorityCoordinator PriorityCoordinator { get; set; } = null;

        /// <summary>
        /// Configuration for the chunked HTTP Range asset download path. Only takes
        /// effect when <see cref="PriorityCoordinator"/> is non-null.
        /// </summary>
        public RangeChunkedDownloadOptions RangeDownload { get; set; } = new RangeChunkedDownloadOptions();

        /// <summary>
        /// Selects which transport pools this <see cref="ApiClient"/> instance owns.
        /// Default <see cref="ApiClientLane.Mixed"/> keeps the historical behaviour of
        /// one client owning every kind of traffic. Use
        /// <see cref="ApiClientLane.Gameplay"/> / <see cref="ApiClientLane.Asset"/>
        /// only when running two <see cref="ApiClient"/> instances side-by-side and
        /// sharing one <see cref="PriorityCoordinator"/>.
        /// </summary>
        public ApiClientLane Lane { get; set; } = ApiClientLane.Mixed;
    }
}