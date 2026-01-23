using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using ApiClient.Runtime;
using ApiClient.Runtime.HttpResponses;
using Polly;
using Polly.Contrib.WaitAndRetry;

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

        public static readonly AuthenticationHeaderValue authenticationHeaderValue = new("Bearer", "your_token_here");

        private const string RETRY_ATTEMPT_CONTEXT_KEY = "RetryAttempt";
        private const string NEW_AUTHENTICATION_HEADER_VALUE_CONTEXT_KEY = "newAuthenticationHeaderValue";

        public readonly IApiClientConnection ApiClientConnecton = new ApiClientConnection(
            new ApiClientOptions()
            {
                GraphQLClientEndpoint = "https://spacex-production.up.railway.app/",
                Timeout = TimeSpan.FromSeconds(10),
                Middleware = new Middleware(),
                RetryPolicies = Policy.WrapAsync(Policy
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
                        Backoff.DecorrelatedJitterBackoffV2(medianFirstRetryDelay: TimeSpan.FromSeconds(1), retryCount: 2),
                        (response, delay, retryAttempt, context) =>
                        {
                            // Logic to be executed before each retry
                            context[RETRY_ATTEMPT_CONTEXT_KEY] = retryAttempt;
                        }),
                        Policy
                    .HandleResult<IHttpResponse>(r =>
                    {
                        if (r is IHttpResponseStatusCode responseWithStatusCode)
                        {
                            return responseWithStatusCode.StatusCode == (HttpStatusCode)401;
                        }
                        return false;
                    })
                    .WaitAndRetryAsync(Backoff.DecorrelatedJitterBackoffV2(
                        medianFirstRetryDelay: TimeSpan.FromSeconds(1),
                        retryCount: 2),
                        (response, delay, retryAttempt, context) =>
                    {
                        context[NEW_AUTHENTICATION_HEADER_VALUE_CONTEXT_KEY] = authenticationHeaderValue;
                    })
                )
            });

        public readonly IApiClientConnection MockApiClientConnecton = new ApiClientConnection(
            new ApiClientOptions()
            {
                GraphQLClientEndpoint = "https://spacex-production.up.railway.app/",
                Timeout = TimeSpan.FromSeconds(10),
                Middleware = new Middleware(),
                RetryPolicies = Policy.WrapAsync(Policy
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
                        Backoff.DecorrelatedJitterBackoffV2(medianFirstRetryDelay: TimeSpan.FromSeconds(1), retryCount: 2),
                        (response, delay, retryAttempt, context) =>
                        {
                            // Logic to be executed before each retry
                            context[RETRY_ATTEMPT_CONTEXT_KEY] = retryAttempt;
                        }),
                        Policy
                    .HandleResult<IHttpResponse>(r =>
                    {
                        if (r is IHttpResponseStatusCode responseWithStatusCode)
                        {
                            return responseWithStatusCode.StatusCode == (HttpStatusCode)401;
                        }
                        return false;
                    })
                    .WaitAndRetryAsync(Backoff.DecorrelatedJitterBackoffV2(
                        medianFirstRetryDelay: TimeSpan.FromSeconds(1),
                        retryCount: 2),
                        (response, delay, retryAttempt, context) =>
                    {
                        context[NEW_AUTHENTICATION_HEADER_VALUE_CONTEXT_KEY] = authenticationHeaderValue;
                    })
                )
            }, 
            new ApiClientMock());
    }
}