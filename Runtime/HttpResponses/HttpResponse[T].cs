using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;

namespace ApiClient.Runtime.HttpResponses
{
    /// <summary>
    /// A type of HttpResponse where content <see cref="T"/> was
    /// obtained from response's body.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class HttpResponse<T> : IHttpResponse, IHttpResponseStatusCode, IHttpResponseBody
    {
        public HttpResponse(T content, HttpResponseHeaders headers, HttpContentHeaders contentHeaders, string body, Uri uri, HttpStatusCode statusCode)
        {
            Content = content;
            Headers = headers.ToHeadersDictionary();
            RequestUri = uri;
            StatusCode = statusCode;
            ContentHeaders = contentHeaders.ToHeadersDictionary();
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
        public Uri RequestUri { get; private set; }
        public HttpStatusCode StatusCode { get; private set; }
        public Dictionary<string, string> Headers { get; private set; }
        public Dictionary<string, string> ContentHeaders { get; private set; }

        /// <summary>
        /// Unprocessed response's body string
        /// </summary>
        /// <value></value>
        public string Body { get; private set; }
    }
}