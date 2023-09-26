using System;
using System.Collections.Generic;
using System.Net.Http;
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
        Uri Uri { get; }
        HttpRequestMessage RequestMessage { get; }


        string RequestId { get; }
    }
}