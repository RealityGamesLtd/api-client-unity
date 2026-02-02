using System;
using System.Collections.Generic;
using Diagnostics = System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ApiClient.Runtime.Requests;
using ApiClient.Runtime.HttpResponses;
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

        private readonly HttpClient _httpClient;
        private readonly IApiClientMiddleware _middleware;
        private readonly int _streamBufferSize = 4096;
        private readonly int _streamReadDeltaUpdateTime = 1000;
        private readonly int _byteArrayBufferSize = 4096;
        private readonly bool _verboseLogging;
        private readonly bool _bodyLogging;
        private readonly SynchronizationContext _syncCtx = SynchronizationContext.Current;
        private readonly AsyncPolicyWrap<IHttpResponse> _retryPolicies;
        private const string NewAuthenticationHeaderValueKey = "newAuthenticationHeaderValue";
        private const string HttpClientKey = "httpClient";
        private static readonly Regex JsonExtractorRegex = new(@"({.*})", RegexOptions.Compiled | RegexOptions.Multiline);

        public ApiClient(ApiClientOptions options)
        {
            // create http client instance
            _httpClient = new HttpClient()
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
            var result = await Task.Run(async () =>
            {
                await _middleware.ProcessRequest(req, true);

                IHttpResponse response = null;

                try
                {
                    await _retryPolicies.ExecuteAsync(async (context, ct) =>
                    {
                        response = null;

                        var request = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;
                        request.IsSent = true;

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
                        catch (TaskCanceledException)
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

            return await ReturnOnSyncContext(result, req.CancellationToken);
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
            var result = await Task.Run(async () =>
            {
                await _middleware.ProcessRequest(req, true);

                IHttpResponse response = null;

                try
                {
                    await _retryPolicies.ExecuteAsync(async (context, ct) =>
                    {
                        response = null;

                        var request = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;
                        request.IsSent = true;

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
                        catch (TaskCanceledException)
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

            return await ReturnOnSyncContext(result, req.CancellationToken);
        }

        /// <summary>
        /// Make http request using HttpCLient with a specified response type to which the 
        /// response body will be deserialized. If deserialization is inpossible it will return
        /// <see cref="ParsingErrorHttpResponse"/>.
        /// </summary>
        /// <typeparam name="T">Response content type</typeparam>
        /// <typeparam name="E">Response error type</typeparam>
        /// <param name="req">Request to make</param>
        /// <returns><see cref="HttpResponse"/> or 
        /// <see cref="ParsingErrorHttpResponse"/> or 
        /// <see cref="AbortedHttpResponse"/> or 
        /// <see cref="TimeoutHttpResponse"/> or 
        /// <see cref="NetworkErrorHttpResponse"/>.</returns>
        public async Task<IHttpResponse> SendHttpRequest<T, E>(HttpClientRequest<T, E> req)
        {
            var result = await Task.Run(async () =>
            {
                await _middleware.ProcessRequest(req, true);

                IHttpResponse response = null;

                try
                {
                    await _retryPolicies.ExecuteAsync(async (context, ct) =>
                    {
                        response = null;

                        var request = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;
                        request.IsSent = true;

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
                        catch (TaskCanceledException)
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

            return await ReturnOnSyncContext(result, req.CancellationToken);
        }

        public async Task<IHttpResponse> SendHttpHeadersRequest(HttpClientHeadersRequest req)
        {
            var result = await Task.Run(async () =>
            {
                await _middleware.ProcessRequest(req, true);

                IHttpResponse response = null;

                try
                {
                    await _retryPolicies.ExecuteAsync(async (context, ct) =>
                    {
                        response = null;

                        var request = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;
                        request.IsSent = true;

                        if (context[NewAuthenticationHeaderValueKey] is AuthenticationHeaderValue newAuthHeaderValue)
                        {
                            request.Authentication = newAuthHeaderValue;
                            context[NewAuthenticationHeaderValueKey] = null;
                        }

                        await _middleware.ProcessRequest(request, false);

                        try
                        {
                            using var responseMessage = await _httpClient.SendAsync(
                                req.RequestMessage,
                                HttpCompletionOption.ResponseHeadersRead,
                                req.CancellationToken);

                            req.IsSent = true;

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
                                    req.RequestMessage,
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
                                req.RequestMessage,
                                responseMessage.StatusCode);
                        }
                        catch (TaskCanceledException)
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

            return await ReturnOnSyncContext(result, req.CancellationToken);
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
                        response = null;

                        var request = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;
                        request.IsSent = true;

                        if (context[NewAuthenticationHeaderValueKey] is AuthenticationHeaderValue newAuthHeaderValue)
                        {
                            request.Authentication = newAuthHeaderValue;
                            context[NewAuthenticationHeaderValueKey] = null;
                        }

                        await _middleware.ProcessRequest(request, false);

                        try
                        {
                            byte[] responseBytes = null;

                            using var responseMessage = await _httpClient.SendAsync(
                                req.RequestMessage,
                                HttpCompletionOption.ResponseHeadersRead,
                                req.CancellationToken);

                            req.IsSent = true;

                            if (!responseMessage.IsSuccessStatusCode)
                            {
                                if (_verboseLogging)
                                {
                                    Debug.LogError($"{nameof(ApiClient)}:{nameof(SendByteArrayRequest)} statusCode:{responseMessage.StatusCode}");
                                }

                                response = new HttpResponse<byte[]>(
                                    default,
                                    responseMessage.Headers,
                                    null,
                                    null,
                                    req.RequestMessage,
                                    responseMessage.StatusCode);
                                return response;
                            }

                            if (_verboseLogging)
                            {
                                Debug.Log($"{nameof(ApiClient)}:{nameof(SendByteArrayRequest)} statusCode:{responseMessage.StatusCode}");
                            }

                            await using var contentStream = await responseMessage.Content.ReadAsStreamAsync();
                            var responseStream = PrepareJsonStream(contentStream, responseMessage.Content.Headers);

                            var contentLengthFromHeader = responseMessage.Content.Headers.ContentLength ?? 0L;
                            var totalBytesRead = 0L;
                            var buffer = new byte[_byteArrayBufferSize];
                            var isMoreToRead = true;

                            using var memoryStream = new MemoryStream();

                            do
                            {
                                if (req.CancellationToken.IsCancellationRequested)
                                {
                                    throw new TaskCanceledException();
                                }

                                var bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, req.CancellationToken);
                                if (bytesRead == 0)
                                {
                                    isMoreToRead = false;

                                    if (_verboseLogging)
                                    {
                                        Debug.Log($"{nameof(ApiClient)}:{nameof(SendByteArrayRequest)} All bytes read.");
                                    }

                                    continue;
                                }

                                await memoryStream.WriteAsync(buffer, 0, bytesRead);
                                totalBytesRead += bytesRead;

                                if (_verboseLogging)
                                {
                                    Debug.Log($"{nameof(ApiClient)}:{nameof(SendByteArrayRequest)} Update progress: {totalBytesRead}/{contentLengthFromHeader}.");
                                }

                                progressCallback?.PostOnMainThread(new(totalBytesRead, contentLengthFromHeader), _syncCtx);
                            }
                            while (isMoreToRead && !ct.IsCancellationRequested);

                            if (ct.IsCancellationRequested)
                            {
                                throw new TaskCanceledException();
                            }

                            responseBytes = memoryStream.ToArray();

                            UpdateResponseMetrics(responseBytes.Length, contentLengthFromHeader);

                            response = new HttpResponse<byte[]>(
                                responseBytes,
                                responseMessage.Headers,
                                responseMessage.Content?.Headers,
                                null,
                                req.RequestMessage,
                                responseMessage.StatusCode);
                        }
                        catch (TaskCanceledException)
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

            return await ReturnOnSyncContext(result, req.CancellationToken);
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
                    request.IsSent = true;

                    await _middleware.ProcessRequest(request, true);

                    Profiler.BeginSample($"Api Client Execute Stream Request: {request.Uri}");

                    using var responseMessage = await _httpClient.SendAsync(
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
                        _ = UpdateReadDeltaValueTask(() => { return streamLastReadTime; }, readDelta, updateReadDeltaValueCts.Token);

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

                                            OnStreamResponse?.PostOnMainThread(await _middleware.ProcessResponse(
                                                new HttpResponse<T>(
                                                    content,
                                                    responseMessage.Headers,
                                                    responseMessage.Content?.Headers,
                                                    jsonString,
                                                    request.RequestMessage.RequestUri,
                                                    responseMessage.StatusCode),
                                                request.RequestId,
                                                false),
                                                _syncCtx);
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

        protected Stream PrepareJsonStream(Stream source, HttpContentHeaders headers)
        {
            if (headers.ContentEncoding.Contains("gzip"))
            {
                return new GZipStream(source, CompressionMode.Decompress);
            }
            return source;
        }

        protected T DeserializeJson<T>(Stream memoryStream, HttpContentHeaders headers, string profilerLabel, out long bytesRead)
        {
            memoryStream.Position = 0;
            var jsonStream = PrepareJsonStream(memoryStream, headers);

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
                var bodyJsonStream = PrepareJsonStream(memoryStream, headers);
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

        protected async Task<IHttpResponse> ReturnOnSyncContext(IHttpResponse result, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<IHttpResponse>();
            if (cancellationToken.CanBeCanceled)
                cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

            _syncCtx.Post(_ => 
            { 
                if(result != null && cancellationToken.IsCancellationRequested == false)
                {
                    tcs.SetResult(result); 
                }
            }, null);
            return await tcs.Task;
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

        #endregion
    }
}