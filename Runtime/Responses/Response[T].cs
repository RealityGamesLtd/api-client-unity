using System;
using System.Net.Http.Headers;
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
        public Uri RequestUri { get; private set; }
        public HttpResponseHeaders Headers { get; private set; }

        public ResponseError<T> Error { get; private set; }

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
            RequestUri = httpResponse.RequestUri;
            Headers = httpResponse.Headers;
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