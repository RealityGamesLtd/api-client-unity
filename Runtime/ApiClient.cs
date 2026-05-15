using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Stopwatch = System.Diagnostics.Stopwatch;
using Newtonsoft.Json;
using ApiClient.Runtime.Requests;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Priority;
using System.Threading;
using ApiClient.Runtime.Cache;
using Polly.Wrap;
using UnityEngine;
using UnityEngine.Profiling;
using ApiClient.Runtime.Auxiliary;

namespace ApiClient.Runtime
{
    public class ApiClient : IApiClient
    {
        private long _responseTotalCompressedBytes;
        private long _responseTotalUncompressedBytes;
        public long ResponseTotalCompressedBytes => _responseTotalCompressedBytes;
        public long ResponseTotalUncompressedBytes => _responseTotalUncompressedBytes;

        public UrlCache Cache { get; } = new();

        public event Action<RequestTimingSample> OnRequestCompleted;

        private readonly HttpClient _httpClient;
        private readonly HttpClient _streamHttpClient;
        private readonly IApiClientMiddleware _middleware;
        private readonly int _streamBufferSize = 4096;
        private readonly int _streamReadDeltaUpdateTime = 1000;
        private readonly int _byteArrayBufferSize = 4096;
        private readonly bool _verboseLogging;
        private readonly bool _bodyLogging;
        private readonly SynchronizationContext _syncCtx = SynchronizationContext.Current;
        private readonly AsyncPolicyWrap<IHttpResponse> _retryPolicies;
        private readonly RequestPriorityCoordinator _priority;
        private readonly RangeChunkedDownloadOptions _rangeOpts;
        private bool _disposed;
        private const string NewAuthenticationHeaderValueKey = "newAuthenticationHeaderValue";
        private const string HttpClientKey = "httpClient";
        private static readonly Regex JsonExtractorRegex = new(@"({.*})", RegexOptions.Compiled | RegexOptions.Multiline);
        private readonly HttpClientHandler _httpClientHandler;
        private readonly HttpClientHandler _streamHttpClientHandler;

        public ApiClient(ApiClientOptions options)
        {
            _priority = options.PriorityCoordinator;
            _rangeOpts = options.RangeDownload ?? new RangeChunkedDownloadOptions();

            _httpClientHandler = new HttpClientHandler
            {
                AutomaticDecompression = options.AutomaticDecompression
            };
            _httpClient = new HttpClient(_httpClientHandler, disposeHandler: true)
            {
                Timeout = options.Timeout
            };

            _streamHttpClientHandler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.None
            };
            _streamHttpClient = new HttpClient(_streamHttpClientHandler, disposeHandler: true)
            {
                Timeout = options.Timeout
            };

            // assign middleware
            _middleware = options.Middleware ?? new DefaultApiClientMiddleware();

            // assign custom retry policy
            _retryPolicies = options.RetryPolicies;

            // assign stream buffer size
            _streamBufferSize = options.StreamBufferSize;

            // assign byte array buffer size
            _byteArrayBufferSize = options.ByteArrayBufferSize;

            // assign verbose logging
            _verboseLogging = options.VerboseLogging;

            // assign body logging
            _bodyLogging = options.BodyLogging;

            _streamReadDeltaUpdateTime = options.StreamReadDeltaUpdateTime;
        }

        // Acquire bulkhead slot, yield to higher-priority lanes, and register as in-flight
        // on the request's lane. Disposing the returned handshake exits the lane (LIFO)
        // and releases the slot. No-op when no coordinator is configured or the request
        // carries no priority lane id.
        private async Task<PriorityHandshake> BeginPriorityAsync(IHttpRequest req, CancellationToken ct)
        {
            if (_priority == null || req.PriorityLane == null)
                return default;

            var slot = await _priority.AcquireSlotAsync(req.PriorityLane, ct).ConfigureAwait(false);
            try
            {
                await _priority.WaitForYieldedLanesIdleAsync(req.PriorityLane, ct).ConfigureAwait(false);
            }
            catch
            {
                slot.Dispose();
                throw;
            }
            var scope = _priority.EnterLane(req.PriorityLane);
            return new PriorityHandshake(slot, scope);
        }

        // Allocation-light disposable bundling slot + lane scope. Default value is a no-op.
        private readonly struct PriorityHandshake : IDisposable
        {
            private readonly IDisposable _slot;
            private readonly LaneScope _scope;

            public PriorityHandshake(IDisposable slot, LaneScope scope)
            {
                _slot = slot;
                _scope = scope;
            }

            public void Dispose()
            {
                // LIFO: exit the lane first so anyone yielding to us can resume,
                // then release the bulkhead slot.
                _scope.Dispose();
                _slot?.Dispose();
            }
        }

        private bool ShouldUseChunkedRange(IHttpRequest req)
        {
            if (_priority == null || req.PriorityLane == null) return false;
            return _priority.GetLaneConfig(req.PriorityLane).ChunkedRangeDownloads;
        }

        // Emits one RequestTimingSample for a completed call. Tolerant of any null
        // input — only the duration is required to be meaningful. Subscriber exceptions
        // are swallowed so a buggy listener can't poison the send pipeline.
        private void EmitTiming(
            Stopwatch sw,
            IHttpResponse response,
            HttpMethod method,
            string requestUri,
            string priorityLane)
        {
            var handler = OnRequestCompleted;
            if (handler == null) return;

            sw.Stop();

            // IsSuccess: bytes flowed end-to-end. Parsing errors count as success
            // (network was healthy; only deserialisation failed). Aborts/timeouts/
            // network errors carry meaningless durations and must NOT bias an EWMA.
            var isSuccess =
                response != null
                && !response.IsAborted
                && !response.IsTimeout
                && !response.IsNetworkError;

            var isFromCache = response is ICachedHttpResponse cached && cached.IsFromCache;

            HttpStatusCode? statusCode = response is IHttpResponseStatusCode withStatus
                ? withStatus.StatusCode
                : (HttpStatusCode?)null;

            var sample = new RequestTimingSample(
                sw.Elapsed,
                isSuccess,
                isFromCache,
                method,
                requestUri,
                statusCode,
                priorityLane);

            try
            {
                handler(sample);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Make http request using HttpCLient with no body processing.
        /// </summary>
        /// <param name="req">Request to make</param>
        /// <returns><see cref="HttpResponse"/> or 
        /// <see cref="AbortedHttpResponse"/> or 
        /// <see cref="TimeoutHttpResponse"/> or 
        /// <see cref="NetworkErrorHttpResponse"/>.</returns>
        public async Task<IHttpResponse> SendHttpRequest(HttpClientRequest req)
        {
            // Snapshot identifying metadata BEFORE the send pipeline mutates / disposes
            // req.RequestMessage during retries. Stopwatch captures total user-perceived
            // call time (Task.Run schedule + middleware + Polly retry budget + sync ctx).
            var __method = req?.RequestMessage?.Method;
            var __uri = req?.RequestMessage?.RequestUri?.ToString();
            var __lane = req?.PriorityLane;
            var __sw = Stopwatch.StartNew();
            IHttpResponse __final = null;
            try
            {
            var result = await Task.Run(async () =>
            {
                await _middleware.ProcessRequest(req, true);

                IHttpResponse response = null;

                try
                {
                    await _retryPolicies.ExecuteAsync(async (context, ct) =>
                    {
                        using var __pri = await BeginPriorityAsync(req, ct).ConfigureAwait(false);

                        response = null;

                        HttpClientRequest request;
                        lock (req)
                        {
                            request = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;
                            request.IsSent = true;
                        }

                        if (context[NewAuthenticationHeaderValueKey] is AuthenticationHeaderValue newAuthHeaderValue)
                        {
                            request.Authentication = newAuthHeaderValue;
                            context[NewAuthenticationHeaderValueKey] = null;
                        }

                        await _middleware.ProcessRequest(request, false);

                        try
                        {
                            using var responseMessage = await _httpClient.SendAsync(request.RequestMessage, request.CancellationToken);
                            response = new HttpResponse(
                                request.RequestMessage,
                                responseMessage.Headers,
                                responseMessage.Content.Headers,
                                responseMessage.StatusCode);
                        }
                        catch (OperationCanceledException)
                        {
                            if (request.CancellationToken.IsCancellationRequested)
                                response = new AbortedHttpResponse(request.RequestMessage);
                            else
                                response = new TimeoutHttpResponse(request.RequestMessage);
                        }
                        catch (Exception ex)
                        {
                            var message = $"Type: {ex.GetType()}\nMessage: {ex.Message}\nInner exception type:{ex.InnerException?.GetType()}\nInner exception: {ex.InnerException?.Message}\n";
                            response = new NetworkErrorHttpResponse(message, request.RequestMessage);
                        }

                        return await _middleware.ProcessResponse(response, request.RequestId, false);
                    }, new Dictionary<string, object>() { { HttpClientKey, _httpClient }, { NewAuthenticationHeaderValueKey, null } }, req.CancellationToken, true);
                }
                catch (OperationCanceledException)
                {
                    response = new AbortedHttpResponse(req.RequestMessage);
                }

                return await _middleware.ProcessResponse(response, req.RequestId, true);
            }, req.CancellationToken);

            __final = await ReturnOnSyncContext(result);
            return __final;
            }
            finally
            {
                EmitTiming(__sw, __final, __method, __uri, __lane);
            }
        }

        /// <summary>
        /// Make http request using HttpCLient with a specified response type to which the
        /// response body will be deserialized. If deserialization is inpossible it will return
        /// <see cref="ParsingErrorHttpResponse"/>.
        /// </summary>
        /// <typeparam name="E">Response error type</typeparam>
        /// <param name="req">Request to make<</param>
        /// <returns><see cref="HttpResponse"/> or 
        /// <see cref="ParsingErrorHttpResponse"/> or 
        /// <see cref="AbortedHttpResponse"/> or 
        /// <see cref="TimeoutHttpResponse"/> or 
        /// <see cref="NetworkErrorHttpResponse"/>.</returns>
        public async Task<IHttpResponse> SendHttpRequest<E>(HttpClientRequest<E> req)
        {
            var __method = req?.RequestMessage?.Method;
            var __uri = req?.RequestMessage?.RequestUri?.ToString();
            var __lane = req?.PriorityLane;
            var __sw = Stopwatch.StartNew();
            IHttpResponse __final = null;
            try
            {
            var result = await Task.Run(async () =>
            {
                await _middleware.ProcessRequest(req, true);

                IHttpResponse response = null;

                try
                {
                    await _retryPolicies.ExecuteAsync(async (context, ct) =>
                    {
                        using var __pri = await BeginPriorityAsync(req, ct).ConfigureAwait(false);

                        response = null;

                        HttpClientRequest<E> request;
                        lock (req)
                        {
                            request = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;
                            request.IsSent = true;
                        }

                        if (context[NewAuthenticationHeaderValueKey] is AuthenticationHeaderValue newAuthHeaderValue)
                        {
                            request.Authentication = newAuthHeaderValue;
                            context[NewAuthenticationHeaderValueKey] = null;
                        }

                        await _middleware.ProcessRequest(request, false);

                        Profiler.BeginSample($"Api Client Execute Request [E]: {request.Uri}");

                        try
                        {
                            using var responseMessage = await _httpClient.SendAsync(request.RequestMessage, request.CancellationToken);

                            var (error, body, errorResponse) = await ProcessJsonErrorResponse<E>(responseMessage, request.RequestMessage);

                            response = errorResponse ?? new HttpResponse<E>(
                                error,
                                responseMessage.Headers,
                                responseMessage.Content?.Headers,
                                body,
                                request.RequestMessage,
                                responseMessage.StatusCode);
                        }
                        catch (OperationCanceledException)
                        {
                            if (request.CancellationToken.IsCancellationRequested)
                                response = new AbortedHttpResponse(request.RequestMessage);
                            else
                                response = new TimeoutHttpResponse(request.RequestMessage);
                        }
                        catch (Exception ex)
                        {
                            var message = $"Type: {ex.GetType()}\nMessage: {ex.Message}\nInner exception type:{ex.InnerException?.GetType()}\nInner exception: {ex.InnerException?.Message}\n";
                            response = new NetworkErrorHttpResponse(message, request.RequestMessage);
                        }

                        Profiler.EndSample();

                        return await _middleware.ProcessResponse(response, request.RequestId, false);
                    }, new Dictionary<string, object>() { { HttpClientKey, _httpClient }, { NewAuthenticationHeaderValueKey, null } }, req.CancellationToken, true);
                }
                catch (OperationCanceledException)
                {
                    response = new AbortedHttpResponse(req.RequestMessage);
                }

                return await _middleware.ProcessResponse(response, req.RequestId, true);
            }, req.CancellationToken);

            __final = await ReturnOnSyncContext(result);
            return __final;
            }
            finally
            {
                EmitTiming(__sw, __final, __method, __uri, __lane);
            }
        }

        /// <summary>
        /// Sends an HTTP request and returns a typed response.
        /// </summary>
        /// <typeparam name="T">Response body type.</typeparam>
        /// <typeparam name="E">Response error type.</typeparam>
        /// <param name="req">Request to make.</param>
        /// <returns><see cref="HttpResponse"/> or 
        /// <see cref="ParsingErrorHttpResponse"/> or 
        /// <see cref="AbortedHttpResponse"/> or 
        /// <see cref="TimeoutHttpResponse"/> or 
        /// <see cref="NetworkErrorHttpResponse"/>.</returns>
        public async Task<IHttpResponse> SendHttpRequest<T, E>(HttpClientRequest<T, E> req)
        {
            var __method = req?.RequestMessage?.Method;
            var __uri = req?.RequestMessage?.RequestUri?.ToString();
            var __lane = req?.PriorityLane;
            var __sw = Stopwatch.StartNew();
            IHttpResponse __final = null;
            try
            {
            var result = await Task.Run(async () =>
            {
                await _middleware.ProcessRequest(req, true);

                IHttpResponse response = null;

                try
                {
                    await _retryPolicies.ExecuteAsync(async (context, ct) =>
                    {
                        using var __pri = await BeginPriorityAsync(req, ct).ConfigureAwait(false);

                        response = null;

                        HttpClientRequest<T, E> request;
                        lock (req)
                        {
                            request = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;
                            request.IsSent = true;
                        }

                        if (context[NewAuthenticationHeaderValueKey] is AuthenticationHeaderValue newAuthHeaderValue)
                        {
                            request.Authentication = newAuthHeaderValue;
                            context[NewAuthenticationHeaderValueKey] = null;
                        }

                        await _middleware.ProcessRequest(request, false);

                        Profiler.BeginSample($"Api Client Execute Request: {request.Uri}");
                        try
                        {
                            using var responseMessage = await _httpClient.SendAsync(request.RequestMessage, request.CancellationToken);

                            var (content, error, body, errorResponse) = await ProcessJsonResponse<T, E>(responseMessage, request.RequestMessage);

                            response = errorResponse ?? new HttpResponse<T, E>(
                                content,
                                error,
                                responseMessage.Headers,
                                responseMessage.Content?.Headers,
                                body,
                                request.RequestMessage,
                                responseMessage.StatusCode);
                        }
                        catch (OperationCanceledException)
                        {
                            if (request.CancellationToken.IsCancellationRequested)
                                response = new AbortedHttpResponse(request.RequestMessage);
                            else
                                response = new TimeoutHttpResponse(request.RequestMessage);
                        }
                        catch (Exception ex)
                        {
                            var message = $"Type: {ex.GetType()}\nMessage: {ex.Message}\nInner exception type:{ex.InnerException?.GetType()}\nInner exception: {ex.InnerException?.Message}\n";
                            response = new NetworkErrorHttpResponse(message, request.RequestMessage);
                        }

                        Profiler.EndSample();

                        return await _middleware.ProcessResponse(response, request.RequestId, false);
                    }, new Dictionary<string, object>() { { HttpClientKey, _httpClient }, { NewAuthenticationHeaderValueKey, null } }, req.CancellationToken, true);
                }
                catch (OperationCanceledException)
                {
                    response = new AbortedHttpResponse(req.RequestMessage);
                }

                return await _middleware.ProcessResponse(response, req.RequestId, true);
            }, req.CancellationToken);

            __final = await ReturnOnSyncContext(result);
            return __final;
            }
            finally
            {
                EmitTiming(__sw, __final, __method, __uri, __lane);
            }
        }


        public async Task<IHttpResponse> SendHttpHeadersRequest(HttpClientHeadersRequest req)
        {
            var __method = req?.RequestMessage?.Method;
            var __uri = req?.RequestMessage?.RequestUri?.ToString();
            var __lane = req?.PriorityLane;
            var __sw = Stopwatch.StartNew();
            IHttpResponse __final = null;
            try
            {
            var result = await Task.Run(async () =>
            {
                await _middleware.ProcessRequest(req, true);

                IHttpResponse response = null;

                try
                {
                    await _retryPolicies.ExecuteAsync(async (context, ct) =>
                    {
                        using var __pri = await BeginPriorityAsync(req, ct).ConfigureAwait(false);

                        response = null;

                        HttpClientHeadersRequest request;
                        lock (req)
                        {
                            request = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;
                            request.IsSent = true;
                        }

                        if (context[NewAuthenticationHeaderValueKey] is AuthenticationHeaderValue newAuthHeaderValue)
                        {
                            request.Authentication = newAuthHeaderValue;
                            context[NewAuthenticationHeaderValueKey] = null;
                        }

                        await _middleware.ProcessRequest(request, false);

                        try
                        {
                            using var responseMessage = await _httpClient.SendAsync(
                                request.RequestMessage,
                                HttpCompletionOption.ResponseHeadersRead,
                                request.CancellationToken);

                            if (!responseMessage.IsSuccessStatusCode)
                            {
                                if (_verboseLogging)
                                {
                                    Debug.LogError($"{nameof(ApiClient)}:{nameof(SendHttpHeadersRequest)} statusCode:{responseMessage.StatusCode}");
                                }

                                response = new HttpResponse<byte[]>(
                                    default,
                                    responseMessage.Headers,
                                    null,
                                    null,
                                    request.RequestMessage,
                                    responseMessage.StatusCode);
                                return response;
                            }

                            if (_verboseLogging)
                            {
                                Debug.Log($"{nameof(ApiClient)}:{nameof(SendHttpHeadersRequest)} statusCode:{responseMessage.StatusCode}");
                            }

                            response = new HttpResponse<byte[]>(
                                null,
                                responseMessage.Headers,
                                responseMessage.Content?.Headers,
                                null,
                                request.RequestMessage,
                                responseMessage.StatusCode);
                        }
                        catch (OperationCanceledException)
                        {
                            if (request.CancellationToken.IsCancellationRequested)
                                response = new AbortedHttpResponse(request.RequestMessage);
                            else
                                response = new TimeoutHttpResponse(request.RequestMessage);
                        }
                        catch (Exception ex)
                        {
                            var message = $"Type: {ex.GetType()}\nMessage: {ex.Message}\nInner exception type:{ex.InnerException?.GetType()}\nInner exception: {ex.InnerException?.Message}\n";
                            response = new NetworkErrorHttpResponse(message, request.RequestMessage);
                        }

                        return await _middleware.ProcessResponse(response, request.RequestId, false);
                    }, new Dictionary<string, object>() { { HttpClientKey, _httpClient }, { NewAuthenticationHeaderValueKey, null } }, req.CancellationToken, true);
                }
                catch (OperationCanceledException)
                {
                    response = new AbortedHttpResponse(req.RequestMessage);
                }

                return await _middleware.ProcessResponse(response, req.RequestId, true);
            }, req.CancellationToken);

            __final = await ReturnOnSyncContext(result);
            return __final;
            }
            finally
            {
                EmitTiming(__sw, __final, __method, __uri, __lane);
            }
        }

        public async Task<IHttpResponse> SendByteArrayRequest(
            HttpClientByteArrayRequest req,
            Action<ByteArrayRequestProgress> progressCallback = null)
        {
            var result = await Task.Run(async () =>
            {
                await _middleware.ProcessRequest(req, true);

                IHttpResponse response = null;

                try
                {
                    await _retryPolicies.ExecuteAsync(async (context, ct) =>
                    {
                        using var __pri = await BeginPriorityAsync(req, ct).ConfigureAwait(false);

                        response = null;

                        HttpClientByteArrayRequest request;
                        lock (req)
                        {
                            request = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;
                            request.IsSent = true;
                        }

                        if (context[NewAuthenticationHeaderValueKey] is AuthenticationHeaderValue newAuthHeaderValue)
                        {
                            request.Authentication = newAuthHeaderValue;
                            context[NewAuthenticationHeaderValueKey] = null;
                        }

                        await _middleware.ProcessRequest(request, false);

                        try
                        {
                            if (ShouldUseChunkedRange(request))
                            {
                                response = await ChunkedByteArrayDownloadAsync(request, progressCallback, _httpClient, request.CancellationToken).ConfigureAwait(false);
                            }
                            else
                            {
                                var gate = _priority != null && request.PriorityLane != null && _rangeOpts.FallbackPreemptInBufferLoop;
                                response = await LegacyByteArrayDownloadAsync(request, progressCallback, _httpClient, gate, request.CancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            if (request.CancellationToken.IsCancellationRequested)
                                response = new AbortedHttpResponse(request.RequestMessage);
                            else
                                response = new TimeoutHttpResponse(request.RequestMessage);
                        }
                        catch (Exception ex)
                        {
                            var message = $"Type: {ex.GetType()}\nMessage: {ex.Message}\nInner exception type:{ex.InnerException?.GetType()}\nInner exception: {ex.InnerException?.Message}\n";
                            response = new NetworkErrorHttpResponse(message, request.RequestMessage);
                        }

                        return await _middleware.ProcessResponse(response, request.RequestId, false);
                    }, new Dictionary<string, object>() { { HttpClientKey, _httpClient }, { NewAuthenticationHeaderValueKey, null } }, req.CancellationToken, true);
                }
                catch (OperationCanceledException)
                {
                    response = new AbortedHttpResponse(req.RequestMessage);
                }

                return await _middleware.ProcessResponse(response, req.RequestId, true);
            }, req.CancellationToken);

            return await ReturnOnSyncContext(result);
        }

        /// <summary>
        /// Single-stream byte-array download. When <paramref name="gateBetweenReads"/> is
        /// true, awaits the priority coordinator's idle gate before each <c>ReadAsync</c>
        /// so a saturated download yields some bandwidth back to gameplay traffic via TCP
        /// back-pressure (less effective than chunk-level preemption but safe).
        /// </summary>
        private async Task<IHttpResponse> LegacyByteArrayDownloadAsync(
            HttpClientByteArrayRequest request,
            Action<ByteArrayRequestProgress> progressCallback,
            HttpClient client,
            bool gateBetweenReads,
            CancellationToken ct)
        {
            using var responseMessage = await client.SendAsync(
                request.RequestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            return await DrainResponseToByteArrayResponseAsync(
                request, responseMessage, progressCallback, gateBetweenReads, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Drains the body of an already-issued response into a byte[] response, with
        /// optional gameplay-idle gate between buffer reads. Used by both the legacy path
        /// and the Range-fallback path (when probe returned 200).
        /// </summary>
        private async Task<IHttpResponse> DrainResponseToByteArrayResponseAsync(
            HttpClientByteArrayRequest request,
            HttpResponseMessage responseMessage,
            Action<ByteArrayRequestProgress> progressCallback,
            bool gateBetweenReads,
            CancellationToken ct)
        {
            if (!responseMessage.IsSuccessStatusCode)
            {
                if (_verboseLogging)
                {
                    Debug.LogError($"{nameof(ApiClient)}:{nameof(SendByteArrayRequest)} statusCode:{responseMessage.StatusCode}");
                }

                return new HttpResponse<byte[]>(
                    default,
                    responseMessage.Headers,
                    null,
                    null,
                    request.RequestMessage,
                    responseMessage.StatusCode);
            }

            if (_verboseLogging)
            {
                Debug.Log($"{nameof(ApiClient)}:{nameof(SendByteArrayRequest)} statusCode:{responseMessage.StatusCode}");
            }

            await using var contentStream = await responseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false);

            var contentLengthFromHeader = responseMessage.Content.Headers.ContentLength ?? 0L;
            var totalBytesRead = 0L;
            var buffer = new byte[_byteArrayBufferSize];
            var isMoreToRead = true;

            using var memoryStream = new MemoryStream();

            do
            {
                ct.ThrowIfCancellationRequested();

                if (gateBetweenReads && _priority != null && request.PriorityLane != null)
                {
                    await _priority.WaitForYieldedLanesIdleAsync(request.PriorityLane, ct).ConfigureAwait(false);
                }

                var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    isMoreToRead = false;

                    if (_verboseLogging)
                    {
                        Debug.Log($"{nameof(ApiClient)}:{nameof(SendByteArrayRequest)} All bytes read.");
                    }

                    continue;
                }

                await memoryStream.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
                totalBytesRead += bytesRead;

                if (_verboseLogging)
                {
                    Debug.Log($"{nameof(ApiClient)}:{nameof(SendByteArrayRequest)} Update progress: {totalBytesRead}/{contentLengthFromHeader}.");
                }

                progressCallback?.PostOnMainThread(new ByteArrayRequestProgress(totalBytesRead, contentLengthFromHeader), _syncCtx);
            }
            while (isMoreToRead);

            ct.ThrowIfCancellationRequested();

            var responseBytes = memoryStream.ToArray();

            UpdateResponseMetrics(responseBytes.Length, contentLengthFromHeader);

            return new HttpResponse<byte[]>(
                responseBytes,
                responseMessage.Headers,
                responseMessage.Content?.Headers,
                null,
                request.RequestMessage,
                responseMessage.StatusCode);
        }

        /// <summary>
        /// Chunked HTTP Range download. The first request doubles as a probe and the first
        /// chunk: if the server answers <c>206 Partial Content</c>, we parse the total size
        /// from <c>Content-Range</c> and loop the remaining ranges. Between chunks we await
        /// the coordinator's gameplay-idle gate so the asset transfer pauses while gameplay
        /// requests are in flight (sub-second preemption granularity).
        ///
        /// If the server answers <c>200 OK</c> (Range not honoured) we transparently fall
        /// back to draining the same response stream as a legacy single-GET, with the
        /// same gate-between-reads behaviour.
        ///
        /// On a successful chunked assembly the synthesized response carries
        /// <c>HttpStatusCode.OK</c> so the URL cache stores it as a normal success and
        /// downstream consumers don't see a <c>206</c>.
        /// </summary>
        private async Task<IHttpResponse> ChunkedByteArrayDownloadAsync(
            HttpClientByteArrayRequest request,
            Action<ByteArrayRequestProgress> progressCallback,
            HttpClient client,
            CancellationToken ct)
        {
            var chunkSize = Math.Max(1, _rangeOpts.ChunkSizeBytes);

            // Probe = first chunk: ask for bytes 0..chunkSize-1. If the server honours Range
            // we get 206 + Content-Range; if it doesn't we get the full body in 200.
            request.RequestMessage.Headers.Range = new RangeHeaderValue(0, chunkSize - 1);

            using var probeResponse = await client.SendAsync(
                request.RequestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            if (!probeResponse.IsSuccessStatusCode)
            {
                if (_verboseLogging)
                {
                    Debug.LogError($"{nameof(ApiClient)}:{nameof(ChunkedByteArrayDownloadAsync)} probe statusCode:{probeResponse.StatusCode}");
                }
                return new HttpResponse<byte[]>(
                    default,
                    probeResponse.Headers,
                    null,
                    null,
                    request.RequestMessage,
                    probeResponse.StatusCode);
            }

            if (probeResponse.StatusCode != HttpStatusCode.PartialContent)
            {
                // Server ignored Range — drain as legacy single-GET with optional gate.
                return await DrainResponseToByteArrayResponseAsync(
                    request,
                    probeResponse,
                    progressCallback,
                    gateBetweenReads: _rangeOpts.FallbackPreemptInBufferLoop,
                    ct).ConfigureAwait(false);
            }

            // Validate the probe's Content-Range. A server returning a shifted unit, a
            // non-zero From, or a body length that disagrees with To-From+1 would silently
            // corrupt the assembled buffer. Per-chunk validation in DownloadOneChunkAsync
            // covers later chunks; the probe needs the same treatment.
            var probeRange = probeResponse.Content.Headers.ContentRange;
            if (probeRange == null
                || !probeRange.HasRange
                || probeRange.Unit != "bytes"
                || probeRange.From != 0
                || probeRange.To == null
                || probeRange.Length == null)
            {
                if (_verboseLogging)
                {
                    Debug.LogWarning($"{nameof(ApiClient)}:{nameof(ChunkedByteArrayDownloadAsync)} 206 with malformed Content-Range '{probeRange}', falling back to single drain.");
                }
                return await DrainResponseToByteArrayResponseAsync(
                    request,
                    probeResponse,
                    progressCallback,
                    gateBetweenReads: _rangeOpts.FallbackPreemptInBufferLoop,
                    ct).ConfigureAwait(false);
            }

            var totalLength = probeRange.Length.Value;

            using var memoryStream = new MemoryStream(capacity: (int)Math.Min(totalLength, int.MaxValue));

            // 1. Drain first chunk into the assembly buffer.
            await using (var firstChunkStream = await probeResponse.Content.ReadAsStreamAsync().ConfigureAwait(false))
            {
                await firstChunkStream.CopyToAsync(memoryStream, _byteArrayBufferSize, ct).ConfigureAwait(false);
            }
            var offset = memoryStream.Length;

            // The probe body's actual byte count must match (To - From + 1). A short read
            // (truncated stream / premature EOF) is an integrity failure on this attempt;
            // throw so the outer Polly wrap can retry the whole transfer.
            var expectedProbeBytes = probeRange.To.Value - probeRange.From.Value + 1;
            if (offset != expectedProbeBytes)
            {
                throw new IOException(
                    $"Probe body length {offset} bytes does not match Content-Range '{probeRange}' (expected {expectedProbeBytes}).");
            }

            progressCallback?.PostOnMainThread(new ByteArrayRequestProgress(offset, totalLength), _syncCtx);

            // 2. Loop remaining chunks. Each chunk is a fresh HttpRequestMessage cloned from
            // the original (HttpRequestMessage cannot be reused across SendAsync calls).
            while (offset < totalLength)
            {
                ct.ThrowIfCancellationRequested();

                // Coarse-grained preemption: pause while any yielded-to lane is in flight.
                await _priority.WaitForYieldedLanesIdleAsync(request.PriorityLane, ct).ConfigureAwait(false);

                var chunkEnd = Math.Min(offset + chunkSize - 1, totalLength - 1);

                var chunkResult = await DownloadOneChunkAsync(
                    request, client, memoryStream, offset, chunkEnd, totalLength, progressCallback, ct).ConfigureAwait(false);

                if (chunkResult.legacyFallbackResponse != null)
                {
                    return chunkResult.legacyFallbackResponse; // server stopped honouring Range mid-transfer
                }

                if (!chunkResult.succeeded)
                {
                    // exhausted per-chunk retries with a server-side error response (4xx/5xx).
                    return chunkResult.lastErrorResponse;
                }

                offset = chunkEnd + 1;
                progressCallback?.PostOnMainThread(new ByteArrayRequestProgress(offset, totalLength), _syncCtx);
            }

            ct.ThrowIfCancellationRequested();

            var responseBytes = memoryStream.ToArray();
            UpdateResponseMetrics(responseBytes.Length, totalLength);

            // The probe's Content-Range / chunk Content-Length describe the first chunk
            // only — they would lie about the assembled body if cached. Build a fresh
            // header set with the right Content-Length and Range-specific entries removed.
            var assembledContentHeaders = BuildAssembledContentHeaders(probeResponse.Content?.Headers, totalLength);

            // Synthesize a 200 OK so URL cache stores the assembled response normally and
            // downstream consumers never see 206.
            return new HttpResponse<byte[]>(
                responseBytes,
                probeResponse.Headers,
                assembledContentHeaders,
                null,
                request.RequestMessage,
                HttpStatusCode.OK);
        }

        private static HttpContentHeaders BuildAssembledContentHeaders(HttpContentHeaders source, long totalLength)
        {
            if (source == null) return null;

            // ByteArrayContent gives us a writable HttpContentHeaders without allocating
            // a real body buffer.
            var carrier = new ByteArrayContent(Array.Empty<byte>());
            var headers = carrier.Headers;

            foreach (var header in source)
            {
                if (string.Equals(header.Key, "Content-Range", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            headers.ContentLength = totalLength;
            return headers;
        }

        /// <summary>
        /// Downloads one Range chunk into <paramref name="memoryStream"/>. Per-chunk retries
        /// transient network errors so a mid-transfer hiccup does not throw away the bytes
        /// already received. On exhaustion rethrows so the outer Polly wrap can retry the
        /// whole transfer.
        /// </summary>
        /// <returns>
        /// Tuple of (succeeded, legacyFallbackResponse, lastErrorResponse).
        /// When the server returns 200 mid-transfer (Range no longer honoured),
        /// <c>legacyFallbackResponse</c> is non-null and the chunked path aborts.
        /// </returns>
        private async Task<(bool succeeded, IHttpResponse legacyFallbackResponse, IHttpResponse lastErrorResponse)> DownloadOneChunkAsync(
            HttpClientByteArrayRequest request,
            HttpClient client,
            MemoryStream memoryStream,
            long offset,
            long chunkEnd,
            long totalLength,
            Action<ByteArrayRequestProgress> progressCallback,
            CancellationToken ct)
        {
            var attempts = Math.Max(1, _rangeOpts.MaxChunkRetries + 1);

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                using var chunkRequest = CloneRequestForRange(request.RequestMessage, offset, chunkEnd);
                try
                {
                    using var chunkResponse = await client.SendAsync(
                        chunkRequest,
                        HttpCompletionOption.ResponseHeadersRead,
                        ct).ConfigureAwait(false);

                    if (chunkResponse.StatusCode == HttpStatusCode.PartialContent)
                    {
                        // Validate Content-Range. A misbehaving server that returns the
                        // wrong window or a truncated body would otherwise silently
                        // corrupt the assembled payload.
                        var expectedLength = chunkEnd - offset + 1;
                        var contentRange = chunkResponse.Content?.Headers?.ContentRange;
                        if (contentRange == null
                            || !contentRange.HasRange
                            || contentRange.Unit != "bytes"
                            || contentRange.From != offset
                            || contentRange.To != chunkEnd)
                        {
                            throw new IOException(
                                $"Invalid Content-Range for chunk {offset}-{chunkEnd}: '{contentRange}'.");
                        }

                        var startLength = memoryStream.Length;
                        try
                        {
                            await using var chunkStream = await chunkResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
                            await chunkStream.CopyToAsync(memoryStream, _byteArrayBufferSize, ct).ConfigureAwait(false);

                            var appended = memoryStream.Length - startLength;
                            if (appended != expectedLength)
                            {
                                throw new IOException(
                                    $"Chunk {offset}-{chunkEnd} returned {appended} bytes, expected {expectedLength}.");
                            }
                            return (true, null, null);
                        }
                        catch
                        {
                            // Roll back partial writes so the per-chunk retry sees a clean stream.
                            memoryStream.SetLength(startLength);
                            memoryStream.Position = startLength;
                            throw;
                        }
                    }

                    if (chunkResponse.StatusCode == HttpStatusCode.OK)
                    {
                        // Server stopped honouring Range mid-transfer. Drop the partial
                        // assembly and re-issue a fresh full GET.
                        if (_verboseLogging)
                        {
                            Debug.LogWarning($"{nameof(ApiClient)}:{nameof(DownloadOneChunkAsync)} server returned 200 mid-transfer at offset {offset}; falling back to full GET.");
                        }
                        var fallback = await FallbackFullDownloadAsync(request, client, progressCallback, ct).ConfigureAwait(false);
                        return (false, fallback, null);
                    }

                    // 4xx/5xx — return as the last-error response. Outer Polly may retry.
                    var errorResponse = new HttpResponse<byte[]>(
                        default,
                        chunkResponse.Headers,
                        chunkResponse.Content?.Headers,
                        null,
                        request.RequestMessage,
                        chunkResponse.StatusCode);
                    return (false, null, errorResponse);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Treat as transient if it wasn't user cancellation.
                    if (attempt == attempts - 1) throw;
                    await Task.Delay(BackoffForChunkAttempt(attempt), ct).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    if (attempt == attempts - 1) throw;
                    await Task.Delay(BackoffForChunkAttempt(attempt), ct).ConfigureAwait(false);
                }
                catch (IOException)
                {
                    if (attempt == attempts - 1) throw;
                    await Task.Delay(BackoffForChunkAttempt(attempt), ct).ConfigureAwait(false);
                }
            }

            // Loop completed without success. Should not normally reach here because the
            // last attempt rethrows; defensive return.
            return (false, null, null);
        }

        private static TimeSpan BackoffForChunkAttempt(int attempt)
        {
            // 200ms, 400ms, 800ms, ... capped at 5s.
            var ms = Math.Min(5000, 200 * (1 << attempt));
            return TimeSpan.FromMilliseconds(ms);
        }

        private static HttpRequestMessage CloneRequestForRange(HttpRequestMessage src, long from, long to)
        {
            var clone = new HttpRequestMessage(src.Method, src.RequestUri)
            {
                Version = src.Version
            };
            foreach (var header in src.Headers)
            {
                if (string.Equals(header.Key, "Range", StringComparison.OrdinalIgnoreCase)) continue;
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            clone.Headers.Range = new RangeHeaderValue(from, to);
            return clone;
        }

        private async Task<IHttpResponse> FallbackFullDownloadAsync(
            HttpClientByteArrayRequest request,
            HttpClient client,
            Action<ByteArrayRequestProgress> progressCallback,
            CancellationToken ct)
        {
            using var fullRequest = CloneRequestForFullGet(request.RequestMessage);
            using var fullResponse = await client.SendAsync(
                fullRequest,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            return await DrainResponseToByteArrayResponseAsync(
                request,
                fullResponse,
                progressCallback,
                gateBetweenReads: _rangeOpts.FallbackPreemptInBufferLoop,
                ct).ConfigureAwait(false);
        }

        private static HttpRequestMessage CloneRequestForFullGet(HttpRequestMessage src)
        {
            var clone = new HttpRequestMessage(src.Method, src.RequestUri)
            {
                Version = src.Version
            };
            foreach (var header in src.Headers)
            {
                if (string.Equals(header.Key, "Range", StringComparison.OrdinalIgnoreCase)) continue;
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return clone;
        }

        /// <summary>
        /// Make stream request using HttpClient, responses can be accessed by the <see cref="OnStreamResponse"/> callback
        /// as long as the task is running.
        /// </summary>
        /// <typeparam name="T">Response content type</typeparam>
        /// <param name="request">Request to make</param>
        /// <param name="OnStreamResponse">Callback action to retrieve responses of: <see cref="HttpResponse"/> or 
        /// <see cref="ParsingErrorHttpResponse"/> or 
        /// <see cref="AbortedHttpResponse"/> or 
        /// <see cref="TimeoutHttpResponse"/> or 
        /// <see cref="NetworkErrorHttpResponse"/></param>
        /// <param name="readDelta">Callback action to notify about read delta times</param>
        /// <returns>Request Task</returns>
        public async Task SendStreamRequest<T>(
            HttpClientStreamRequest<T> request,
            Action<IHttpResponse> OnStreamResponse,
            Action<TimeSpan> readDelta)
        {
            await Task.Run(async () =>
            {
                DateTime streamLastReadTime = DateTime.UtcNow;
                var updateReadDeltaValueCts = new CancellationTokenSource();

                request.RequestMessage.Headers.Remove("Accept-Encoding");

                try
                {
                    lock (request)
                    {
                        if (request.IsSent)
                        {
                            throw new InvalidOperationException("This request has already been sent and cannot be sent again.");
                        }

                        request.IsSent = true;
                    }

                    await _middleware.ProcessRequest(request, true);

                    Profiler.BeginSample($"Api Client Execute Stream Request: {request.Uri}");

                    using var responseMessage = await _streamHttpClient.SendAsync(
                        request.RequestMessage,
                        HttpCompletionOption.ResponseHeadersRead,
                        request.CancellationToken);

                    // read a stream only when 200 status code was returned
                    if (!responseMessage.IsSuccessStatusCode)
                    {
                        if (_verboseLogging)
                        {
                            Debug.LogError($"{nameof(ApiClient)}:{nameof(SendStreamRequest)} statusCode:{responseMessage.StatusCode}");
                        }

                        // Handle non 2xx response
                        OnStreamResponse?.PostOnMainThread(await _middleware.ProcessResponse(new HttpResponse<T>(
                            default,
                            responseMessage.Headers,
                            null,
                            null,
                            request.RequestMessage,
                            responseMessage.StatusCode), request.RequestId, true),
                            _syncCtx);
                        return;
                    }

                    if (_verboseLogging)
                    {
                        Debug.Log($"{nameof(ApiClient)}:{nameof(SendStreamRequest)} statusCode:{responseMessage.StatusCode}");
                    }

                    await using var contentStream = await responseMessage.Content.ReadAsStreamAsync();
                    using (StreamReader streamReader = new(contentStream, encoding: Encoding.UTF8, true))
                    {
                        // start task that will update read delta regularly
                        _ = UpdateReadDeltaValueTask(() => { return streamLastReadTime; }, readDelta, updateReadDeltaValueCts.Token).HandleTaskContinuation();

                        char[] buffer = new char[_streamBufferSize];
                        var partialMessageBuilder = new StringBuilder();

                        do
                        {
                            if (request.CancellationToken.IsCancellationRequested)
                            {
                                throw new TaskCanceledException();
                            }

                            // read the stream
                            int charsRead = await streamReader.ReadAsync(buffer, request.CancellationToken);
                            var readString = new string(buffer, 0, charsRead);

                            // update read time
                            streamLastReadTime = DateTime.UtcNow;

                            /*
                                 On some platform the message might be returned in chunks. 
                                 "0A 0A" -> "\n\n" ending characters mean that we've got full message.
                                 "0D 0A" -> "\r\n" means that we haven't
                                 As a workaround check for those endings and combine full message from them.
                             */
                            if (readString.EndsWith("\n\n") == false)
                            {
                                partialMessageBuilder.Append(readString);
                                continue;
                            }
                            else
                            {
                                if (partialMessageBuilder.Length > 0)
                                {
                                    partialMessageBuilder.Append(readString);
                                    readString = partialMessageBuilder.ToString();
                                    partialMessageBuilder.Clear();
                                }
                            }

                            // update content length
                            responseMessage.Content.Headers.ContentLength = readString.Length;

                            // extract json string
                            MatchCollection matches = null;
                            try
                            {
                                Profiler.BeginSample("Api Client Stream Regex Extraction");
                                matches = JsonExtractorRegex.Matches(readString);
                            }
                            catch (Exception ex)
                            {
                                OnStreamResponse?.PostOnMainThread(await _middleware.ProcessResponse(
                                    new ParsingErrorHttpResponse(
                                        ex.Message,
                                        responseMessage.Headers,
                                        responseMessage.Content.Headers,
                                        readString,
                                        request.RequestMessage.RequestUri,
                                        responseMessage.StatusCode),
                                    request.RequestId,
                                    false),
                                _syncCtx);
                            }
                            finally
                            {
                                Profiler.EndSample();
                            }

                            // process matches
                            if (matches != null && matches.Count > 0)
                            {
                                for (int i = 0; i < matches.Count; i++)
                                {
                                    if (request.CancellationToken.IsCancellationRequested)
                                    {
                                        throw new TaskCanceledException();
                                    }

                                    var jsonString = matches[i].Value;

                                    if (!string.IsNullOrEmpty(jsonString))
                                    {
                                        T content = default;
                                        try
                                        {
                                            Profiler.BeginSample("Api Client Stream Deserialization");
                                            content = JsonConvert.DeserializeObject<T>(jsonString);
                                            Profiler.EndSample();

                                            if (_verboseLogging)
                                            {
                                                Debug.Log($"{nameof(ApiClient)}:{nameof(SendStreamRequest)} Got stream message:{jsonString}");
                                            }

                                            var response = new HttpResponse<T>(
                                                content,
                                                responseMessage.Headers,
                                                responseMessage.Content?.Headers,
                                                jsonString,
                                                request.RequestMessage.RequestUri,
                                                responseMessage.StatusCode);

                                            OnStreamResponse?.PostOnMainThread(response, _syncCtx);
                                        }
                                        catch (Exception ex)
                                        {
                                            Profiler.EndSample();
                                            // handle parsing error
                                            OnStreamResponse?.PostOnMainThread(await _middleware.ProcessResponse(
                                                new ParsingErrorHttpResponse(
                                                    ex.Message,
                                                    responseMessage.Headers,
                                                    responseMessage.Content.Headers,
                                                    readString,
                                                    request.RequestMessage.RequestUri,
                                                    responseMessage.StatusCode),
                                                request.RequestId,
                                                false),
                                                _syncCtx);
                                        }
                                    }
                                    else
                                    {
                                        // handle invalid string
                                        OnStreamResponse?.PostOnMainThread(await _middleware.ProcessResponse(
                                            new ParsingErrorHttpResponse(
                                                "JSON string is null",
                                                responseMessage.Headers,
                                                responseMessage.Content.Headers,
                                                readString,
                                                request.RequestMessage.RequestUri,
                                                responseMessage.StatusCode),
                                            request.RequestId,
                                            false),
                                            _syncCtx);
                                    }
                                }
                            }
                            else
                            {
                                // handle no matches
                                OnStreamResponse?.PostOnMainThread(
                                    await _middleware.ProcessResponse(
                                        new ParsingErrorHttpResponse(
                                            $"Couldn't get valid JSON string that is matching regex pattern",
                                            responseMessage.Headers,
                                            responseMessage.Content.Headers,
                                            readString,
                                            request.RequestMessage.RequestUri,
                                            responseMessage.StatusCode),
                                        request.RequestId,
                                        false),
                                    _syncCtx);
                            }
                        }
                        while (!streamReader.EndOfStream && !request.CancellationToken.IsCancellationRequested);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (request.CancellationToken.IsCancellationRequested)
                    {
                        OnStreamResponse?.PostOnMainThread(await _middleware.ProcessResponse(
                            new AbortedHttpResponse(request.RequestMessage),
                            request.RequestId,
                            true),
                            _syncCtx);
                    }
                    else
                    {
                        OnStreamResponse?.PostOnMainThread(await _middleware.ProcessResponse(
                            new TimeoutHttpResponse(request.RequestMessage),
                            request.RequestId,
                            true),
                            _syncCtx);
                    }
                }
                catch (Exception e)
                {
                    OnStreamResponse?.PostOnMainThread(await _middleware.ProcessResponse(
                        new NetworkErrorHttpResponse(e.Message, request.RequestMessage),
                        request.RequestId,
                        true),
                        _syncCtx);
                }
                finally
                {
                    updateReadDeltaValueCts?.Cancel();
                    Profiler.EndSample();
                }

                async Task UpdateReadDeltaValueTask(Func<DateTime> streamLastRead, Action<TimeSpan> readDelta, CancellationToken ct)
                {
                    while (!ct.IsCancellationRequested)
                    {
                        // calculate delta between last read and current read
                        var readDeltaValue = DateTime.UtcNow.Subtract(streamLastRead());
                        readDelta?.PostOnMainThread(readDeltaValue, _syncCtx);

                        try
                        {
                            await Task.Delay(_streamReadDeltaUpdateTime, ct);
                        }
                        catch (OperationCanceledException) { }
                    }
                }
            }, request.CancellationToken);
        }

        #region Helper Methods

        protected T DeserializeJson<T>(Stream memoryStream, HttpContentHeaders headers, string profilerLabel, out long bytesRead)
        {
            memoryStream.Position = 0;
            var jsonStream = memoryStream;

            Profiler.BeginSample(profilerLabel);
            try
            {
                using var countingStream = new CountingStream(jsonStream);
                using var reader = new StreamReader(countingStream, Encoding.UTF8, true, 1024, leaveOpen: true);
                using var jsonReader = new JsonTextReader(reader);
                var result = JsonSerializer.CreateDefault().Deserialize<T>(jsonReader);
                bytesRead = countingStream.BytesRead;

                return result;
            }
            finally
            {
                Profiler.EndSample();
            }
        }

        protected async Task<string> ReadBodyForLoggingAsync(Stream memoryStream, HttpContentHeaders headers)
        {
            if (!_bodyLogging)
                return string.Empty;

            Profiler.BeginSample("Api Client Body Read");
            try
            {
                memoryStream.Position = 0;
                var bodyJsonStream = memoryStream;
                using var bodyStreamReader = new StreamReader(bodyJsonStream, Encoding.UTF8, true, 1024, leaveOpen: true);
                return await bodyStreamReader.ReadToEndAsync();
            }
            finally
            {
                Profiler.EndSample();
            }
        }

        protected void UpdateResponseMetrics(long bytesRead, long? headerContentLength)
        {
            Interlocked.Add(ref _responseTotalUncompressedBytes, bytesRead);
            Interlocked.Add(ref _responseTotalCompressedBytes, headerContentLength ?? bytesRead);
        }

        protected Task<IHttpResponse> ReturnOnSyncContext(IHttpResponse result)
        {
            if (result == null)
                throw new InvalidOperationException(
                    $"{nameof(ReturnOnSyncContext)}: middleware returned a null {nameof(IHttpResponse)}. " +
                    "Ensure all IApiClientMiddleware.ProcessResponse implementations return a non-null value.");

            // No sync context (e.g. unit tests, thread-pool construction) — return directly.
            if (_syncCtx == null)
                return Task.FromResult(result);

            var tcs = new TaskCompletionSource<IHttpResponse>();
            _syncCtx.Post(_ => tcs.SetResult(result), null);
            return tcs.Task;
        }

        protected async Task<(T content, E error, string body, IHttpResponse errorResponse)> ProcessJsonResponse<T, E>(
            HttpResponseMessage responseMessage,
            HttpRequestMessage requestMessage)
        {
            T content = default;
            E error = default;
            string body = string.Empty;
            IHttpResponse errorResponse = null;

            await using var stream = await responseMessage.Content.ReadAsStreamAsync();

            if (responseMessage?.Content?.Headers?.ContentType?.MediaType != "application/json")
            {
                return (content, error, body, errorResponse);
            }

            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            try
            {
                // If status code indicates error, prioritize error deserialization
                if ((int)responseMessage.StatusCode >= 400)
                {
                    try
                    {
                        error = DeserializeJson<E>(memoryStream, responseMessage.Content.Headers, "Api Client Error Deserialization", out var errorBytesRead);
                        UpdateResponseMetrics(errorBytesRead, responseMessage.Content.Headers.ContentLength);
                    }
                    catch (Exception)
                    {
                        // Silently ignore error deserialization failures
                    }
                }
                else
                {
                    // Try to deserialize content for success status codes
                    content = DeserializeJson<T>(memoryStream, responseMessage.Content.Headers, "Api Client Content Deserialization", out var contentBytesRead);
                    UpdateResponseMetrics(contentBytesRead, responseMessage.Content.Headers.ContentLength);
                }

                body = await ReadBodyForLoggingAsync(memoryStream, responseMessage.Content.Headers);
            }
            catch (Exception ex)
            {
                errorResponse = new ParsingErrorHttpResponse(
                    ex.ToString(),
                    responseMessage.Headers,
                    responseMessage.Content.Headers,
                    body,
                    requestMessage.RequestUri,
                    responseMessage.StatusCode);
            }

            return (content, error, body, errorResponse);
        }

        protected async Task<(E error, string body, IHttpResponse errorResponse)> ProcessJsonErrorResponse<E>(
            HttpResponseMessage responseMessage,
            HttpRequestMessage requestMessage)
        {
            E error = default;
            string body = string.Empty;
            IHttpResponse errorResponse = null;

            await using var stream = await responseMessage.Content.ReadAsStreamAsync();

            if (responseMessage?.Content?.Headers?.ContentType?.MediaType != "application/json")
            {
                return (error, body, errorResponse);
            }

            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            try
            {
                if ((int)responseMessage.StatusCode >= 400)
                {
                    try
                    {
                        error = DeserializeJson<E>(memoryStream, responseMessage.Content.Headers, "Api Client Error Deserialization [E]", out var errorBytesRead);
                        UpdateResponseMetrics(errorBytesRead, responseMessage.Content.Headers.ContentLength);
                    }
                    catch (Exception)
                    {
                        // Silently ignore error deserialization failures
                    }
                }

                body = await ReadBodyForLoggingAsync(memoryStream, responseMessage.Content.Headers);
            }
            catch (Exception ex)
            {
                errorResponse = new ParsingErrorHttpResponse(
                    ex.ToString(),
                    responseMessage.Headers,
                    responseMessage.Content.Headers,
                    body,
                    requestMessage.RequestUri,
                    responseMessage.StatusCode);
            }

            return (error, body, errorResponse);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _httpClient?.Dispose();
                _streamHttpClient?.Dispose();
                OnRequestCompleted = null;
            }

            _disposed = true;
        }

        #endregion
    }

    public static class ApiClientTasksExtensions
    {
        /// <summary>
        /// Extension method that provides continuation with exception handling
        /// </summary>
        /// <param name="task"></param>
        /// <returns></returns>
        public static Task HandleTaskContinuation(this Task task)
        {
            task.ContinueWith(
                faultedTask => Debug.LogException(faultedTask.Exception),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

            return task;
        }

        /// <summary>
        /// Extension method that provides continuation with exception handling
        /// </summary>
        /// <param name="task"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static Task<T> HandleTaskContinuation<T>(this Task<T> task)
        {
            task.ContinueWith(
                faultedTask => Debug.LogException(faultedTask.Exception),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

            return task;
        }
    }
}