using System;
using System.Collections.Generic;
using System.Net.Http;

namespace ApiClient.Runtime.HttpResponses
{
    public class TimeoutHttpResponse : IHttpResponse, IHttpResponseTiming
    {
        public bool IsClientError => false;
        public bool IsServerError => false;
        public bool IsNetworkError => false;
        public bool IsAborted => false;
        public bool IsTimeout => true;
        public bool IsContentParsingError => false;
        public HttpMethod RequestMethod { get; }
        public Uri RequestUri { get; private set; }
        public Dictionary<string, string> Headers { get; private set; } = null;
        public Dictionary<string, string> ContentHeaders { get; private set; } = null;
        public TimingInfo TimingInfo { get; set; }

        public TimeoutHttpResponse(HttpRequestMessage request)
        {
            RequestMethod = request.Method;
            RequestUri = request.RequestUri;
            TimingInfo = new TimingInfo();
        }

        public TimeoutHttpResponse(Uri requestUri)
        {
            RequestUri = requestUri;
            TimingInfo = new TimingInfo();
        }
    }
}