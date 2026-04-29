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
    /// <see cref="ApiClientConnection"/> is responsible for preparing Requests
    /// </summary>
    public class ApiClientConnection : IApiClientConnection
    {
        private readonly IApiClient _apiClient;
        private readonly IApiClient _assetApiClient;
        public IApiClient APIClient => _apiClient;

        /// <summary>
        /// Dedicated asset/stream <see cref="IApiClient"/> when the consumer is running the
        /// two-instance topology (gameplay client + asset client sharing one
        /// <see cref="ApiClient.Runtime.Priority.RequestPriorityCoordinator"/>). Falls back
        /// to <see cref="APIClient"/> when only one instance is configured.
        /// </summary>
        public IApiClient AssetAPIClient => _assetApiClient;

        private readonly Dictionary<string, string> _defaultHeaders = new();
        private readonly Version _httpVersion;
        private readonly UrlCache _urlCache = new();

        public ApiClientConnection(ApiClientOptions apiClientOptions, IApiClient apiClient = null)
        {
            _apiClient = apiClient ?? new ApiClient(apiClientOptions);
            _assetApiClient = _apiClient;
            _httpVersion = apiClientOptions.Version;
        }

        /// <summary>
        /// Two-instance constructor. Gameplay REST traffic routes through
        /// <paramref name="gameplayApiClient"/>; byte-array (asset) and stream traffic routes
        /// through <paramref name="assetApiClient"/>. Both should typically share one
        /// <see cref="ApiClient.Runtime.Priority.RequestPriorityCoordinator"/> via their
        /// respective <see cref="ApiClientOptions"/> so gameplay activity throttles asset
        /// transfers across the two instances.
        /// </summary>
        public ApiClientConnection(
            ApiClientOptions apiClientOptions,
            IApiClient gameplayApiClient,
            IApiClient assetApiClient)
        {
            _apiClient = gameplayApiClient ?? throw new ArgumentNullException(nameof(gameplayApiClient));
            _assetApiClient = assetApiClient ?? gameplayApiClient;
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

        public HttpClientRequest CreateGet(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            CachePolicy cachePolicy = null)
        {
            var request = new HttpClientRequest(
                new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                cachePolicy,
                () => this.CreateGet(
                    url,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            foreach (var header in headers)
            {
                request.RequestMessage.Headers.Add(header.Key, header.Value);
            }

            return request;
        }

        public HttpClientRequest<E> CreateGet<E>(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            CachePolicy cachePolicy = null)
        {
            var request = new HttpClientRequest<E>(
                new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                cachePolicy,
                () => this.CreateGet<E>(
                    url,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public HttpClientRequest<T, E> CreateGet<T, E>(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            CachePolicy cachePolicy = null)
        {
            var request = new HttpClientRequest<T, E>(
                new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                cachePolicy,
                () => this.CreateGet<T, E>(
                    url,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders
                ))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public HttpClientRequest CreatePost(
            string url,
            string jsonBody,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest(
                new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                null,
                () => this.CreatePost(
                    url,
                    jsonBody,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
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
            bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest<T>(
                new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                null,
                () => this.CreatePost<T>(
                    url,
                    jsonBody,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
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
            bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest<T, E>(
                new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                null,
                () => this.CreatePost<T, E>(
                    url,
                    jsonBody,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
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
            bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest(
                new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                null,
                () => this.CreatePut(
                    url,
                    jsonBody,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
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
            bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest<T>(
                new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                null,
                () => this.CreatePut<T>(
                    url,
                    jsonBody,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
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
            bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest<T, E>(
                new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                null,
                () => this.CreatePut<T, E>(
                    url,
                    jsonBody,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders
                ))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
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
            bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest(
                new HttpRequestMessage(HttpMethod.Delete, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                null,
                () => this.CreateDelete(
                    url,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders
                ))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public HttpClientRequest<T> CreateDelete<T>(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest<T>(
                new HttpRequestMessage(HttpMethod.Delete, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                null,
                () => this.CreateDelete<T>(
                    url,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders
                ))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public HttpClientRequest<T, E> CreateDelete<T, E>(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true)
        {
            var request = new HttpClientRequest<T, E>(
                new HttpRequestMessage(HttpMethod.Delete, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                null,
                () => this.CreateDelete<T, E>(
                    url,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders
                ))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public HttpClientStreamRequest<T> CreateGetStreamRequest<T>(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true)
        {
            var request = new HttpClientStreamRequest<T>(
                new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = _httpVersion
                },
                _assetApiClient,
                ct)
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public HttpClientByteArrayRequest CreateGetByteArrayRequest(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            CachePolicy cachePolicy = null)
        {
            var request = new HttpClientByteArrayRequest(
                new HttpRequestMessage(HttpMethod.Get, url)
                {
                    Version = _httpVersion
                },
                _assetApiClient,
                ct,
                _urlCache,
                cachePolicy,
                () => this.CreateGetByteArrayRequest(
                    url,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders,
                    cachePolicy))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }

        public HttpClientHeadersRequest CreateGetHeadersOnlyRequest(
            string url,
            CancellationToken ct,
            AuthenticationHeaderValue authentication = null,
            Dictionary<string, string> headers = null,
            bool useDefaultHeaders = true,
            CachePolicy cachePolicy = null)
        {
            var request = new HttpClientHeadersRequest(
                new HttpRequestMessage(HttpMethod.Head, url)
                {
                    Version = _httpVersion
                },
                _apiClient,
                ct,
                _urlCache,
                cachePolicy,
                () => this.CreateGetHeadersOnlyRequest(
                    url,
                    ct,
                    authentication,
                    headers,
                    useDefaultHeaders,
                    cachePolicy))
            {
                Authentication = authentication,
                Headers = headers,
                DefaultHeaders = useDefaultHeaders ? _defaultHeaders : null
            };

            return request;
        }
    }
}