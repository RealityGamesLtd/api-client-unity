using System;
using System.Collections.Generic;
using System.Net.Http;

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
        public HttpRequestMessage RequestMessage { get; private set; }
        public Uri RequestUri { get; private set; }
        public Dictionary<string, string> Headers { get; private set; } = null;
        public Dictionary<string, string> ContentHeaders { get; private set; } = null;

        public TimeoutHttpResponse(HttpRequestMessage request)
        {
            RequestMessage = request;
            RequestUri = request.RequestUri;
        }

        public TimeoutHttpResponse(Uri requestUri)
        {
            RequestUri = requestUri;
        }
    }
}