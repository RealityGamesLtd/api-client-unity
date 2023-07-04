using System.Threading.Tasks;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Requests;

namespace ApiClient.Runtime
{
    /// <summary>
    /// Middleware interface that allows to inject some logic before the response is returned.
    /// Potential use cases: Logging, Time measuring, Error handling, Simulating of delayed responses or failures
    /// by completely overriding the response.
    /// 
    /// Classes that implement this interfaces and are making processing that isn't instant should also have
    /// Cancellation Token instance passed to them. So there's no processing while e.g. unity runtime is off.
    /// </summary>
    public interface IApiClientMiddleware
    {
        Task ProcessRequest(IHttpRequest request, bool isResponseWithBackoff = false);
        Task<IHttpResponse> ProcessResponse(IHttpResponse response, string requestId, bool isResponseWithBackoff = false);
    }
}