using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using ApiClient.Runtime.Requests;
using ApiClient.Runtime.HttpResponses;
using UnityEngine.AI;

namespace ApiClient.Runtime
{
    public class ApiClient
    {
        private readonly GraphQLHttpClient _graphQLClient;
        private readonly HttpClient _httpClient;
        private readonly IApiClientMiddleware _middleware;
        private readonly int _streamBufferSize = 4096;
        private readonly AsyncRetryPolicy<IHttpResponse> _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .OrResult<IHttpResponse>(r =>
                r.IsTimeout ||
                r.IsNetworkError)
            // Exponential Backoff
            .WaitAndRetryAsync(
                retryCount: 0,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (response, timeSpan) =>
                {
                    // Additional logic to be executed before each retry
                });

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
            _retryPolicy = options.RetryPolicy;

            // assign stream buffer size
            _streamBufferSize = options.StreamBufferSize;

            if (Uri.IsWellFormedUriString(options.GraphQLClientEndpoint, UriKind.Absolute))
            {
                // create options for graphQLClient
                var graphQLClientOptions = new GraphQLHttpClientOptions()
                {
                    EndPoint = new Uri(options.GraphQLClientEndpoint),
                };
                // create graphQLClient using httpClient instance
                _graphQLClient = new GraphQLHttpClient(graphQLClientOptions, new NewtonsoftJsonSerializer(), _httpClient);
            }
        }

        /// <summary>
        /// Make http request using HttpCLient with no body processing.
        /// </summary>
        /// <param name="req">Request to make<</param>
        /// <returns><see cref="HttpResponse"/> or 
        /// <see cref="AbortedHttpResponse"/> or 
        /// <see cref="TimeoutHttpResponse"/> or 
        /// <see cref="NetworkErrorHttpResponse"/>.</returns>
        public async Task<IHttpResponse> SendHttpRequest(HttpClientRequest req)
        {
            await _middleware.ProcessRequest(req, true);

            IHttpResponse response = null;

            try
            {
                await _retryPolicy.ExecuteAsync(async (c, ct) =>
                {
                    response = null;

                    // if the request has been sent already we must recreate it as it's not
                    // posible to send the same request message multiple times
                    var reqest = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;

                    await _middleware.ProcessRequest(reqest, false);

                    try
                    {
                        using var responseMessage = await _httpClient.SendAsync(reqest.RequestMessage, reqest.CancellationToken);
                        response ??= new HttpResponse(
                                reqest.RequestMessage.RequestUri,
                                responseMessage.Headers,
                                responseMessage.Content.Headers,
                                responseMessage.StatusCode);
                    }
                    catch (TaskCanceledException)
                    {
                        if (reqest.CancellationToken.IsCancellationRequested)
                            response = new AbortedHttpResponse(reqest.RequestMessage.RequestUri);
                        else
                            response = new TimeoutHttpResponse(reqest.RequestMessage.RequestUri);
                    }
                    catch (Exception ex)
                    {
                        string message = "";
                        if (ex.InnerException != null) message += $"Inner exception: {ex.InnerException.Message}\n";
                        message += ex.Message;

                        response = new NetworkErrorHttpResponse(message, reqest.RequestMessage.RequestUri);
                    }

                    return await _middleware.ProcessResponse(response, reqest.RequestId, false); ;
                }, new Dictionary<string, object>() { { "httpClient", _httpClient } }, req.CancellationToken, true);
            }
            catch (OperationCanceledException)
            {
                response = new AbortedHttpResponse(req.RequestMessage.RequestUri);
            }

            return await _middleware.ProcessResponse(response, req.RequestId, true);
        }

        /// <summary>
        /// Make http request using HttpCLient with a specified response type to which the 
        /// response body will be deserialized. If deserialization is inpossible it will return
        /// <see cref="ParsingErrorHttpResponse"/>.
        /// </summary>
        /// <typeparam name="T">Response error type</typeparam>
        /// <param name="req">Request to make<</param>
        /// <returns><see cref="HttpResponse"/> or 
        /// <see cref="ParsingErrorHttpResponse"/> or 
        /// <see cref="AbortedHttpResponse"/> or 
        /// <see cref="TimeoutHttpResponse"/> or 
        /// <see cref="NetworkErrorHttpResponse"/>.</returns>
        public async Task<IHttpResponse> SendHttpRequest<T>(HttpClientRequest<T> req)
        {
            await _middleware.ProcessRequest(req, true);

            IHttpResponse response = null;

            try
            {
                await _retryPolicy.ExecuteAsync(async (c, ct) =>
                {
                    response = null;

                    // if the request has been sent already we must recreate it as it's not
                    // posible to send the same request message multiple times
                    var reqest = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;

                    await _middleware.ProcessRequest(reqest, false);

                    try
                    {
                        using var responseMessage = await _httpClient.SendAsync(reqest.RequestMessage, reqest.CancellationToken);
                        var body = await responseMessage.Content.ReadAsStringAsync();
                        var headers = responseMessage.Headers;
                        T content = default;

                        if (responseMessage?.Content?.Headers?.ContentType?.MediaType == "application/json")
                        {
                            // try parsing content with provided content type
                            try
                            {
                                content = JsonConvert.DeserializeObject<T>(body);
                            }
                            catch (Exception ex)
                            {
                                // if unsuccessfull it's not an error and content parsing failed, 
                                // return parsing error from content parsing so we can process it later
                                response = new ParsingErrorHttpResponse(
                                    ex.ToString(),
                                    responseMessage.Headers,
                                    responseMessage.Content.Headers,
                                    body,
                                    reqest.RequestMessage.RequestUri,
                                    responseMessage.StatusCode);
                            }
                        }

                        response ??= new HttpResponse<T>(
                                content,
                                responseMessage.Headers,
                                responseMessage.Content.Headers,
                                body,
                                reqest.RequestMessage.RequestUri,
                                responseMessage.StatusCode);
                    }
                    catch (TaskCanceledException)
                    {
                        if (reqest.CancellationToken.IsCancellationRequested)
                            response = new AbortedHttpResponse(reqest.RequestMessage.RequestUri);
                        else
                            response = new TimeoutHttpResponse(reqest.RequestMessage.RequestUri);
                    }
                    catch (Exception ex)
                    {
                        string message = "";
                        if (ex.InnerException != null) message += $"Inner exception: {ex.InnerException.Message}\n";
                        message += ex.Message;

                        response = new NetworkErrorHttpResponse(message, reqest.RequestMessage.RequestUri);
                    }

                    return await _middleware.ProcessResponse(response, reqest.RequestId, false);
                }, new Dictionary<string, object>() { { "httpClient", _httpClient } }, req.CancellationToken, true);
            }
            catch (OperationCanceledException)
            {
                response = new AbortedHttpResponse(req.RequestMessage.RequestUri);
            }

            return await _middleware.ProcessResponse(response, req.RequestId, true);
        }

        /// <summary>
        /// Make http request using HttpCLient with a specified response type to which the 
        /// response body will be deserialized. If deserialization is inpossible it will return
        /// <see cref="ParsingErrorHttpResponse"/>.
        /// </summary>
        /// <typeparam name="T">Response content type</typeparam>
        /// <typeparam name="E">Response error type</typeparam>
        /// <param name="req">Request to make<</param>
        /// <returns><see cref="HttpResponse"/> or 
        /// <see cref="ParsingErrorHttpResponse"/> or 
        /// <see cref="AbortedHttpResponse"/> or 
        /// <see cref="TimeoutHttpResponse"/> or 
        /// <see cref="NetworkErrorHttpResponse"/>.</returns>
        public async Task<IHttpResponse> SendHttpRequest<T, E>(HttpClientRequest<T, E> req)
        {
            await _middleware.ProcessRequest(req, true);

            IHttpResponse response = null;

            try
            {
                await _retryPolicy.ExecuteAsync(async (c, ct) =>
                {
                    response = null;

                    // if the request has been sent already we must recreate it as it's not
                    // posible to send the same request message multiple times
                    var reqest = req.IsSent ? req.RecreateWithHttpRequestMessage() : req;

                    await _middleware.ProcessRequest(reqest, false);

                    try
                    {
                        using var responseMessage = await _httpClient.SendAsync(reqest.RequestMessage, reqest.CancellationToken);
                        var body = await responseMessage.Content.ReadAsStringAsync();
                        var headers = responseMessage.Headers;
                        T content = default;
                        E error = default;

                        if (responseMessage?.Content?.Headers?.ContentType?.MediaType == "application/json")
                        {
                            // try parsing content with provided content type
                            try
                            {
                                content = JsonConvert.DeserializeObject<T>(body);
                            }
                            catch (Exception ex)
                            {
                                // if unsuccessfull it's not an error and content parsing failed, 
                                // return parsing error from content parsing so we can process it later
                                response = new ParsingErrorHttpResponse(
                                    ex.ToString(),
                                    responseMessage.Headers,
                                    responseMessage.Content.Headers,
                                    body,
                                    reqest.RequestMessage.RequestUri,
                                    responseMessage.StatusCode);
                            }

                            // if parsing content was unsuccessful then try to parse it as error
                            if (content == null)
                            {
                                // try parsing content with provided error type
                                try
                                {
                                    error = JsonConvert.DeserializeObject<E>(body);
                                }
                                catch (Exception)
                                {
                                    // do nothing with this exception as we don't want to propagate an error
                                    // of parsing error response
                                }
                            }
                        }

                        response ??= new HttpResponse<T, E>(
                                content,
                                error,
                                responseMessage.Headers,
                                responseMessage.Content.Headers,
                                body,
                                reqest.RequestMessage.RequestUri,
                                responseMessage.StatusCode);
                    }
                    catch (TaskCanceledException)
                    {
                        if (reqest.CancellationToken.IsCancellationRequested)
                            response = new AbortedHttpResponse(reqest.RequestMessage.RequestUri);
                        else
                            response = new TimeoutHttpResponse(reqest.RequestMessage.RequestUri);
                    }
                    catch (Exception ex)
                    {
                        string message = "";
                        if (ex.InnerException != null) message += $"Inner exception: {ex.InnerException.Message}\n";
                        message += ex.Message;

                        response = new NetworkErrorHttpResponse(message, reqest.RequestMessage.RequestUri);
                    }

                    return await _middleware.ProcessResponse(response, reqest.RequestId, false);
                }, new Dictionary<string, object>() { { "httpClient", _httpClient } }, req.CancellationToken, true);
            }
            catch (OperationCanceledException)
            {
                response = new AbortedHttpResponse(req.RequestMessage.RequestUri);
            }

            return await _middleware.ProcessResponse(response, req.RequestId, true);
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
        /// <returns>Request Task</returns>
        public async Task SendStreamRequest<T>(HttpClientStreamRequest<T> request, Action<IHttpResponse> OnStreamResponse)
        {
            try
            {
                await _middleware.ProcessRequest(request, true);

                using var responseMessage = await _httpClient.SendAsync(
                    request.RequestMessage,
                    HttpCompletionOption.ResponseHeadersRead,
                    request.CancellationToken);

                // read a stream only when 200 status code was returned
                if (responseMessage.IsSuccessStatusCode)
                {
                    using var contentStream = await responseMessage.Content.ReadAsStreamAsync();
                    using (StreamReader streamReader = new(contentStream, encoding: System.Text.Encoding.UTF8, true))
                    {
                        // run on non UI thread
                        await Task.Run(async () =>
                        {
                            char[] buffer = new char[_streamBufferSize];

                            do
                            {
                                if (request.CancellationToken.IsCancellationRequested)
                                {
                                    throw new TaskCanceledException();
                                }

                                int charsRead = await streamReader.ReadAsync(buffer, request.CancellationToken);
                                var readString = new string(buffer)[..charsRead];

                                // update content length
                                responseMessage.Content.Headers.ContentLength = readString.Length;

                                // extract json string
                                var regexPattern = @"({.*})";
                                MatchCollection matches = null;
                                try
                                {
                                    readString = Regex.Unescape(readString);
                                    matches = Regex.Matches(readString, regexPattern, RegexOptions.Multiline);
                                }
                                catch (Exception ex)
                                {
                                    OnStreamResponse?.InvokeOnMainThread(await _middleware.ProcessResponse(
                                        new ParsingErrorHttpResponse(
                                            ex.Message,
                                            responseMessage.Headers,
                                            responseMessage.Content.Headers,
                                            readString,
                                            request.RequestMessage.RequestUri,
                                            responseMessage.StatusCode),
                                        request.RequestId,
                                        false));
                                }

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
                                                content = JsonConvert.DeserializeObject<T>(jsonString);
                                            }
                                            catch (Exception ex)
                                            {
                                                // handle parsing error
                                                OnStreamResponse?.InvokeOnMainThread(await _middleware.ProcessResponse(
                                                    new ParsingErrorHttpResponse(
                                                        ex.Message,
                                                        responseMessage.Headers,
                                                        responseMessage.Content.Headers,
                                                        readString,
                                                        request.RequestMessage.RequestUri,
                                                        responseMessage.StatusCode),
                                                    request.RequestId,
                                                    false));
                                            }

                                            OnStreamResponse?.InvokeOnMainThread(await _middleware.ProcessResponse(
                                                new HttpResponse<T>(
                                                    content,
                                                    responseMessage.Headers,
                                                    responseMessage.Content?.Headers,
                                                    jsonString,
                                                    request.RequestMessage.RequestUri,
                                                    responseMessage.StatusCode),
                                                request.RequestId,
                                                false));
                                        }
                                        else
                                        {
                                            // handle invalid string
                                            OnStreamResponse?.InvokeOnMainThread(await _middleware.ProcessResponse(
                                                new ParsingErrorHttpResponse(
                                                    "JSON string is null",
                                                    responseMessage.Headers,
                                                    responseMessage.Content.Headers,
                                                    readString,
                                                    request.RequestMessage.RequestUri,
                                                    responseMessage.StatusCode),
                                                request.RequestId,
                                                false));
                                        }
                                    }
                                }
                                else
                                {
                                    // handle invalid string
                                    OnStreamResponse?.InvokeOnMainThread(
                                        await _middleware.ProcessResponse(
                                            new ParsingErrorHttpResponse(
                                                $"Couldn't get valid JSON string that is matching regex pattern:'{regexPattern}'",
                                                responseMessage.Headers,
                                                responseMessage.Content.Headers,
                                                readString,
                                                request.RequestMessage.RequestUri,
                                                responseMessage.StatusCode),
                                            request.RequestId,
                                            false));
                                }
                            }
                            while (!streamReader.EndOfStream && !request.CancellationToken.IsCancellationRequested);
                        }, request.CancellationToken).ContinueWith(c => { }, request.CancellationToken);
                    };
                }
                else
                {
                    // Handle non 2xx response
                    OnStreamResponse?.InvokeOnMainThread(await _middleware.ProcessResponse(new HttpResponse<T>(
                        default,
                        responseMessage.Headers,
                        null,
                        null,
                        request.RequestMessage.RequestUri,
                        responseMessage.StatusCode), request.RequestId, true));
                }
            }
            catch (OperationCanceledException)
            {
                if (request.CancellationToken.IsCancellationRequested)
                {
                    // Task was aborted
                    OnStreamResponse?.InvokeOnMainThread(await _middleware.ProcessResponse(
                        new AbortedHttpResponse(request.RequestMessage.RequestUri),
                        request.RequestId,
                        true));
                }
                else
                {
                    // timeout
                    OnStreamResponse?.InvokeOnMainThread(await _middleware.ProcessResponse(
                        new TimeoutHttpResponse(request.RequestMessage.RequestUri),
                        request.RequestId,
                        true));
                }
            }
            catch (Exception e)
            {
                // other exception
                OnStreamResponse?.InvokeOnMainThread(await _middleware.ProcessResponse(
                    new NetworkErrorHttpResponse(e.Message, request.RequestMessage.RequestUri),
                    request.RequestId,
                    true));
            }
        }

        /// <summary>
        /// Make GraphQL request
        /// </summary>
        /// <typeparam name="T">Response content type</typeparam>
        /// <param name="graphQLRequest">Request to make</param>
        /// <returns><see cref="HttpResponse"/> or 
        /// <see cref="ParsingErrorHttpResponse"/> or 
        /// <see cref="AbortedHttpResponse"/> or 
        /// <see cref="TimeoutHttpResponse"/> or 
        /// <see cref="NetworkErrorHttpResponse"/>.</returns>
        public async Task<IHttpResponse> SendGraphQLRequest<T>(GraphQLClientRequest<T> graphQLRequest)
        {
            await _middleware.ProcessRequest(graphQLRequest, true);

            IHttpResponse response = null;

            try
            {
                await _retryPolicy.ExecuteAsync(async (c, ct) =>
                {
                    await _middleware.ProcessRequest(graphQLRequest, false);

                    var graphQLResponse = _graphQLClient.SendQueryAsync<T>(graphQLRequest, graphQLRequest.CancellationToken);

                    try
                    {
                        await graphQLResponse;
                    }
                    catch (OperationCanceledException)
                    {
                        if (graphQLRequest.CancellationToken.IsCancellationRequested)
                        {
                            // Task was aborted
                            new AbortedHttpResponse(graphQLRequest.Uri);
                        }
                        else
                        {
                            // timeout
                            response = new TimeoutHttpResponse(graphQLRequest.Uri);
                        }
                    }
                    catch (JsonException e)
                    {
                        response = new ParsingErrorHttpResponse(e.Message, null, graphQLRequest.Uri);
                    }
                    catch (Exception e)
                    {
                        response = new NetworkErrorHttpResponse(e.Message, graphQLRequest.Uri);
                    }

                    if (response == null && graphQLResponse != null && graphQLResponse.Result != null)
                    {
                        // try to get graphQLHttpResponse to check status code and response headers
                        GraphQLHttpResponse<T> graphQLHttpResponse = null;
                        try
                        {
                            graphQLHttpResponse = graphQLResponse.Result.AsGraphQLHttpResponse<T>();
                        }
                        catch (Exception e)
                        {
                            // handle invalid cast
                            response = new ParsingErrorHttpResponse(e.Message, graphQLHttpResponse.ResponseHeaders, graphQLRequest.Uri);
                        }

                        if (response == null && graphQLResponse.Result.Errors == null)
                        {
                            // valid response
                            response = new HttpResponse<T>(
                                graphQLResponse.Result.Data,
                                graphQLHttpResponse.ResponseHeaders,
                                null,
                                null,
                                graphQLRequest.Uri,
                                graphQLHttpResponse.StatusCode);
                        }
                        else
                        {
                            // handle errors
                            var errorMessage = JsonConvert.SerializeObject(graphQLResponse.Result.Errors, Formatting.Indented);
                            response = new NetworkErrorHttpResponse(errorMessage, graphQLRequest.Uri);
                        }
                    }
                    else if (response == null)
                    {
                        // no result
                        response = new NetworkErrorHttpResponse("No valid result in graphQLResponse", graphQLRequest.Uri);
                    }

                    return await _middleware.ProcessResponse(response, graphQLRequest.RequestId, false);

                }, new Dictionary<string, object>() { { "httpClient", _httpClient } }, graphQLRequest.CancellationToken, true);
            }
            catch (OperationCanceledException)
            {
                response = new AbortedHttpResponse(graphQLRequest.Uri);
            }

            return await _middleware.ProcessResponse(response, graphQLRequest.RequestId, true);
        }

        /// <summary>
        /// Default middleware
        /// </summary>

        public class DefaultApiClientMiddleware : IApiClientMiddleware
        {
#pragma warning disable CS1998 // allow to run synchronusly
            public async Task ProcessRequest(IHttpRequest request, bool isResponseWithBackoff = false)
            {

            }

            public async Task<IHttpResponse> ProcessResponse(IHttpResponse response, string requestId, bool isResponseWithBackoff = false)
            {
                return response;
            }
#pragma warning restore CS1998 // allow to run synchronusly
        }
    }
}