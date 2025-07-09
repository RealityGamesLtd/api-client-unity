using System;
using System.Collections.Generic;
using System.Net.Http;
using ApiClient.Runtime.HttpResponses;

namespace ApiClient.Runtime
{
    /// <summary>
    /// Response without content, with error code - use when there is no parsed content of interest, but there is some logic implemented to report errors as enums and/or user facing message.
    /// </summary>
    /// <typeparam name="T">Error code enum</typeparam>
    public class Response<T> : IHttpResponse where T : System.Enum
    {
        public bool IsClientError { get; private set; }
        public bool IsServerError { get; private set; }
        public bool IsContentParsingError { get; private set; }
        public bool IsNetworkError { get; private set; }
        public bool IsAborted { get; private set; }
        public bool IsTimeout { get; private set; }
        public bool HasNoErrors => !IsServerError && !IsClientError && !IsContentParsingError && !IsNetworkError && !IsAborted && !IsTimeout && Error == null;
        public HttpRequestMessage RequestMessage { get; private set; }
        public Uri RequestUri { get; private set; }
        public Dictionary<string, string> Headers { get; private set; }
        public Dictionary<string, string> ContentHeaders { get; private set; }

        public ResponseError<T> Error { get; private set; }

        public Response()
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="response"><see cref="IHttpResponse"/> response that was returned from <see cref="ApiClient.SendHttpRequest{T}(Requests.HttpClientRequest{T})"/></param>
        public Response(IHttpResponse response)
        {
            IsClientError = response.IsClientError;
            IsServerError = response.IsServerError;
            IsContentParsingError = response.IsContentParsingError;
            IsNetworkError = response.IsNetworkError;
            IsAborted = response.IsAborted;
            IsTimeout = response.IsTimeout;
            RequestMessage = response.RequestMessage;
            RequestUri = response.RequestUri;
            Headers = response.Headers;
            ContentHeaders = response.ContentHeaders;
        }

        public void SetError(T errorCode)
        {
            Error = new ResponseError<T>(errorCode);
        }

        public void SetError(T errorCode, string userFacingErrorMessage)
        {
            Error = new ResponseError<T>(errorCode, userFacingErrorMessage);
        }
    }
}