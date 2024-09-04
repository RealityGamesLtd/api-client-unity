using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;

namespace ApiClient.Runtime.HttpResponses
{
    /// <summary>
    /// A type of HttpResponse where no content was
    /// obtained from response's body.
    /// </summary>
    public class HttpResponse : IHttpResponse, IHttpResponseStatusCode
    {
        public HttpResponse(Uri uri, HttpResponseHeaders headers, HttpContentHeaders contentHeaders, HttpStatusCode statusCode)
        {
            RequestUri = uri;
            StatusCode = statusCode;
            Headers = headers.ToHeadersDictionary();
            ContentHeaders = contentHeaders.ToHeadersDictionary();
        }

        /// <summary>
        /// Error is categorised as client error when <see cref="StatusCode"/> is between 400 & 500
        /// </summary>
        /// <returns></returns>
        public bool IsClientError => (int)StatusCode >= 400 && (int)StatusCode < 500;

        /// <summary>
        /// Error is categorised as client error when <see cref="StatusCode"/> is over 500
        /// </summary>
        /// <returns></returns>
        public bool IsServerError => (int)StatusCode >= 500;
        public bool IsContentParsingError => false;
        public bool IsNetworkError => false;
        public bool IsAborted => false;
        public bool IsTimeout => false;
        public Uri RequestUri { get; private set; }
        public HttpStatusCode StatusCode { get; private set; }
        public Dictionary<string, string> Headers { get; private set; }
        public Dictionary<string, string> ContentHeaders { get; private set; }
    }
}