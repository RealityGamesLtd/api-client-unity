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
    public class HttpClientStreamRequest<T> : IHttpRequest
    {
        public bool IsSent { get; private set; }
        public CancellationToken CancellationToken { get; }
        public HttpRequestMessage RequestMessage { get; private set; }
        public string RequestId { get; } = Guid.NewGuid().ToString();
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


        public HttpClientStreamRequest(HttpRequestMessage requestMessage, ApiClient apiClient, CancellationToken ct)
        {
            RequestMessage = requestMessage;
            CancellationToken = ct;
            Uri = requestMessage.RequestUri;
            _apiClient = apiClient;
        }

        public async Task Send(Action<IHttpResponse> OnStreamResponse)
        {
            if (IsSent)
            {
                throw new Exception("This request has been already sent! Resending is not allowed.");
            }

            IsSent = true;

            await _apiClient.SendStreamRequest<T>(this, OnStreamResponse);
        }
    }
}