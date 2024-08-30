using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime.HttpResponses;

namespace ApiClient.Runtime.Requests
{
    public class HttpClientRequest<T, E> : IHttpRequest
    {
        public bool IsSent { get; private set; }
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
                if (_authentication != null)
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
                    RequestMessage.Headers.Add(kv.Key, kv.Value);
                }
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
                    RequestMessage.Headers.Add(kv.Key, kv.Value);
                }
            }
            get
            {
                return RequestMessage.Headers.ToDictionary(
                    x => x.Key,
                    x => string.Join(";", x.Value));
            }
        }

        private readonly ApiClient _apiClient;
        private readonly Func<HttpClientRequest<T, E>> _recreateFunc;

        private AuthenticationHeaderValue _authentication;


        public HttpClientRequest(
            HttpRequestMessage httpRequestMessage,
            ApiClient apiClient,
            CancellationToken ct,
            Func<HttpClientRequest<T, E>> recreateFunc)
        {
            RequestMessage = httpRequestMessage;
            CancellationToken = ct;
            Uri = httpRequestMessage.RequestUri;
            _apiClient = apiClient;
            _recreateFunc = recreateFunc;
        }

        public async Task<IHttpResponse> Send()
        {
            if (IsSent)
            {
                throw new Exception("This request has been already sent! Resending is not allowed.");
            }

            IsSent = true;
            return await _apiClient.SendHttpRequest(this);
        }

        public HttpClientRequest<T, E> RecreateWithHttpRequestMessage()
        {
            RequestMessage.Dispose();
            return _recreateFunc?.Invoke();
        }

        // public HttpClientRequest<T, E> RecreateWithHttpRequestMessage()
        // {
        //     var recreatedHttpRequestMessage = new HttpClientRequest<T, E>(RecreateRequestMessage(this.RequestMessage), _apiClient, CancellationToken)
        //     {
        //         Authentication = this.Authentication,
        //         RequestId = Guid.NewGuid().ToString()
        //     };

        //     return recreatedHttpRequestMessage;
        // }

        private HttpRequestMessage RecreateRequestMessage(HttpRequestMessage req)
        {
            HttpRequestMessage httpRequestMessage = new(
                req.Method,
                req.RequestUri)
            {
                // Content = req.Content,
                Content = new StringContent("", System.Text.Encoding.UTF8, "application/json"),
                Version = req.Version
            };

            // request.RequestMessage.Content = new StringContent(req.Content, System.Text.Encoding.UTF8, "application/json");


            var headers = req.Headers;

            foreach (KeyValuePair<string, IEnumerable<string>> kv in headers)
            {
                httpRequestMessage.Headers.Add(kv.Key, kv.Value);
            }

            var properties = req.Properties;
            foreach (KeyValuePair<string, object> kv in properties)
            {
                httpRequestMessage.Properties.Add(kv.Key, kv.Value);
            }

            req.Dispose();

            return httpRequestMessage;
        }
    }
}