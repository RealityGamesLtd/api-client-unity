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
using UnityEngine;

namespace ApiClient.Runtime
{
    public class ApiClient
    {
        /// <summary>
        /// Called before sending a request. Backoff included
        /// </summary>
        public event Action<IHttpRequest> OnWillSendRequestWithBackoff;
        /// <summary>
        /// Called on single request within backoff
        /// </summary>
        public event Action<RequestInfo> OnRequest;
        /// <summary>
        /// Called on single response within backoff
        /// </summary>
        public event Action<ResponseInfo> OnResponse;
        /// <summary>
        /// Called after recieving a response. Backoff included
        /// </summary>
        public event Action<IHttpResponse> OnResponseReceivedWithBackoff;


        private readonly GraphQLHttpClient graphQLClient;
        private readonly System.Net.Http.HttpClient httpClient;

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
            httpClient = new HttpClient()
            {
                Timeout = options.Timeout
            };

            // assign custom retry policy
            _retryPolicy = options.RetryPolicy;

            // create options for graphQLClient
            var graphQLClientOptions = new GraphQLHttpClientOptions()
            {
                EndPoint = new Uri(options.GraphQLClientEndpoint),
            };
            // create graphQLClient using httpClient instance
            graphQLClient = new GraphQLHttpClient(graphQLClientOptions, new NewtonsoftJsonSerializer(), httpClient);
        }

        /// <summary>
        /// Make http request using HttpCLient with no body processing.
        /// </summary>
        /// <param name="req">Request to make<</param>
        /// <returns><see cref="HttpResponse"/> or <see cref="AbortedHttpResponse"/> or <see cref="TimeoutHttpResponse"/> or <see cref="NetworkErrorHttpResponse"/>.</returns>
        public async Task<IHttpResponse> SendHttpRequest(HttpClientRequest req)
        {
            OnWillSendRequestWithBackoff?.Invoke(req);

            IHttpResponse response = null;

            try
            {
                await _retryPolicy.ExecuteAsync(async (c, ct) =>
                {
                    response = null;

                    var reqest = req.RecreateWithHttpRequestMessage();

                    OnRequest?.Invoke(new RequestInfo(reqest.RequestId, reqest));

                    try
                    {
                        using (var responseMessage = await httpClient.SendAsync(reqest.RequestMessage, reqest.CancellationToken))
                        {
                            if (response == null)
                            {
                                response = new HttpResponse(reqest.RequestMessage.RequestUri, responseMessage.Headers, responseMessage.StatusCode);
                            }
                        }
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

                    OnResponse?.Invoke(new ResponseInfo(reqest.RequestId, response));
                    return response;
                }, new Dictionary<string, object>() { { "httpClient", httpClient } }, req.CancellationToken, true);
            }
            catch (OperationCanceledException)
            {
                response = new AbortedHttpResponse(req.RequestMessage.RequestUri);
            }

            OnResponseReceivedWithBackoff?.Invoke(response);

            return response;
        }

        /// <summary>
        /// Make http request using HttpCLient with a specified response type to which the 
        /// response body will be deserialized. If deserialization is inpossible it will return
        /// <see cref="ParsingErrorHttpResponse"/>.
        /// </summary>
        /// <typeparam name="T">Response content type</typeparam>
        /// <param name="req">Request to make<</param>
        /// <returns><see cref="HttpResponse"/> or <see cref="ParsingErrorHttpResponse"/> or <see cref="AbortedHttpResponse"/> or <see cref="TimeoutHttpResponse"/> or <see cref="NetworkErrorHttpResponse"/>.</returns>
        public async Task<IHttpResponse> SendHttpRequest<T>(HttpClientRequest<T> req)
        {
            OnWillSendRequestWithBackoff?.Invoke(req);

            IHttpResponse response = null;

            try
            {
                await _retryPolicy.ExecuteAsync(async (c, ct) =>
                {
                    response = null;

                    var reqest = req.RecreateWithHttpRequestMessage();

                    OnRequest?.Invoke(new RequestInfo(reqest.RequestId, reqest));

                    try
                    {
                        using (var responseMessage = await httpClient.SendAsync(reqest.RequestMessage, reqest.CancellationToken))
                        {
                            var body = await responseMessage.Content.ReadAsStringAsync();
                            var headers = responseMessage.Headers;
                            T content = default;

                            if (responseMessage?.Content?.Headers?.ContentType?.MediaType == "application/json")
                            {
                                // try parsing content with provided type
                                try
                                {
                                    content = JsonConvert.DeserializeObject<T>(body);
                                }
                                catch (Exception ex)
                                {
                                    // if unsuccessfull, return parsing error
                                    response = new ParsingErrorHttpResponse(ex.ToString(), responseMessage.Headers, reqest.RequestMessage.RequestUri);
                                }
                            }
                            else
                            {
                                // handle no valid result
                                response = new NetworkErrorHttpResponse("No valid result in http response", reqest.RequestMessage.RequestUri);
                            }

                            if (response == null)
                            {
                                response = new HttpResponse<T>(content, responseMessage.Headers, reqest.RequestMessage.RequestUri, responseMessage.StatusCode);
                            }
                        }
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

                    OnResponse?.Invoke(new ResponseInfo(reqest.RequestId, response));
                    return response;
                }, new Dictionary<string, object>() { { "httpClient", httpClient } }, req.CancellationToken, true);
            }
            catch (OperationCanceledException)
            {
                response = new AbortedHttpResponse(req.RequestMessage.RequestUri);
            }

            OnResponseReceivedWithBackoff?.Invoke(response);

            return response;
        }

        /// <summary>
        /// Make stream request using HttpClient, responses can be accessed by the <see cref="OnStreamResponse"/> callback
        /// as long as the task is running.
        /// </summary>
        /// <typeparam name="T">Response content type</typeparam>
        /// <param name="request">Request to make</param>
        /// <param name="OnStreamResponse">Callback action to retrieve responses of: <see cref="HttpResponse"/> or <see cref="ParsingErrorHttpResponse"/> or <see cref="AbortedHttpResponse"/> or <see cref="TimeoutHttpResponse"/> or <see cref="NetworkErrorHttpResponse"/></param>
        /// <returns>Request Task</returns>
        public async Task SendStreamRequest<T>(HttpClientStreamRequest<T> request, Action<IHttpResponse> OnStreamResponse)
        {
            try
            {
                using (var responseMessage = await httpClient.SendAsync(request.RequestMessage, HttpCompletionOption.ResponseHeadersRead, request.CancellationToken))
                {
                    // read a stream only when 200 status code was returned
                    if (responseMessage.IsSuccessStatusCode)
                    {
                        using (var contentStream = await responseMessage.Content.ReadAsStreamAsync())
                        {
                            using (StreamReader streamReader = new StreamReader(contentStream, encoding: System.Text.Encoding.UTF8, true))
                            {
                                // run on non UI thread
                                await Task.Run(async () =>
                                {
                                    char[] buffer = new char[4096];

                                    do
                                    {
                                        if (request.CancellationToken.IsCancellationRequested)
                                        {
                                            throw new TaskCanceledException();
                                        }

                                        int charsRead = await streamReader.ReadAsync(buffer, 0, 4096);
                                        var readString = new string(buffer).Substring(0, charsRead);

                                        Debug.Log($"Memory<char> len: {readString.Length}, bytes read: {charsRead}");

                                        // extract json string
                                        var regexPattern = @"({.*?})";
                                        MatchCollection matches = null;
                                        try
                                        {
                                            readString = Regex.Unescape(readString);
                                            matches = Regex.Matches(readString, regexPattern, RegexOptions.Multiline);
                                        }
                                        catch (Exception ex)
                                        {
                                            OnStreamResponse?.InvokeOnMainThread(new ParsingErrorHttpResponse(ex.Message, responseMessage.Headers, request.RequestMessage.RequestUri, responseMessage.StatusCode));
                                        }

                                        if (matches != null && matches.Count > 0)
                                        {
                                            for (int i = 0; i < matches.Count; i++)
                                            {
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
                                                        OnStreamResponse?.InvokeOnMainThread(new ParsingErrorHttpResponse(ex.Message, responseMessage.Headers, request.RequestMessage.RequestUri, responseMessage.StatusCode));
                                                    }

                                                    OnStreamResponse?.InvokeOnMainThread(new HttpResponse<T>(content, responseMessage.Headers, request.RequestMessage.RequestUri, responseMessage.StatusCode));
                                                }
                                                else
                                                {
                                                    // handle invalid string
                                                    OnStreamResponse?.InvokeOnMainThread(new ParsingErrorHttpResponse("JSON string is null", responseMessage.Headers, request.RequestMessage.RequestUri, responseMessage.StatusCode));
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // handle invalid string
                                            OnStreamResponse?.InvokeOnMainThread(new ParsingErrorHttpResponse($"Couldn't get valid JSON string that is matching regex pattern:'{regexPattern}'", responseMessage.Headers, request.RequestMessage.RequestUri, responseMessage.StatusCode));
                                        }
                                    }
                                    while (!streamReader.EndOfStream);
                                }, request.CancellationToken);
                            };
                        }
                    }
                    else
                    {
                        // Handle non 2xx response
                        OnStreamResponse?.InvokeOnMainThread(new HttpResponse<T>(default, responseMessage.Headers, request.RequestMessage.RequestUri, responseMessage.StatusCode));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (request.CancellationToken.IsCancellationRequested)
                {
                    // Task was aborted
                    OnStreamResponse?.InvokeOnMainThread(new AbortedHttpResponse(request.RequestMessage.RequestUri));
                }
                else
                {
                    // timeout
                    OnStreamResponse?.InvokeOnMainThread(new TimeoutHttpResponse(request.RequestMessage.RequestUri));
                }
            }
            catch (Exception e)
            {
                // other exception
                OnStreamResponse?.InvokeOnMainThread(new NetworkErrorHttpResponse(e.Message, request.RequestMessage.RequestUri));
            }
        }

        /// <summary>
        /// Make GraphQL request
        /// </summary>
        /// <typeparam name="T">Response content type</typeparam>
        /// <param name="graphQLRequest">Request to make</param>
        /// <returns><see cref="HttpResponse"/> or <see cref="ParsingErrorHttpResponse"/> or <see cref="AbortedHttpResponse"/> or <see cref="TimeoutHttpResponse"/> or <see cref="NetworkErrorHttpResponse"/>.</returns>
        public async Task<IHttpResponse> SendGraphQLRequest<T>(GraphQLClientRequest<T> graphQLRequest)
        {
            OnWillSendRequestWithBackoff?.Invoke(graphQLRequest);

            IHttpResponse response = null;

            try
            {
                await _retryPolicy.ExecuteAsync(async (c, ct) =>
                {

                    OnRequest?.Invoke(new RequestInfo(graphQLRequest.RequestId, graphQLRequest));

                    var graphQLResponse = graphQLClient.SendQueryAsync<T>(graphQLRequest, graphQLRequest.CancellationToken);

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

                        if (graphQLResponse.Result.Errors == null)
                        {
                            // valid response
                            response = new HttpResponse<T>(graphQLResponse.Result.Data, graphQLHttpResponse.ResponseHeaders, graphQLRequest.Uri, graphQLHttpResponse.StatusCode);
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

                    OnResponse?.Invoke(new ResponseInfo(graphQLRequest.RequestId, response));

                    return response;

                }, new Dictionary<string, object>() { { "httpClient", httpClient } }, graphQLRequest.CancellationToken, true);
            }
            catch (OperationCanceledException)
            {
                response = new AbortedHttpResponse(graphQLRequest.Uri);
            }

            OnResponseReceivedWithBackoff?.Invoke(response);

            return response;
        }
    }
}