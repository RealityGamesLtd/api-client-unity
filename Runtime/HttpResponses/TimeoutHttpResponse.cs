using System;
using System.Net.Http.Headers;

namespace ApiClient.Runtime.HttpResponses
{
    public class TimeoutHttpResponse : IHttpResponse
    {
        public bool IsClientError => false;
        public bool IsServerError => false;
        public bool IsNetworkError => false;
        public bool IsAborted => false;
        public bool IsTimeout => true;
        public bool IsContentParsingError => false;
        public Uri RequestUri { get; private set; }
        public HttpResponseHeaders Headers { get; private set; } = null;
        public HttpContentHeaders ContentHeaders { get; private set; } = null;

        public TimeoutHttpResponse(Uri requestUri)
        {
            RequestUri = requestUri;
        }
    }
}