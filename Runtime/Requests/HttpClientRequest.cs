using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime.Cache;
using ApiClient.Runtime.HttpResponses;

namespace ApiClient.Runtime.Requests
{
    public class HttpClientRequest : IHttpRequest
    {
        public bool IsSent { get; internal set; }
        public CancellationToken CancellationToken { get; }
        public HttpRequestMessage RequestMessage { get; private set; }
        public string RequestId { get; private set; } = Guid.NewGuid().ToString();
        public Uri Uri { get; private set; }

        public AuthenticationHeaderValue Authentication
        {
            get => _authentication;
            set
            {
                _authentication = value;

                // apply authentication header
                if (_authentication != null && RequestMessage?.Headers != null)
                {
                    RequestMessage.Headers.Authorization = _authentication;
                }
            }
        }

        public Dictionary<string, string> DefaultHeaders
        {
            set
            {
                if (value == null)
                {
                    return;
                }

                foreach (var kv in value)
                {
                    RequestMessage?.Headers?.Add(kv.Key, kv.Value);
                }
            }
            get
            {
                return RequestMessage?.Headers?.ToDictionary(
                    x => x.Key,
                    x => string.Join(";", x.Value));
            }
        }

        public Dictionary<string, string> Headers
        {
            set
            {
                if (value == null)
                {
                    return;
                }

                foreach (var kv in value)
                {
                    RequestMessage?.Headers?.Add(kv.Key, kv.Value);
                }
            }
            get
            {
                return RequestMessage?.Headers?.ToHeadersDictionary();
            }
        }

        private readonly ApiClient _apiClient;
        private readonly Func<HttpClientRequest> _recreateFunc;
        private readonly CachePolicy _cachePolicy;
        private readonly UrlCache _urlCache;

        private AuthenticationHeaderValue _authentication;

        public HttpClientRequest(
            HttpRequestMessage httpRequestMessage,
            ApiClient apiClient,
            CancellationToken ct,
            UrlCache urlCache,
            CachePolicy cachePolicy,
            Func<HttpClientRequest> recreateFunc)
        {
            RequestMessage = httpRequestMessage;
            CancellationToken = ct;
            Uri = httpRequestMessage?.RequestUri;
            _apiClient = apiClient;
            _recreateFunc = recreateFunc;
            _urlCache = urlCache;
            _cachePolicy = cachePolicy;
        }

        public async Task<IHttpResponse> Send()
        {
            if (IsSent)
            {
                throw new Exception("This request has been already sent! Resending is not allowed.");
            }

            if (RequestMessage == null)
            {
                throw new Exception($"Trying to send request without {nameof(RequestMessage)}. This is not allowed");
            }

            IsSent = true;

            var response = await _urlCache.Process(
                this,
                _cachePolicy,
                () => _apiClient.SendHttpRequest(this));

            return response;
        }

        public HttpClientRequest RecreateWithHttpRequestMessage()
        {
            var recreateFuncResult = _recreateFunc?.Invoke();
            RequestMessage.Dispose();
            return recreateFuncResult;
        }
    }
}