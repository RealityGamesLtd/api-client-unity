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

        /// <summary>
        /// Optional priority lane id. When set and the executing <see cref="ApiClient"/>
        /// has a <see cref="ApiClient.Runtime.Priority.RequestPriorityCoordinator"/>
        /// configured, the send pipeline acquires a slot, yields to higher-priority lanes
        /// and registers as in-flight on this lane. Null means no priority handling
        /// (legacy behaviour).
        /// </summary>
        string PriorityLane { get; }
    }
}