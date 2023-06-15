using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using ApiClient.Runtime.GraphQLBuilder;
using ApiClient.Runtime.Requests;

namespace ApiClient.Runtime
{
    public class ApiClientConnection : IApiClientConnection
    {
        private readonly ApiClient _apiClient;

        private readonly Dictionary<string, string> _defaultHeaders = new();


        public ApiClientConnection(ApiClientOptions apiClientOptions)
        {
            _apiClient = new ApiClient(apiClientOptions);
        }

        public void SetDefaultHeader(string key, string value)
        {
            if (_defaultHeaders.ContainsKey(key))
            {
                _defaultHeaders.Remove(key);
            }
            _defaultHeaders.Add(key, value);
        }

        public HttpClientRequest CreateGet(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest(new HttpRequestMessage(HttpMethod.Get, url), _apiClient, ct)
            {
                Authentication = authentication,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public HttpClientRequest<T> CreateGet<T>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest<T>(new HttpRequestMessage(HttpMethod.Get, url), _apiClient, ct)
            {
                Authentication = authentication,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public HttpClientRequest CreatePost(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest(new HttpRequestMessage(HttpMethod.Post, url), _apiClient, ct)
            {
                Authentication = authentication,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            if (jsonBody != null)
                request.RequestMessage.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            return request;
        }

        public HttpClientRequest<T> CreatePost<T>(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest<T>(new HttpRequestMessage(HttpMethod.Post, url), _apiClient, ct)
            {
                Authentication = authentication,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            if (jsonBody != null)
                request.RequestMessage.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            return request;
        }

        public HttpClientRequest CreatePut(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest(new HttpRequestMessage(HttpMethod.Put, url), _apiClient, ct)
            {
                Authentication = authentication,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            if (jsonBody != null) request.RequestMessage.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            return request;
        }

        public HttpClientRequest<T> CreatePut<T>(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest<T>(new HttpRequestMessage(HttpMethod.Put, url), _apiClient, ct)
            {
                Authentication = authentication,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            if (jsonBody != null)
                request.RequestMessage.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            return request;
        }

        public HttpClientRequest CreateDelete(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest(new HttpRequestMessage(HttpMethod.Delete, url), _apiClient, ct)
            {
                Authentication = authentication,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public HttpClientRequest<T> CreateDelete<T>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest<T>(new HttpRequestMessage(HttpMethod.Delete, url), _apiClient, ct)
            {
                Authentication = authentication,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public HttpClientStreamRequest<T> CreateGetStreamRequest<T>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new HttpClientStreamRequest<T>(new HttpRequestMessage(HttpMethod.Get, url), _apiClient, ct)
            {
                Authentication = authentication,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public GraphQLClientRequest<T> CreateGraphQLRequest<T>(IQuery query, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new GraphQLClientRequest<T>(_apiClient, ct)
            {
                Authentication = authentication,
                Query = query.ToString(),
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public GraphQLClientRequest<T> CreateGraphQLRequest<T>(string query, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new GraphQLClientRequest<T>(_apiClient, ct)
            {
                Authentication = authentication,
                Query = query,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public GraphQLClientRequest<T> CreateGraphQLRequest<T>(string query, object variables, CancellationToken ct, AuthenticationHeaderValue authentication = null, bool useDefaultHeaders = true)
        {
            var request = new GraphQLClientRequest<T>(_apiClient, ct)
            {
                Authentication = authentication,
                Query = query,
                Variables = variables,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }
    }
}