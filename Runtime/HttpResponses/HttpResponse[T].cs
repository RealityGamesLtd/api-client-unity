using System;
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
            Headers = headers;
            RequestUri = uri;
            StatusCode = statusCode;
            ContentHeaders = contentHeaders;
            Body = body;
        }

        public T Content { get; }

        public bool IsClientError => (int)StatusCode >= 400 && (int)StatusCode < 500;
        public bool IsServerError => (int)StatusCode >= 500;
        public bool IsContentParsingError => false;
        public bool IsNetworkError => false;
        public bool IsAborted => false;
        public bool IsTimeout => false;
        public Uri RequestUri { get; private set; }
        public HttpStatusCode StatusCode { get; private set; }
        public HttpResponseHeaders Headers { get; private set; }
        public HttpContentHeaders ContentHeaders { get; private set; }
        public string Body { get; private set; }
    }
}