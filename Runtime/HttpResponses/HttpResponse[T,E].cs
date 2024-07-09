using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using ApiClient.Runtime.Cache;

namespace ApiClient.Runtime.HttpResponses
{
    /// <summary>
    /// A type of HttpResponse where either content <see cref="T"/> or error <see cref="E"/> was
    /// obtained from response's body.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="E"></typeparam>
    public class HttpResponse<T, E> : IHttpResponse, IHttpResponseStatusCode, IHttpResponseBody, ICachedHttpResponse
    {
        public HttpResponse(T content, E error, HttpResponseHeaders headers, HttpContentHeaders contentHeaders, string body, Uri uri, HttpStatusCode statusCode)
        {
            Content = content;
            Error = error;
            Headers = headers.ToHeadersDictionary();
            RequestUri = uri;
            StatusCode = statusCode;
            ContentHeaders = contentHeaders.ToHeadersDictionary();
            Body = body;
        }

        /// <summary>
        /// Content retrieved from response's body
        /// </summary>
        /// <value></value>
        public T Content { get; }

        /// <summary>
        /// Error retrieved from response's body
        /// </summary>
        /// <value></value>
        public E Error { get; }

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

        public long ContentSize()
        {
            // assume the content will have the same size as body.
            // this doesn't have to be the exact value
            return Body.Length * sizeof(char) * 2; 
        }
    }
}