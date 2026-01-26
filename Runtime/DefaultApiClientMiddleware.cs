using System.Threading.Tasks;
using ApiClient.Runtime.Requests;
using ApiClient.Runtime.HttpResponses;

namespace ApiClient.Runtime
{
    /// <summary>
    /// Default middleware
    /// </summary>
    public sealed class DefaultApiClientMiddleware : IApiClientMiddleware
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