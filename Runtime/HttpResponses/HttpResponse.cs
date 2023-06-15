using System;
using System.Net;

namespace ApiClient.Runtime.HttpResponses
{
    public class HttpResponse : IHttpResponse, IHttpResponseStatusCode
    {
        public HttpResponse(Uri uri, HttpStatusCode statusCode)
        {
            RequestUri = uri;
            StatusCode = statusCode;
        }

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