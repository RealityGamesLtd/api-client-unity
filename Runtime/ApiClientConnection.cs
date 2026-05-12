using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using ApiClient.Runtime.Cache;
using ApiClient.Runtime.Requests;

namespace ApiClient.Runtime
{
    /// <summary>
    /// Builds <see cref="IHttpRequest"/> instances and routes them to the appropriate
    /// <see cref="IApiClient"/>.
    /// </summary>
    /// <remarks>
    /// Single-client topology: pass one <see cref="IApiClient"/>; every request runs
    /// through it.
    ///
    /// Multi-client topology: pass a default client plus a <c>laneRouting</c> map. Each
    /// <c>Create*</c> call accepts an optional <c>priorityLane</c> id; when present and
    /// the lane is keyed in <c>laneRouting</c>, the request is dispatched through the
    /// mapped <see cref="IApiClient"/>. Otherwise it falls back to the default. The
    /// chosen lane id is also stamped onto the request as
    /// <see cref="IHttpRequest.PriorityLane"/> so the executor can coordinate with a
    /// shared <see cref="ApiClient.Runtime.Priority.RequestPriorityCoordinator"/>.
    /// </remarks>
    public class ApiClientConnection : IApiClientConnection
    {
        private readonly IApiClient _apiClient;
        private readonly IReadOnlyDictionary<string, IApiClient> _laneRouting;

        public IApiClient APIClient => _apiClient;

        private readonly Dictionary<string, string> _defaultHeaders = new();
        private readonly Version _httpVersion;
        private readonly UrlCache _urlCache = new();

        public ApiClientConnection(ApiClientOptions apiClientOptions, IApiClient apiClient = null)
            : this(apiClientOptions, apiClient ?? new ApiClient(apiClientOptions), laneRouting: null)
        {
        }

        /// <summary>
        /// Build a connection that dispatches per-request based on
        /// <paramref name="laneRouting"/>. When a request supplies a <c>priorityLane</c>
        /// keyed in the map, that lane's <see cref="IApiClient"/> services the request;
        /// otherwise <paramref name="defaultApiClient"/> handles it. The same coordinator
        /// reference is typically shared by every <see cref="IApiClient"/> via
        /// <see cref="ApiClientOptions.PriorityCoordinator"/>.
        /// </summary>
        public ApiClientConnection(
            ApiClientOptions apiClientOptions,
            IApiClient defaultApiClient,
            IReadOnlyDictionary<string, IApiClient> laneRouting)
        {
            if (apiClientOptions == null) throw new ArgumentNullException(nameof(apiClientOptions));
            _apiClient = defaultApiClient ?? throw new ArgumentNullException(nameof(defaultApiClient));
            _laneRouting = laneRouting;
            _httpVersion = apiClientOptions.Version;
        }

        public void SetDefaultHeader(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || value == null)
                return;

            _defaultHeaders[key.Trim()] = value;
        }

        public void RemoveDefaultHeader(string key)
        {
            if (string.IsNullOrEmpty(key))
                return;

            _defaultHeaders.Remove(key.Trim());
        }

        // Pick the IApiClient for a request based on its priority lane. Lanes not in the
        // routing map (or null lane) fall back to the default client.
        private IApiClient Route(string priorityLane)
        {
            if (priorityLane != null && _laneRouting != null && _laneRouting.TryGetValue(priorityLane, out var routed))
                return routed;
            return _apiClient;
        }

        public HttpClientRequest CreateGet(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            CachePolicy cachePolicy = null,
            string priorityLane = null)
        {
            // Headers are applied once via the request's Headers setter; the duplicate
            // foreach that lived here previously caused header values to be added twice.
            var request = new HttpClientRequest(
                new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                cachePolicy,
                () => this.CreateGet(url, ct, authentication, headers, useDefaultHeaders, cachePolicy, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            return request;
        }

        public HttpClientRequest<T> CreateGet<T>(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            CachePolicy cachePolicy = null,
            string priorityLane = null)
        {
            var request = new HttpClientRequest<T>(
                new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                cachePolicy,
                () => this.CreateGet<T>(url, ct, authentication, headers, useDefaultHeaders, cachePolicy, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            return request;
        }

        public HttpClientRequest<T, E> CreateGet<T, E>(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            CachePolicy cachePolicy = null,
            string priorityLane = null)
        {
            var request = new HttpClientRequest<T, E>(
                new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                cachePolicy,
                () => this.CreateGet<T, E>(url, ct, authentication, headers, useDefaultHeaders, cachePolicy, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            return request;
        }

        public HttpClientRequest CreatePost(
            string url,
            string jsonBody,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            string priorityLane = null)
        {
            var request = new HttpClientRequest(
                new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                null,
                () => this.CreatePost(url, jsonBody, ct, authentication, headers, useDefaultHeaders, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            if (jsonBody != null)
            {
                request.RequestMessage.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            }
            return request;
        }

        public HttpClientRequest<T> CreatePost<T>(
            string url,
            string jsonBody,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            string priorityLane = null)
        {
            var request = new HttpClientRequest<T>(
                new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                null,
                () => this.CreatePost<T>(url, jsonBody, ct, authentication, headers, useDefaultHeaders, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            if (jsonBody != null)
            {
                request.RequestMessage.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            }
            return request;
        }

        public HttpClientRequest<T, E> CreatePost<T, E>(
            string url,
            string jsonBody,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            string priorityLane = null)
        {
            var request = new HttpClientRequest<T, E>(
                new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                null,
                () => this.CreatePost<T, E>(url, jsonBody, ct, authentication, headers, useDefaultHeaders, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            if (jsonBody != null)
            {
                request.RequestMessage.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            }
            return request;
        }

        public HttpClientRequest CreatePut(
            string url,
            string jsonBody,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            string priorityLane = null)
        {
            var request = new HttpClientRequest(
                new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                null,
                () => this.CreatePut(url, jsonBody, ct, authentication, headers, useDefaultHeaders, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            if (jsonBody != null)
            {
                request.RequestMessage.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            }
            return request;
        }

        public HttpClientRequest<T> CreatePut<T>(
            string url,
            string jsonBody,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            string priorityLane = null)
        {
            var request = new HttpClientRequest<T>(
                new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                null,
                () => this.CreatePut<T>(url, jsonBody, ct, authentication, headers, useDefaultHeaders, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            if (jsonBody != null)
            {
                request.RequestMessage.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            }
            return request;
        }

        public HttpClientRequest<T, E> CreatePut<T, E>(
            string url,
            string jsonBody,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            string priorityLane = null)
        {
            var request = new HttpClientRequest<T, E>(
                new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                null,
                () => this.CreatePut<T, E>(url, jsonBody, ct, authentication, headers, useDefaultHeaders, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            if (jsonBody != null)
            {
                request.RequestMessage.Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            }
            return request;
        }

        public HttpClientRequest CreateDelete(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            string priorityLane = null)
        {
            var request = new HttpClientRequest(
                new HttpRequestMessage(HttpMethod.Delete, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                null,
                () => this.CreateDelete(url, ct, authentication, headers, useDefaultHeaders, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            return request;
        }

        public HttpClientRequest<T> CreateDelete<T>(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            string priorityLane = null)
        {
            var request = new HttpClientRequest<T>(
                new HttpRequestMessage(HttpMethod.Delete, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                null,
                () => this.CreateDelete<T>(url, ct, authentication, headers, useDefaultHeaders, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            return request;
        }

        public HttpClientRequest<T, E> CreateDelete<T, E>(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            string priorityLane = null)
        {
            var request = new HttpClientRequest<T, E>(
                new HttpRequestMessage(HttpMethod.Delete, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                null,
                () => this.CreateDelete<T, E>(url, ct, authentication, headers, useDefaultHeaders, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            return request;
        }

        /// <summary>
        /// Creates a long-lived Server-Sent Events stream request. <paramref name="priorityLane"/>
        /// is used for <see cref="IApiClient"/> routing (via the connection's lane map) and
        /// stamped on the request for observability — it does NOT participate in
        /// <see cref="ApiClient.Runtime.Priority.RequestPriorityCoordinator"/> bulkhead /
        /// yield / in-flight handshakes. Stream lifetimes are open-ended; holding a slot
        /// or in-flight count for the stream's duration would deadlock other lanes.
        /// </summary>
        public HttpClientStreamRequest<T> CreateGetStreamRequest<T>(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            string priorityLane = null)
        {
            var request = new HttpClientStreamRequest<T>(
                new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct)
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            return request;
        }

        public HttpClientByteArrayRequest CreateGetByteArrayRequest(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            CachePolicy cachePolicy = null,
            string priorityLane = null)
        {
            var request = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                cachePolicy,
                () => this.CreateGetByteArrayRequest(url, ct, authentication, headers, useDefaultHeaders, cachePolicy, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            return request;
        }

        public HttpClientHeadersRequest CreateGetHeadersOnlyRequest(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            CachePolicy cachePolicy = null,
            string priorityLane = null)
        {
            var request = new HttpClientHeadersRequest(
                new HttpRequestMessage(HttpMethod.Head, url)
                {
                    Version = _httpVersion
                },
                Route(priorityLane),
                ct,
                _urlCache,
                cachePolicy,
                () => this.CreateGetHeadersOnlyRequest(url, ct, authentication, headers, useDefaultHeaders, cachePolicy, priorityLane))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null,
                PriorityLane = priorityLane,
            };

            return request;
        }
    }
}
