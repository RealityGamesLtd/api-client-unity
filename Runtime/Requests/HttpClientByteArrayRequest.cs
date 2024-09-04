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
    public class HttpClientByteArrayRequest : IHttpRequest
    {
        public bool IsSent { get; private set; }
        public CancellationToken CancellationToken { get; }
        public HttpRequestMessage RequestMessage { get; private set; }
        public string RequestId { get; private set;} = Guid.NewGuid().ToString();
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

        private AuthenticationHeaderValue _authentication;


        public HttpClientByteArrayRequest(HttpRequestMessage requestMessage, ApiClient apiClient, CancellationToken ct)
        {
            RequestMessage = requestMessage;
            CancellationToken = ct;
            Uri = requestMessage.RequestUri;
            _apiClient = apiClient;
        }

        public async Task<IHttpResponse> Send(Action<ByteArrayRequestProgress> OnProgressChanged)
        {
            if (IsSent)
            {
                throw new Exception("This request has been already sent! Resending is not allowed.");
            }

            IsSent = true;

            return await _apiClient.SendByteArrayRequest(this, OnProgressChanged);
        }

        public HttpClientByteArrayRequest RecreateWithHttpRequestMessage()
        {
            var recreatedHttpRequestMessage = new HttpClientByteArrayRequest(
                RecreateRequestMessage(this.RequestMessage),
                _apiClient,
                CancellationToken)
            {
                Authentication = this.Authentication,
                RequestId = Guid.NewGuid().ToString()
            };

            return recreatedHttpRequestMessage;
        }

        private HttpRequestMessage RecreateRequestMessage(HttpRequestMessage req)
        {
            HttpRequestMessage httpRequestMessage = new HttpRequestMessage(
                req.Method,
                req.RequestUri)
            {
                Content = req.Content,
            };

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
            return httpRequestMessage;
        }
    }
}