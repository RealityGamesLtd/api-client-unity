using System;
using System.Net;

namespace ApiClient.Runtime.HttpResponses
{
    public class ParsingErrorHttpResponse : IHttpResponse, IHttpResponseStatusCode
    {
        public bool IsClientError => false;
        public bool IsServerError => false;
        public bool IsContentParsingError => true;
        public bool IsNetworkError => false;
        public bool IsAborted => false;
        public bool IsTimeout => false;
        public Uri RequestUri { get; private set; }
        public string Message { get; }

        public HttpStatusCode StatusCode { get; private set; }

        public ParsingErrorHttpResponse(string errorMessage, Uri requestUri, HttpStatusCode statusCode)
        {
            Message = errorMessage;
            RequestUri = requestUri;
            StatusCode = statusCode;
        }

        public ParsingErrorHttpResponse(string errorMessage, Uri requestUri)
        {
            Message = errorMessage;
            RequestUri = requestUri;
        }
    }
}