using System;
using System.Net;
using System.Net.Http.Headers;

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
        public HttpResponseHeaders Headers { get; private set; }


        public HttpStatusCode StatusCode { get; private set; }

        public ParsingErrorHttpResponse(string errorMessage, HttpResponseHeaders headers, Uri requestUri, HttpStatusCode statusCode)
        {
            Message = errorMessage;
            Headers = headers;
            RequestUri = requestUri;
            StatusCode = statusCode;
        }

        public ParsingErrorHttpResponse(string errorMessage, HttpResponseHeaders headers, Uri requestUri)
        {
            Message = errorMessage;
            Headers = headers;
            RequestUri = requestUri;
        }
    }
}