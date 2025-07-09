using System;
using System.Collections.Generic;
using System.Net.Http;

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
        public HttpRequestMessage RequestMessage { get; private set; }
        public Uri RequestUri { get; private set; }
        public Dictionary<string, string> Headers { get; private set; } = null;
        public Dictionary<string, string> ContentHeaders { get; private set; } = null;

        public AbortedHttpResponse(Uri requestUri)
        {
            RequestUri = requestUri;
        }
    }
}