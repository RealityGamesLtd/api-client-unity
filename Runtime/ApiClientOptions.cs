using System;
using Polly.Retry;
using ApiClient.Runtime.HttpResponses;

namespace ApiClient.Runtime
{
    public class ApiClientOptions
    {
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
        public string GraphQLClientEndpoint { get; set; }
        public AsyncRetryPolicy<IHttpResponse> RetryPolicy { get; set; }
    }
}