using System.Threading.Tasks;
using ApiClient.Runtime.Requests;
using ApiClient.Runtime.HttpResponses;

namespace ApiClient.Runtime
{
    public partial class ApiClient
    {
        /// <summary>
        /// Default middleware
        /// </summary>
        private sealed class DefaultApiClientMiddleware : IApiClientMiddleware
        {
#pragma warning disable CS1998 // allow to run synchronusly
            public async Task ProcessRequest(IHttpRequest request, bool isResponseWithBackoff = false)
            {

            }

            public async Task<IHttpResponse> ProcessResponse(IHttpResponse response, string requestId, bool exhaustedRetries = false)
            {
                return response;
            }
#pragma warning restore CS1998 // allow to run synchronusly
        }
    }
}