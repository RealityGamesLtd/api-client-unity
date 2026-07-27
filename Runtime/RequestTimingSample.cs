using System;
using System.Net;
using System.Net.Http;

namespace ApiClient.Runtime
{
    /// <summary>
    /// One observation of a completed REST call. Emitted via
    /// <see cref="IApiClient.OnRequestCompleted"/> for every <c>SendHttpRequest*</c>
    /// and <c>SendHttpHeadersRequest</c> invocation. Byte-array and stream sends do
    /// NOT emit — byte-array duration is bandwidth-bound (would skew RTT estimates),
    /// and stream lifetime is not an RTT signal.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Duration"/> measures user-perceived call time and includes
    /// <see cref="System.Threading.Tasks.Task"/> scheduling, middleware
    /// <c>ProcessRequest</c>/<c>ProcessResponse</c>, the entire Polly retry budget
    /// (with backoff), and the <see cref="System.Threading.SynchronizationContext"/>
    /// post back to the caller. It is not pure network RTT — but it IS what an end
    /// user feels.</para>
    ///
    /// <para><see cref="NetworkDuration"/>, by contrast, brackets ONLY the
    /// <c>HttpClient.SendAsync</c> call of the final attempt — measured on the ThreadPool
    /// inside the send's <see cref="System.Threading.Tasks.Task"/>. It excludes Task
    /// scheduling, middleware, Polly backoff between retries, and the
    /// <see cref="System.Threading.SynchronizationContext"/> post back to the caller. It is
    /// the closest thing to pure server round-trip time and is NOT inflated by a stalled
    /// caller thread — prefer it for connection-quality / latency EWMAs.</para>
    ///
    /// <para>For a connection-quality EWMA, consumers should:</para>
    /// <list type="bullet">
    /// <item>Prefer <see cref="NetworkDuration"/> over <see cref="Duration"/> — the latter
    /// includes caller-thread scheduling latency and is not a network measurement.</item>
    /// <item>Skip when <see cref="IsSuccess"/> is false (aborts/timeouts/network errors
    /// have meaningless durations).</item>
    /// <item>Skip when <see cref="IsFromCache"/> is true (cached responses return
    /// near-instantly and would drag the average low).</item>
    /// </list>
    /// </remarks>
    public readonly struct RequestTimingSample
    {
        /// <summary>
        /// Back-compat overload matching the pre-2.1.0 signature (no wire time). Forwards with
        /// <see cref="NetworkDuration"/> = <see cref="TimeSpan.Zero"/> so existing callers keep
        /// compiling; new code should pass a measured wire duration to the primary constructor.
        /// </summary>
        public RequestTimingSample(
            TimeSpan duration,
            bool isSuccess,
            bool isFromCache,
            HttpMethod method,
            string requestUri,
            HttpStatusCode? statusCode,
            string priorityLane)
            : this(duration, TimeSpan.Zero, isSuccess, isFromCache, method, requestUri, statusCode, priorityLane)
        {
        }

        public RequestTimingSample(
            TimeSpan duration,
            TimeSpan networkDuration,
            bool isSuccess,
            bool isFromCache,
            HttpMethod method,
            string requestUri,
            HttpStatusCode? statusCode,
            string priorityLane)
        {
            Duration = duration;
            NetworkDuration = networkDuration;
            IsSuccess = isSuccess;
            IsFromCache = isFromCache;
            Method = method;
            RequestUri = requestUri;
            StatusCode = statusCode;
            PriorityLane = priorityLane;
        }

        /// <summary>Wall-clock duration from the public <c>SendHttp*</c> entry point
        /// to the moment the response is handed back to the caller.</summary>
        public TimeSpan Duration { get; }

        /// <summary>
        /// Round-trip time of the final <c>HttpClient.SendAsync</c> attempt only, measured on
        /// the ThreadPool. Excludes Task scheduling, middleware, inter-retry backoff and the
        /// SynchronizationContext post-back, so a stalled caller thread does NOT inflate it.
        /// This is the value to feed a network-latency EWMA. <see cref="TimeSpan.Zero"/> when
        /// no send attempt completed (e.g. aborted before the wire call).
        /// </summary>
        public TimeSpan NetworkDuration { get; }

        /// <summary>
        /// True when bytes flowed end-to-end and the response is not an abort, timeout,
        /// or network error. <see cref="HttpResponses.ParsingErrorHttpResponse"/>
        /// counts as success — the network reached the server, only deserialisation
        /// failed.
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>True when <see cref="Cache.UrlCache"/> served the response without
        /// touching the network. Consumers driving an EWMA should drop these.</summary>
        public bool IsFromCache { get; }

        /// <summary>HTTP verb of the originating request, when available.</summary>
        public HttpMethod Method { get; }

        /// <summary>Absolute URI of the request, when available.</summary>
        public string RequestUri { get; }

        /// <summary>HTTP status code when an HTTP response was received,
        /// <c>null</c> for aborts / timeouts / network errors.</summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>Priority lane id the request was tagged with, or <c>null</c>.</summary>
        public string PriorityLane { get; }
    }
}
