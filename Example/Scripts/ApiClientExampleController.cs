using System.Net.Http.Headers;
using System.Threading;
using ApiClient.Runtime;
using UnityEngine;
using TMPro;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.GraphQLBuilder;
using System.Text;
using Newtonsoft.Json;

namespace ApiClientExample
{
    public class ApiClientExampleController : MonoBehaviour
    {
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

            var request = Session.Instance.ApiClientConnecton.CreatePost<LrtResponse<AnonRegisterResponse>, ServerErrorResponse>("https://api.wearerealitygames.com:443/landlord-beta/auth/providers/anon", null, _cts.Token);
            var httpResponse = await request.Send();
            var response = new ResponseWithContent<LrtResponse<AnonRegisterResponse>, ResponseErrorCode>(httpResponse);

            if (httpResponse.HasNoErrors)
            {
                var responseContent = httpResponse as HttpResponse<LrtResponse<AnonRegisterResponse>, ServerErrorResponse>;
                Debug.Log(responseContent.Content.response.playerId);
                responseText.text = responseContent.Content.response.playerId;
            }
            else
            {
                if (!httpResponse.IsAborted)
                {
                    Debug.Log("error");

                    if (httpResponse is NetworkErrorHttpResponse ner)
                    {
                        Debug.Log(ner.Message);
                        response.SetError(ResponseErrorCode.Unknown, "User friendly Network Error message");
                    }
                    else if (httpResponse is TimeoutHttpResponse t)
                    {
                        Debug.Log("Timeout");
                        response.SetError(ResponseErrorCode.Unknown, "User friendly Timeout Error message");
                    }
                    else if (httpResponse is ParsingErrorHttpResponse per)
                    {
                        Debug.Log(per.Message);
                        response.SetError(ResponseErrorCode.Unknown, per.Message);
                    }
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

            var request = Session.Instance.ApiClientConnecton.CreateGraphQLRequest<ResponseType>(query, _cts.Token);

            var httpResponse = await request.Send();
            var response = new ResponseWithContent<ResponseType, ResponseErrorCode>(httpResponse);

            if (httpResponse.HasNoErrors)
            {
                var responseContent = httpResponse as HttpResponse<ResponseType>;
                Debug.Log(responseContent.Content.__type.Name);
                responseText.text = responseContent.Content.__type.Name;
            }
            else
            {
                if (!httpResponse.IsAborted)
                {
                    Debug.Log("error");

                    if (httpResponse is NetworkErrorHttpResponse ner)
                    {
                        Debug.Log(ner.Message);
                        response.SetError(ResponseErrorCode.Unknown, "User friendly Network Error message");
                    }
                    else if (httpResponse is TimeoutHttpResponse t)
                    {
                        Debug.Log("Timeout");
                        response.SetError(ResponseErrorCode.Unknown, "User friendly Timeout Error message");
                    }
                    else if (httpResponse is ParsingErrorHttpResponse per)
                    {
                        Debug.Log(per.Message);
                        response.SetError(ResponseErrorCode.Unknown, "User friendly Content Parsing Error message");
                    }
                }
            }
        }

        public async void MakeStreamRequest()
        {
            responseView.SetActive(true);
            responseText.text = "";

            _streamRequestCts = new();

            var authentication = new AuthenticationHeaderValue("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIwMTkwOTc2Ni02NzQ4LTYyMzItOTY0ZC1lN2NhMWZiM2VlYjgiLCJyb2xlIjoicGxheWVyIiwidG9rZW5UeXBlIjoibXdvLWFjY2VzcyIsImV4cCI6MTcyMDUzMDU5MCwiaWF0IjoxNzIwNTI2OTkwfQ.M49L-VNpL0TT9BBftr4EjDIAyNABquoVqeS2ddeq8OdLGfhfCuSV5l4xv8qeUrweDMQAF3emKdfjQWYVvzWIvvMmohBxyruk5PXKeoUu_HzUxM5oWGSRtaE9KVL_buaVtfxEZ9huMV8-jTOF0fREvRIf50gaXFc5ayRhAh2id5oSxBWNiZpjEmPfoG-n96R3oRZYRdXplwxCL26EaV9Yp9EsRa-6kzn0ob0eSN2fWjZbA-xBTg-Fq38DbpfBP62a0Ozu6zFhz-Jn9AK8uNp0AHPweZpxoCNeqDrp2DaR5s5Uvnbl7YefPcAK1s04xYiAFSubjd87sEzt9k3IM8qF9oQUA5u-mIpek2qQP9J9b9K0kEUysS79wSY7Ubrqi7JjCjhxj43EGYIBPZHf9E6_SYcOE7KhMgQcmiQyqhIIEdPBVyiBocdWda2cIfxdxsDZmjUCXGQqQHmvPJn0k3sXTrJdOnw7aVndmIAd8vsV2yevwbLPThR-RRIr99Jp-ef86P8X02Dn_eQnZW2pcUCClNyY5ZazYr2LbvCv21w03oBkkR54Docfarz6olNAVC-t8wxmofWT_56WMG0nPezkSkxsQucmJwmy9D_Xit19LSDepn83VcXVa8juQowplww2QptSjyB8LMZbPT5t4N-XbRcBIXXVK6PIVxk-mBWVE5g");
            var request = Session.Instance.ApiClientConnecton.CreateGetStreamRequest<StreamData>("https://sse-player-messages.mwodev.r10s.r5y.io/stream", _streamRequestCts.Token, authentication);

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
            },
            (readDelta) =>
            {
                Debug.Log($"Stream read delta:{readDelta}");
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

        public class LrtResponse
        {
            public LrtMeta meta;
        }

        [System.Serializable]
        public class LrtResponse<T>
        {
            public T response;
            public LrtMeta meta;
        }

        public class LrtMeta
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

        public enum ResponseErrorCode
        {
            Unknown
        }

        public class ServerErrorResponse
        {
            [JsonProperty(Required = Required.Always)]
            public string subcode;
            [JsonProperty(Required = Required.Always)]
            public string reason;
            [JsonProperty(Required = Required.AllowNull)]
            public string args;
        }
    }
}