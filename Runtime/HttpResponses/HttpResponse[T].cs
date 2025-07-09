using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using ApiClient.Runtime.Cache;

namespace ApiClient.Runtime.HttpResponses
{
    /// <summary>
    /// A type of HttpResponse where content <see cref="T"/> was
    /// obtained from response's body.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class HttpResponse<T> : IHttpResponse, IHttpResponseStatusCode, IHttpResponseBody, ICachedHttpResponse
    {
        public HttpResponse(T content, HttpResponseHeaders headers, HttpContentHeaders contentHeaders, string body, HttpRequestMessage request, HttpStatusCode statusCode)
        {
            Content = content;
            Headers = headers.ToHeadersDictionary();
            RequestMessage = request;
            RequestUri = request.RequestUri;
            StatusCode = statusCode;
            ContentHeaders = contentHeaders != null ? contentHeaders.ToHeadersDictionary() : new Dictionary<string, string>();
            Body = body;
        }
        
        public HttpResponse(T content, HttpResponseHeaders headers, HttpContentHeaders contentHeaders, string body, Uri requestUri, HttpStatusCode statusCode)
        {
            Content = content;
            Headers = headers.ToHeadersDictionary();
            RequestUri = requestUri;
            StatusCode = statusCode;
            ContentHeaders = contentHeaders != null ? contentHeaders.ToHeadersDictionary() : new Dictionary<string, string>();
            Body = body;
        }

        /// <summary>
        /// Content retrieved from response body
        /// </summary>
        /// <value></value>
        public T Content { get; }

        /// <summary>
        /// Error is categorised as client error when <see cref="StatusCode"/> is between 400 & 500
        /// </summary>
        /// <returns></returns>
        public bool IsClientError => (int)StatusCode >= 400 && (int)StatusCode < 500;

        /// <summary>
        /// Error is categorised as client error when <see cref="StatusCode"/> is over 500
        /// </summary>
        /// <returns></returns>
        public bool IsServerError => (int)StatusCode >= 500;
        public bool IsContentParsingError => false;
        public bool IsNetworkError => false;
        public bool IsAborted => false;
        public bool IsTimeout => false;
        public HttpRequestMessage RequestMessage { get; private set; }
        public Uri RequestUri { get; private set; }
        public HttpStatusCode StatusCode { get; private set; }
        public Dictionary<string, string> Headers { get; private set; }
        public Dictionary<string, string> ContentHeaders { get; private set; }

        /// <summary>
        /// Unprocessed response's body string
        /// </summary>
        /// <value></value>
        public string Body { get; private set; }

        bool ICachedHttpResponse.IsFromCache { get; set; }

        public long CacheContentSize()
        {
            // assume the content will have the same size as body.
            // this doesn't have to be the exact value
            return Body != null ? Body.Length * sizeof(char) * 2 : 1;
        }
    }
}