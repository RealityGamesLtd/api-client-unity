using System;
using System.Net;
using System.Net.Http.Headers;

namespace ApiClient.Runtime.HttpResponses
{
    public class ParsingErrorHttpResponse : IHttpResponse, IHttpResponseStatusCode, IHttpResponseBody
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
        public HttpContentHeaders ContentHeaders { get; private set; }
        public HttpStatusCode StatusCode { get; private set; }
        public string Body { get; private set; }


        public ParsingErrorHttpResponse(string errorMessage, HttpResponseHeaders headers, HttpContentHeaders contentHeaders, string body, Uri requestUri, HttpStatusCode statusCode)
        {
            Message = errorMessage;
            Headers = headers;
            RequestUri = requestUri;
            StatusCode = statusCode;
            ContentHeaders = contentHeaders;
            Body = body;
        }

        public ParsingErrorHttpResponse(string errorMessage, HttpResponseHeaders headers, Uri requestUri)
        {
            Message = errorMessage;
            Headers = headers;
            RequestUri = requestUri;
        }
    }
}