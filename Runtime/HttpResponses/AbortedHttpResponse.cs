using System;
using System.Net.Http.Headers;

namespace ApiClient.Runtime.HttpResponses
{
    public class AbortedHttpResponse : IHttpResponse
    {
        public bool IsClientError => false;
        public bool IsServerError => false;
        public bool IsNetworkError => false;
        public bool IsAborted => true;
        public bool IsTimeout => false;
        public bool IsContentParsingError => false;
        public Uri RequestUri { get; private set; }
        public HttpResponseHeaders Headers { get; private set; } = null;
        public HttpContentHeaders ContentHeaders { get; private set; } = null;

        public AbortedHttpResponse(Uri requestUri)
        {
            RequestUri = requestUri;
        }
    }
}