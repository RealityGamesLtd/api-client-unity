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

            var authentication = new AuthenticationHeaderValue("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIwMTkwNzM3Ni1mNzk2LWYwODItNjBhOC0zMjQwMDlkOGVjNDgiLCJyb2xlIjoicGxheWVyIiwidG9rZW5UeXBlIjoibXdvLWFjY2VzcyIsImV4cCI6MTcyMDUyNTkyMiwiaWF0IjoxNzIwNTIyMzIyfQ.jbXPv-aa54tc1WBVorTsbOVIOhI1zd6pKwUh23Rh7xFpD82gr56U7SEKUNLTwfjX6k4djSL6nBX7vnNbtEpoBSIsHyX5DdKvo9OoCxgV8_9UTQ0V8iWvY4Ym5e1nTZG5e1TvxwhDuzFGP55syUK5ncLTBO-4naQBDTMOtnDM7HaZJYmjJod-sm_by3wZTSuWYDrr1-YfHcnOU-j3jN2FJRc4WHgwNBW9neiZVRH10ocv1LN0zLpEu3qyrNGxp2WmxS9SFRtsbtzBf72lCv8eGN_InV9flvpKC4polCI0WdUS_jpx6q01Sao9HTvnCQrAJTO9vrowkPv4DZFwYbdzmkT2a4pPk2rVsbMDEVGhZmohDZcXs5eMqTEAbCPY2taDe_SBh-DigOgv5nOIydCQTskxtInCaI6Ijtt8o_x1yJhrq0JQJfJY8alWs8Pch5FMEOPgJOnfJzczsNxxA4HhCcuaLAzSeBEbE6mAfSKduq_OrJVATrv17eNpOG31SdCrgmXfn0a3pnscZeUKkOzM_on8QBVSR2zWIFUmIh-l7byyc7axr0092oSYUeDt1H16evKMVp9bEeqRCLk_0vW8ZUOsvkz4w7TP8Po2ra9WXJQVmX_Gy2nr5x9c_DMNmcICR2Eq1WlJQGPP-XFgnDetYolJxZfYLidv-w9szAxlFYc");
            var request = Session.Instance.ApiClientConnecton.CreateGetStreamRequest<StreamData>("https://sse-player-messages.mwostg.r10s.r5y.io/stream", _streamRequestCts.Token, authentication);

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