using System;
using System.Collections.Generic;
using ApiClient.Runtime.HttpResponses;

namespace ApiClient.Runtime
{
    /// <summary>
    /// Response with content, with error code - use when there is need for parsed content and there is logic implemented to report errors as enums and/or user facing message.
    /// </summary>
    /// <typeparam name="T">Content type</typeparam>
    /// <typeparam name="U">Error code enum</typeparam>
    public class ResponseWithContent<T, U> : IHttpResponse where U : System.Enum
    {
        public bool IsClientError { get; set; }
        public bool IsServerError { get; private set; }
        public bool IsContentParsingError { get; set; }
        public bool IsNetworkError { get; set; }
        public bool IsAborted { get; set; }
        public bool IsTimeout { get; set; }
        public bool HasNoErrors => !IsServerError && !IsClientError && !IsContentParsingError && !IsNetworkError && !IsAborted && !IsTimeout && Error == null;
        public Uri RequestUri { get; private set; }
        public bool IsFromCache { get; set; }

        public T Content { get; private set; }
        public Dictionary<string, string> Headers { get; private set; }
        public Dictionary<string, string> ContentHeaders { get; private set; }

        public ResponseError<U> Error { get; private set; }


        public ResponseWithContent()
        {

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="response"><see cref="IHttpResponse"/> response that was returned from <see cref="ApiClient.SendHttpRequest{T, E}(Requests.HttpClientRequest{T, E})"/></param>
        public ResponseWithContent(IHttpResponse response)
        {
            IsClientError = response.IsClientError;
            IsServerError = response.IsServerError;
            IsContentParsingError = response.IsContentParsingError;
            IsNetworkError = response.IsNetworkError;
            IsAborted = response.IsAborted;
            IsTimeout = response.IsTimeout;
            RequestUri = response.RequestUri;
            Headers = response.Headers;
            ContentHeaders = response.ContentHeaders;
        }

        public void SetContent(T content)
        {
            Content = content;
        }

        public void SetError(U errorCode)
        {
            Error = new ResponseError<U>(errorCode);
        }

        public void SetError(U errorCode, string userFacingErrorMessage)
        {
            Error = new ResponseError<U>(errorCode, userFacingErrorMessage);
        }
    }
}