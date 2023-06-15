using System.Net.Http.Headers;
using System.Threading;
using ApiClient.Runtime;
using UnityEngine;
using TMPro;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.GraphQLBuilder;
using System;
using Polly;
using System.Net.Http;
using System.Net;
using System.Linq;
using System.Text;

public class ApiClientExampleController : MonoBehaviour
{
    private static HttpStatusCode[] _httpStatusCodesWorthRetrying = {
                HttpStatusCode.RequestTimeout, // 408
                HttpStatusCode.InternalServerError, // 500
                HttpStatusCode.BadGateway, // 502
                HttpStatusCode.GatewayTimeout // 504
            };

    private readonly IApiClientConnection _apiClientConnecton = new ApiClientConnection(
        new ApiClientOptions()
        {
            GraphQLClientEndpoint = "https://spacex-production.up.railway.app/",
            Timeout = TimeSpan.FromSeconds(10),
            RetryPolicy = Policy
                .Handle<HttpRequestException>()
                .OrResult<IHttpResponse>(r =>
                {
                    var validStatusCode = false;
                    if (r is IHttpResponseStatusCode responseWithStatusCode)
                    {
                        validStatusCode = _httpStatusCodesWorthRetrying.Contains(responseWithStatusCode.StatusCode);
                    }
                    return r.IsTimeout ||
                        r.IsNetworkError ||
                        validStatusCode;
                })
                // Exponential Backoff
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (response, timeSpan) =>
                    {
                        // Logic to be executed before each retry
                    }),
        });

    private CancellationTokenSource _cts;
    private CancellationTokenSource _streamRequestCts;


    [SerializeField] private TextMeshProUGUI responseText;
    [SerializeField] private GameObject responseView;

    private void OnEnable()
    {
        _cts = new();
    }

    private void OnDisable()
    {
        _cts.Cancel();
        _streamRequestCts?.Cancel();
    }

    public void CloseResponseView()
    {
        if (_streamRequestCts != null && !_streamRequestCts.IsCancellationRequested)
        {
            CancelStream();
        }

        responseText.text = "";

        responseView.SetActive(false);
    }

    public async void MakeHttpRequestWithoutContent()
    {

    }

    public async void MakeHttpRequest()
    {
        responseView.SetActive(true);
        responseText.text = "";

        var request = _apiClientConnecton.CreatePost<Response<AnonRegisterResponse>>("https://api.wearerealitygames.com:443/landlord-beta/auth/providers/anon", null, _cts.Token);
        var response = await request.Send();

        if (response.HasNoErrors)
        {
            var responseContent = response as HttpResponse<Response<AnonRegisterResponse>>;
            Debug.Log(responseContent.Content.response.playerId);
            responseText.text = responseContent.Content.response.playerId;
        }
        else
        {
            if (!response.IsAborted)
            {
                Debug.Log("error");
            }

            if (response is NetworkErrorHttpResponse ner)
            {
                Debug.Log(ner.Message);
            }
            else if (response is TimeoutHttpResponse t)
            {
                Debug.Log("Timeout");
            }
            else if (response is ParsingErrorHttpResponse per)
            {
                Debug.Log(per.Message);
            }
        }
    }

    public async void MakeGraphQlRequest()
    {
        responseView.SetActive(true);
        responseText.text = "";

        var query = new Query()
            .Name("__type")
            .Select("name")
            .Where("name", "users");

        // var query = new Query()
        //     .Name("__type")
        //     .Select("name")
        //     .Where("name", new AnonRegisterResponse(){accessToken = "a", playerId = "b", refreshToken = "c"});

        Debug.Log(query.ToString());

        var request = _apiClientConnecton.CreateGraphQLRequest<ResponseType>(query, _cts.Token);

        var response = await request.Send();

        if (response.HasNoErrors)
        {
            var responseContent = response as HttpResponse<ResponseType>;
            Debug.Log(responseContent.Content.__type.Name);
            responseText.text = responseContent.Content.__type.Name;
        }
        else
        {
            if (!response.IsAborted)
            {
                Debug.Log("error");
            }

            if (response is NetworkErrorHttpResponse ner)
            {
                Debug.Log(ner.Message);
            }
            else if (response is TimeoutHttpResponse t)
            {
                Debug.Log("Timeout");
            }
            else if (response is ParsingErrorHttpResponse per)
            {
                Debug.Log(per.Message);
            }
        }
    }

    public async void MakeStreamRequest()
    {
        responseView.SetActive(true);
        responseText.text = "";

        _streamRequestCts = new();

        var authentication = new AuthenticationHeaderValue("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiI5YTViNWViNi00NWE4LTQ3MGQtYTA1MC03MTdiODA5MzEyNjAiLCJyb2xlIjoicGxheWVyIiwidG9rZW5UeXBlIjoibWMxLWFjY2VzcyIsImV4cCI6MTY3NzY3MzkwMCwiaWF0IjoxNjc1MDgxOTAwfQ.JZuwrsazVtGgrRz8vG60ISayG2bo8zCnT7YgqW-W-RWdxNxvMfexfLrQa_cMve7L77W9yn0-i1DrVaXjd2xziMXEOvpQqfrZeu0eNnC34uu0Q6BJZsXfVjJzPFJTNEAhlyb7m7dXAXufOdit8iYj_M95XbEffGbVCaQ2r-w9RTdalH7ieYo4Df20Z3KHdbP4jke55WuoLJGYYWVmQ6cxPzQ2d5MuUIkcQN5lYNsTG0p34utbiiem6JfOhukGS1EI0xCZjCkJpWVb49u-Ru9HJcSr6SuJ9w0FNuxkKJHLPtgzez2PULNZxDDKvnn9hEASCVt2rE0kVLMNY-LN6d6n0oZXuDKknqQJ0N-vmOIfXuQ43e0D3pbeYJhqU9XKlpnWxA8w0A-fqiDQNVVJ3YntuNYp3Okkog5pHjWqygr96iTu5fQRmG8NWf5JVq61cG-6S89sHUDUW0bT9DFrWs4WprsOVG9GbsjSiwNpPu_wxggBH8ZL5oFcGVGFaa4_TErCvvH2GI-EpQjOW2jR3Na_0tCS57Uc-jirQgXOowpB7VaVj5pIiJ9SZZs7OpWK8F_6o65Mi7DVHB6sY7aOP-mvT9FihtsuQMBsqukjV8GoIBLO03WmuaojtePEpUV-zLHslTriU5_OQFQoH_LVaAewQnmcGBZuVmn-FHwuIyfR38U");
        var request = _apiClientConnecton.CreateGetStreamRequest<StreamData>("http://venue-explorer.monopoly-concept1.r10s.r5y.io/public-stream", _streamRequestCts.Token, authentication);

        await request.Send(streamResponse =>
        {
            if (streamResponse.HasNoErrors)
            {
                var responseContent = streamResponse as HttpResponse<StreamData>;
                Debug.Log(responseContent.Content);
                responseText.text += $"{responseContent.Content}\n";
            }
            else
            {
                if (!streamResponse.IsAborted)
                {
                    Debug.Log("error");
                }
            }
        });
    }

    public void CancelStream()
    {
        _streamRequestCts?.Cancel();
        responseText.text = "";
    }


    public class StreamData
    {
        public long nanos;
        public string msg;

        public override string ToString()
        {
            return $"nanos:{nanos}, msg:{msg}";
        }
    }

    public class LaunchResponse
    {
        public string Name { get; set; }
    }

    public class ResponseType
    {
        public LaunchResponse __type { get; set; }
    }

    public class Response
    {
        public Meta meta;
    }

    [System.Serializable]
    public class Response<T>
    {
        public T response;
        public Meta meta;
    }

    public class Meta
    {
        public int code;
        public string message, landlordErrorCode;
    }

    public class AnonRegisterResponse : IQueryObject
    {
        public string playerId;
        public string accessToken;
        public string refreshToken;

        public string SerializeToQuery()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.Append("{");
            stringBuilder.Append($"{nameof(playerId)}: {QueryStringBuilder.BuildQueryParam(playerId)},");
            stringBuilder.Append($"{nameof(accessToken)}: {QueryStringBuilder.BuildQueryParam(accessToken)},");
            stringBuilder.Append($"{nameof(refreshToken)}: {QueryStringBuilder.BuildQueryParam(refreshToken)}");
            stringBuilder.Append("}");
            return stringBuilder.ToString();
        }
    }

    public class UserAuthenticationData
    {
        public UserAuthenticationData(
            string playerId,
            string accessToken,
            string refreshToken)
        {
            PlayerId = playerId;
            AccessToken = accessToken;
            RefreshToken = refreshToken;
        }

        public string PlayerId { get; }
        public string AccessToken { get; }
        public string RefreshToken { get; }
    }
}
