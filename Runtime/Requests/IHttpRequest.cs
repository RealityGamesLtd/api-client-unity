using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Threading;

namespace ApiClient.Runtime.Requests
{
    public interface IHttpRequest
    {
        bool IsSent { get; }
        CancellationToken CancellationToken { get; }
        AuthenticationHeaderValue Authentication { get; set; }
        Dictionary<string, string> DefaultHeaders { set; }

        string RequestId { get; }
    }
}