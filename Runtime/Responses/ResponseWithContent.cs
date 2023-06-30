using System;
using System.Net.Http.Headers;
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
        public U ErrorCode { get; private set; }
        public string UserFacingErrorMessage { get; private set; }
        public HttpResponseHeaders Headers { get; private set; }

        public ResponseError<U> Error { get; private set; }


        public ResponseWithContent()
        {

        }

        public ResponseWithContent(IHttpResponse httpResponse)
        {
            IsClientError = httpResponse.IsClientError;
            IsServerError = httpResponse.IsServerError;
            IsContentParsingError = httpResponse.IsContentParsingError;
            IsNetworkError = httpResponse.IsNetworkError;
            IsAborted = httpResponse.IsAborted;
            IsTimeout = httpResponse.IsTimeout;
            RequestUri = httpResponse.RequestUri;
            Headers = httpResponse.Headers;
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