using System;
using System.Net.Http.Headers;

namespace ApiClient.Runtime.HttpResponses
{
    public class NetworkErrorHttpResponse : IHttpResponse
    {
        public bool IsClientError => false;
        public bool IsServerError => false;
        public bool IsNetworkError => true;
        public bool IsAborted => false;
        public bool IsTimeout => false;
        public bool IsContentParsingError => false;
        public Uri RequestUri { get; private set; }
        public HttpResponseHeaders Headers { get; private set; } = null;
        public HttpContentHeaders ContentHeaders { get; private set; } = null;

        public string Message { get; }

        public NetworkErrorHttpResponse(string message, Uri requestUri)
        {
            Message = message;
            RequestUri = requestUri;
        }
    }
}