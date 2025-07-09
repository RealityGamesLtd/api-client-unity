using System;
using System.Collections.Generic;
using System.Net.Http;
using ApiClient.Runtime.HttpResponses;

namespace ApiClient.Runtime
{
    /// <summary>
    /// Response without content - use when it is only required to know that there were no errors and status code is 2XX
    /// </summary>
    public class Response : IHttpResponse
    {
        public bool IsClientError { get; private set; }
        public bool IsServerError { get; private set; }
        public bool IsContentParsingError { get; private set; }
        public bool IsNetworkError { get; private set; }
        public bool IsAborted { get; private set; }
        public bool IsTimeout { get; private set; }
        public bool HasNoErrors => !IsServerError && !IsClientError && !IsContentParsingError && !IsNetworkError && !IsAborted && !IsTimeout;
        public HttpMethod RequestMethod { get; }
        public Uri RequestUri { get; private set; }
        public Dictionary<string, string> Headers { get; private set; }
        public Dictionary<string, string> ContentHeaders { get; private set; }

        public Response()
        {

        }

        public Response(IHttpResponse httpResponse)
        {
            IsClientError = httpResponse.IsClientError;
            IsServerError = httpResponse.IsServerError;
            IsContentParsingError = httpResponse.IsContentParsingError;
            IsNetworkError = httpResponse.IsNetworkError;
            IsAborted = httpResponse.IsAborted;
            IsTimeout = httpResponse.IsTimeout;
            RequestMethod = httpResponse.RequestMethod;
            RequestUri = httpResponse.RequestUri;
            Headers = httpResponse.Headers;
            ContentHeaders = httpResponse.ContentHeaders;
        }
    }
}