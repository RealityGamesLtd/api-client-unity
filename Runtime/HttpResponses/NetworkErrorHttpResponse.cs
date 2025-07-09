using System;
using System.Collections.Generic;
using System.Net.Http;

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
        public HttpMethod RequestMethod { get; }
        public Uri RequestUri { get; private set; }
        public Dictionary<string, string> Headers { get; private set; } = null;
        public Dictionary<string, string> ContentHeaders { get; private set; } = null;

        public string Message { get; }

        public NetworkErrorHttpResponse(string message, HttpRequestMessage request)
        {
            Message = message;
            RequestMethod = request.Method;
            RequestUri = request.RequestUri;
        }

        public NetworkErrorHttpResponse(string message, Uri requestUri)
        {
            Message = message;
            RequestUri = requestUri;
        }
    }
}