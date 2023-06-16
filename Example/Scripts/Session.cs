using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using ApiClient.Runtime;
using ApiClient.Runtime.HttpResponses;
using Polly;

namespace ApiClientExample
{
    public class Session
    {
        public static Session Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new Session();
                }

                return _instance;
            }
        }

        private static Session _instance;

        private static HttpStatusCode[] _httpStatusCodesWorthRetrying = {
                HttpStatusCode.RequestTimeout, // 408
                HttpStatusCode.InternalServerError, // 500
                HttpStatusCode.BadGateway, // 502
                HttpStatusCode.GatewayTimeout // 504
            };

        public readonly IApiClientConnection ApiClientConnecton = new ApiClientConnection(
            new ApiClientOptions()
            {
                GraphQLClientEndpoint = "https://spacex-production.up.railway.app/",
                Timeout = TimeSpan.FromSeconds(10),
                RetryPolicy = Policy
                    .Handle<HttpRequestException>()
                    .OrResult<IHttpResponse>(r =>
                    {
                        var validStatusCode = false;
                        if (r is IHttpResponseStatusCode responseWithStatusCode)
                        {
                            validStatusCode = _httpStatusCodesWorthRetrying.Contains(responseWithStatusCode.StatusCode);
                        }
                        return r.IsTimeout ||
                            r.IsNetworkError ||
                            validStatusCode;
                    })
                    // Exponential Backoff
                    .WaitAndRetryAsync(
                        3,
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        (response, timeSpan) =>
                        {
                            // Logic to be executed before each retry
                        }),
            });
    }
}