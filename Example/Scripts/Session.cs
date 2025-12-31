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

        public static AuthenticationHeaderValue authenticationHeaderValue = new AuthenticationHeaderValue("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIwMTliMjczYi00YTRjLTFlOTEtNzJhZi0yYTBkZjc5NGU2YmEiLCJyb2xlIjoidGVzdGVyIiwidG9rZW5UeXBlIjoibXdvLWFjY2VzcyIsImV4cCI6MTc2NTg5MzM1NSwiaWF0IjoxNzY1ODg5NzU1fQ.gDfd4vvtlXw7iIzfRGLNmX5pTyKZTTgJ3G5drzbNmNCpEWqfJGG9nJf6nx-CX9I-g2cUsOlgZba8yd-LeESldb41WwRMm9onZHfmunp8UBVg1akJ4TdzD79ITdIFtoYypzuZIUY44XbblbiTEm1_PuLivJU9BM4QGkBcnvTyB8oa1cetBIsTyFwFgmwkWjRsX8r6l7KozaUUJIBgCFfB-0s-zV_cXI2dVzf09V-yiSWByNsJGAC5Ew3DwlSOjlQF1mkXkDS-dRIHgjYbN3ZaLXlAID9JBtCctKibLPwyYddTWjwAMLjcXKoNxUQIdmUNIY5i44sP3QHnwGANtd4hEyVO_z3m0NabS3ExQ7j06iW8NIs-uT1KO9EpQLLKuVirX_VH17N1F004mnRiyU5AY0R5XpohoPVOW3IlpwMeFA48zwTyVhF493MQ971v4kHYTyCXkLnRvcTpmFMm6pmhruHL_fOYsup0enxSg5KJid-I1mtqg65QJ8CT2zxrlhJH6_GFI9MpWu8k54PXH5cssxeFdMQ80_SRLs40yR5-x7OBHsqzJy_9N_MLD9OU74Fw_T-4a6TOXt7nqoa_LUDkuiksVbotPE8z2iCkrHLT1PQdfYjZOPx40QrbGvQOlnBALGiOynnISzZ0wZ3WxOoGLz7mT27GyXQ1mrIYVqGz2NQ");

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
                            context["RetryAttempt"] = retryAttempt;
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
                        context["newAuthenticationHeaderValue"] = authenticationHeaderValue;
                    })
                )
            });

        public readonly IApiClientConnection MockApiClientConnecton = new ApiClientConnection(
            new ApiClientOptions()
            {
                GraphQLClientEndpoint = "https://spacex-production.up.railway.app/",
                Timeout = TimeSpan.FromSeconds(10),
                Middleware = new Middleware(),
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
                        Backoff.DecorrelatedJitterBackoffV2(medianFirstRetryDelay: TimeSpan.FromSeconds(1), retryCount: 2),
                        (response, delay, retryAttempt, context) =>
                        {
                            // Logic to be executed before each retry
                            context["RetryAttempt"] = retryAttempt;
                        }),
            }, 
            new ApiClientMock());
    }
}