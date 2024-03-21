using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using GraphQL.Client.Abstractions;
using GraphQL.Client.Http;
using ApiClient.Runtime.HttpResponses;

namespace ApiClient.Runtime.Requests
{
    public class GraphQLClientRequest<T> : GraphQLHttpRequest, IHttpRequest
    {
        public GraphQLClientRequest(ApiClient apiClient, CancellationToken cancellationToken) : base()
        {
            CancellationToken = cancellationToken;
            _apiClient = apiClient;
        }

        public CancellationToken CancellationToken { get; }
        public AuthenticationHeaderValue Authentication { get; set; }
        public Uri Uri { get; private set; }
        public bool IsSent { get; private set; }
        public string RequestId { get; } = Guid.NewGuid().ToString();
        public HttpRequestMessage RequestMessage { get; private set; }

        public Dictionary<string, string> DefaultHeaders { private get; set; }
        public Dictionary<string, string> Headers { private get; set; }

        private readonly ApiClient _apiClient;

        public override HttpRequestMessage ToHttpRequestMessage(GraphQLHttpClientOptions options, IGraphQLJsonSerializer serializer)
        {
            var r = base.ToHttpRequestMessage(options, serializer);
            r.Headers.Authorization = Authentication;

            if (DefaultHeaders != null)
            {
                foreach (var kv in DefaultHeaders)
                {
                    r.Headers.Add(kv.Key, kv.Value);
                }
            }

            if (Headers != null)
            {
                foreach (var kv in Headers)
                {
                    r.Headers.Add(kv.Key, kv.Value);
                }
            }

            Uri = r.RequestUri;
            RequestMessage = r;

            return r;
        }

        public async Task<IHttpResponse> Send()
        {
            if (IsSent)
            {
                throw new Exception("This request has been already sent! Resending is not allowed.");
            }

            IsSent = true;

            return await _apiClient.SendGraphQLRequest<T>(this);
        }
    }
}