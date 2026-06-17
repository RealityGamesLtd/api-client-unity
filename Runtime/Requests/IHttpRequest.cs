using System;
using System.Collections.Generic;
using System.Net;
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

        /// <summary>
        /// Status codes the caller considers normal for this request (e.g. a 404 from a
        /// "does this exist?" lookup). When the executing <see cref="ApiClient"/> has
        /// verbose logging enabled, a non-success response whose code is listed here is
        /// logged at info level instead of as an error. This is a logging hint only — it
        /// does not change response semantics: the response is still surfaced as a client
        /// error (<see cref="ApiClient.Runtime.HttpResponses.IHttpResponse.IsClientError"/>)
        /// or server error (<see cref="ApiClient.Runtime.HttpResponses.IHttpResponse.IsServerError"/>)
        /// and is still not cached. Null means every non-success code logs as an error
        /// (legacy behaviour).
        /// </summary>
        IReadOnlyCollection<HttpStatusCode> ExpectedStatusCodes { get; }
    }
}