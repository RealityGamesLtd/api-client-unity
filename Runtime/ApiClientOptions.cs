using System;
using Polly.Retry;
using ApiClient.Runtime.HttpResponses;
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
        /// Enable time measurement for request and deserialization.
        /// When enabled, response objects will include timing information.
        /// </summary>
        /// <value></value>
        public bool EnableTimeMeasurements { get; set; } = false;
    }
}