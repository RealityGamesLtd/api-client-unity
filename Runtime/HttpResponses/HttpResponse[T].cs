using System;
using System.Net;

namespace ApiClient.Runtime.HttpResponses
{
    public class HttpResponse<T> : IHttpResponse, IHttpResponseStatusCode
    {
        public HttpResponse(T content, Uri uri, HttpStatusCode statusCode)
        {
            Content = content;
            RequestUri = uri;
            StatusCode = statusCode;
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
    }
}