using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime.Auxiliary;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Streaming;

namespace ApiClient.Runtime.Requests
{
    public class HttpClientStreamRequest<T> : IStreamingRequest
    {
        public bool IsSent { get; internal set; }
        public CancellationToken CancellationToken { get; }
        public HttpRequestMessage RequestMessage { get; private set; }
        public string RequestId { get; } = Guid.NewGuid().ToString();
        public string PriorityLane { get; internal set; }
        public Uri Uri { get; private set; }

        /// <summary>
        /// Strategy used to frame the response body into JSON messages. Defaults to
        /// Server-Sent Events framing; the connection factory selects a different strategy
        /// (for example newline-delimited JSON) for endpoints that stream other formats.
        /// </summary>
        public IStreamMessageReader MessageReader { get; internal set; } = ServerSentEventStreamMessageReader.Instance;

        /// <summary>
        /// When true the request is sent through a client with automatic response decompression,
        /// so the server may gzip the stream (~8-10x fewer wire bytes for JSON, proportionally
        /// less TLS buffer churn). Default false: compression is only safe for bulk-transfer
        /// streams (NDJSON waves) — an SSE stream behind a proxy that buffers gzip output would
        /// have its messages held back, defeating the stream.
        /// </summary>
        public bool AllowCompressedResponse { get; set; }

        public AuthenticationHeaderValue Authentication
        {
            get => _authentication;
            set
            {
                _authentication = value;

                // apply authentication header
                if (_authentication != null && RequestMessage?.Headers != null)
                {
                    RequestMessage.Headers.Authorization = _authentication;
                }
            }
        }

        public Dictionary<string, string> DefaultHeaders
        {
            set
            {
                if (value == null)
                {
                    return;
                }

                foreach (var kv in value)
                {
                    RequestMessage?.Headers?.Add(kv.Key, kv.Value);
                }
            }
        }

        public Dictionary<string, string> Headers
        {
            set
            {
                if (value == null)
                {
                    return;
                }

                foreach (var kv in value)
                {
                    RequestMessage?.Headers?.Add(kv.Key, kv.Value);
                }
            }
            get
            {
                return RequestMessage?.Headers?.ToHeadersDictionary();
            }
        }

        private readonly IApiClient _apiClient;

        private AuthenticationHeaderValue _authentication;


        public HttpClientStreamRequest(HttpRequestMessage requestMessage, IApiClient apiClient, CancellationToken ct)
        {
            RequestMessage = requestMessage;
            CancellationToken = ct;
            Uri = requestMessage?.RequestUri;
            _apiClient = apiClient;
        }

        public async Task Send(Action<IHttpResponse> OnStreamResponse, Action<TimeSpan> readDelta)
        {
            if (IsSent)
            {
                throw new Exception("This request has been already sent! Resending is not allowed.");
            }

            if (RequestMessage == null)
            {
                throw new Exception($"Trying to send request without {nameof(RequestMessage)}. This is not allowed");
            }

            await _apiClient.SendStreamRequest(this, OnStreamResponse, readDelta);
        }
    }
}