using System;
using System.Net;
using System.Net.Http.Headers;

namespace ApiClient.Runtime.HttpResponses
{
    /// <summary>
    /// A type of HttpResponse where either content <see cref="T"/> or error <see cref="E"/> was
    /// obtained from response's body.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="E"></typeparam>
    public class HttpResponse<T, E> : IHttpResponse, IHttpResponseStatusCode, IHttpResponseBody
    {
        public HttpResponse(T content, E error, HttpResponseHeaders headers, HttpContentHeaders contentHeaders, string body, Uri uri, HttpStatusCode statusCode)
        {
            Content = content;
            Error = error;
            Headers = headers;
            RequestUri = uri;
            StatusCode = statusCode;
            ContentHeaders = contentHeaders;
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
        public HttpResponseHeaders Headers { get; private set; }
        public HttpContentHeaders ContentHeaders { get; private set; }

        /// <summary>
        /// Unprocessed response's body string
        /// </summary>
        /// <value></value>
        public string Body { get; private set; }
    }
}