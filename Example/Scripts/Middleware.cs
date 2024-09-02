using System.Text;
using System.Threading.Tasks;
using ApiClient.Runtime;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Requests;
using UnityEngine;

namespace ApiClientExample
{
    public class Middleware : IApiClientMiddleware
    {
        public async Task ProcessRequest(IHttpRequest request, bool isResponseWithBackoff = false)
        {
            if (!isResponseWithBackoff)
            {
                LogRequest(request);
            }
        }

        public async Task<IHttpResponse> ProcessResponse(IHttpResponse response, string requestId, bool isResponseWithBackoff = false)
        {
            // process only the ones without backoff included example -> every single request that's being made
            if (!isResponseWithBackoff)
            {
                // logging example
                LogResponse(response, requestId);
            }
            else
            {
                // error handling example. Here we want to process only those responses that are final
                HandleError(response);
            }

            // simulate delay with each response
            await Task.Delay(300);

            return response;
        }

        private void HandleError(IHttpResponse response)
        {
            var requestHostToConsiderGameBackend = "reality.co";

            //Only monitor requests that are going to reality.co hosts
            if (response?.RequestUri?.Host == null) return;
            if (response.RequestUri?.Host?.Equals(requestHostToConsiderGameBackend) == false) return;

            //Open network error screen on timeout or network error
            if (response.IsNetworkError || response.IsTimeout)
            {
                // ...
            }

            // Below test for cases based on status code received with the response
            var responseWithStatusCode = response as IHttpResponseStatusCode;
            var responseWithBody = response as IHttpResponseBody;

            // Check the body
            if (responseWithBody != null && !string.IsNullOrEmpty(responseWithBody.Body))
            {

            }

            // Check for maintenance
            if (responseWithStatusCode != null && responseWithStatusCode.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {

            }

            // Check for other server errors
            if ((int)responseWithStatusCode.StatusCode >= 500)
            {

            }
        }

        private async void LogRequest(IHttpRequest request)
        {
            StringBuilder stringBuilder = new();
            stringBuilder.Append($"Request -> ");
            stringBuilder.Append($"requestId: \"{request.RequestId}\", ");
            stringBuilder.Append($"\nurl: \"{request.Uri}\", ");
            stringBuilder.Append($"\nDetails: \n\"{request.RequestMessage}\"");

            if (request.RequestMessage.Content != null)
            {
                var content = await request.RequestMessage.Content.ReadAsStringAsync();
                stringBuilder.Append($"\nBody: \n\"{content}\"");
            }

            // Log request details
            Debug.Log(stringBuilder.ToString());
        }

        private void LogResponse(IHttpResponse response, string requestId)
        {
            // // get content length in bytes
            // var contentLength = response.ContentHeaders?.ContentLength;
            // var contentLengthValue = contentLength.HasValue ? contentLength.ToString() : "-";

            // Debug.Log($"RequestId: {requestId}, Url: {response.RequestUri}, Size: {contentLengthValue} bytes");



            if (response.IsAborted)
            {
                // ommit aborted
                return;
            }

            // get content length in bytes
            // var contentLength = response.ContentHeaders?.ContentLength;


            bool isFrontEndError = response.IsContentParsingError;

            string statusCodeName = "";
            if (response is IHttpResponseStatusCode responseWithStatusCode)
            {
                statusCodeName = $"{responseWithStatusCode.StatusCode} - {(int)responseWithStatusCode.StatusCode}";
            }

            string responseBody = "";
            if (response is IHttpResponseBody responseWithBody)
            {
                responseBody = responseWithBody.Body;
            }

            // Build log message
            StringBuilder stringBuilder = new();
            stringBuilder.Append($"Response -> ");

            stringBuilder.Append($"requestId: \"{requestId}\", ");

            if (!string.IsNullOrEmpty(statusCodeName))
            {
                stringBuilder.Append($"statusCode: \"{statusCodeName}\", ");
            }

            if (response.IsContentParsingError)
            {
                if (response is ParsingErrorHttpResponse parsingErrorHttpResponse)
                {
                    stringBuilder.Append($"errorMessage: \"{parsingErrorHttpResponse.Message}\", ");
                }
            }
            else if (response.IsNetworkError)
            {
                if (response is NetworkErrorHttpResponse networkErrorHttpResponse)
                {
                    stringBuilder.Append($"errorMessage: \"{networkErrorHttpResponse.Message}\", ");
                }
            }

            stringBuilder.Append($"\nurl: \"{response.RequestUri}\", ");

            if (response.ContentHeaders?.TryGetValue("Content-Length", out string contentLength) ?? false)
            {
                var contentLengthValue = string.IsNullOrEmpty(contentLength) ? contentLength.ToString() : "-";
                stringBuilder.Append($"\ncontentSize: \"{contentLengthValue} bytes\", ");
            }

            if (!string.IsNullOrEmpty(responseBody))
            {
                stringBuilder.Append($"\nbody: \n\"{responseBody}\"");
            }

            // Log response details
            if (isFrontEndError)
            {
                Debug.LogError(stringBuilder.ToString());
            }
            else
            {
                Debug.Log(stringBuilder.ToString());
            }
        }
    }
}