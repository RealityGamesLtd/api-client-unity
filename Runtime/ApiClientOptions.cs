using System;
using Polly.Retry;
using ApiClient.Runtime.HttpResponses;
using System.Net;

namespace ApiClient.Runtime
{
    public class ApiClientOptions
    {
        /// <summary>
        /// Set how long to wait for the response until it's terminated.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Base GraphQl endpoint url.
        /// </summary>
        public string GraphQLClientEndpoint { get; set; }

        /// <summary>
        /// Custom retry policy to set the rules of what happens when requests fail.
        /// </summary>
        public AsyncRetryPolicy<IHttpResponse> RetryPolicy { get; set; }

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
    }
}