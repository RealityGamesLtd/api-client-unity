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

        private void LogResponse(IHttpResponse response, string requestId)
        {
            // get content length in bytes
            var contentLength = response.ContentHeaders?.ContentLength;
            var contentLengthValue = contentLength.HasValue ? contentLength.ToString() : "-";

            Debug.Log($"Request: {requestId}, Url: {response.RequestUri}, Size: {contentLengthValue} bytes");
        }
    }
}