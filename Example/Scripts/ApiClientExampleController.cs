using System.Net.Http.Headers;
using System.Threading;
using ApiClient.Runtime;
using UnityEngine;
using TMPro;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.GraphQLBuilder;
using System.Text;
using Newtonsoft.Json;
using UnityEngine.UI;
using System;

namespace ApiClientExample
{
    public class ApiClientExampleController : MonoBehaviour
    {
        private CancellationTokenSource _cts;
        private CancellationTokenSource _streamRequestCts;


        [SerializeField] private TextMeshProUGUI responseText;
        [SerializeField] private Image responseImage;
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

            // var request = Session.Instance.ApiClientConnecton.CreatePost<LrtResponse<AnonRegisterResponse>, ServerErrorResponse>("https://api.wearerealitygames.com:443/landlord-beta/auth/providers/anon", null, _cts.Token);
            var request = Session.Instance.ApiClientConnecton.CreatePost<LrtResponse<AnonRegisterResponse>, ServerErrorResponse>(
                "https://httpstat.us/500", 
                JsonConvert.SerializeObject(new RequestMatchmakingPostData(false)),
                _cts.Token);

            // var authentication = new AuthenticationHeaderValue("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIwMTkxYTI3OS05MjI5LTQ3ZjQtYzFlYS00NjVkZmZiODQ2ZjUiLCJyb2xlIjoidGVzdGVyIiwidG9rZW5UeXBlIjoibXdvLWFjY2VzcyIsImV4cCI6MTcyNTAxNjQ2MiwiaWF0IjoxNzI1MDEyODYyfQ.0RpRFFmX4qKMCU30YfLpfxPXU7oqc_sNqEYVOa_l0oURStvLoawJa80v7sn00LYzW6qVPegR9Nkyds4ZVFqmxO--5ABtBBefCjqkGivgbE-vgh4nZDz2EvN0q9H91I3D1Nl0uxpOsPczwXw3Q8rHT6nQADeUjPDZ5LPfDBI4J_jrWPg_-HaAHf8iSpjH1DfIiCHLhSYya_s7UiPjQ6XyfR2Zosw2OtAXfUQrGA4GeHIwlJPy6CBNMlU3ff1Hj3SRnrtxR1d7wyuEAOCAGTR2ZW8t4Qz3jY1rSq4u82AZnCNHl5UYm6M3A1ZRmGVuv_VnIgwdSR8z68nAFZAhy_3HCXcdCNO_LyGzzrBajEcBh5S_R-mzKjjy5kEAUmYcDAarbBcsHycU_a73YDNkN_jPNw2xvl_g1Vj0ZqO4rNwHNgGeZaBc5TLFh_PwS5uM3CV83H3mdqNu8tg6aUn1UPu4rMHiM2SSAnYlxPkg6_eE-q4gFOzzOFmPdl6UZWH2eEIbhRFoKhHog9J-Xm7VZY7xGkScZe_j_NJIgulgdbuQHVs9JL-IV9n74zWNm7pPpHOc0U1yDH72pWoPPvKUXKTF_QjaaIEnp4EG7oklcOlIBZHCEn52cL60tO66kKNBHHMgGvhZumTcQ8_rNwaOcRe7dMDTR0P7ox5t5gFNNHfbZQI");
            // var request = Session.Instance.ApiClientConnecton.CreatePost<Object, ServerErrorResponse>(
            //     "https://pvp.mwodev.r10s.r5y.io/matchmaking",
            //     JsonConvert.SerializeObject(new RequestMatchmakingPostData(false)),
            //     _cts.Token,
            //     authentication);

            var httpResponse = await request.Send();
            var response = new ResponseWithContent<LrtResponse<AnonRegisterResponse>, ResponseErrorCode>(httpResponse);

            if (httpResponse.HasNoErrors)
            {
                var responseContent = httpResponse as HttpResponse<LrtResponse<AnonRegisterResponse>, ServerErrorResponse>;
                Debug.Log("Success");
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

        public async void MakeImageRequest()
        {
            responseView.SetActive(true);
            responseImage.gameObject.SetActive(true);

            _streamRequestCts = new();

            var authentication = new AuthenticationHeaderValue("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiI5YTViNWViNi00NWE4LTQ3MGQtYTA1MC03MTdiODA5MzEyNjAiLCJyb2xlIjoicGxheWVyIiwidG9rZW5UeXBlIjoibWMxLWFjY2VzcyIsImV4cCI6MTY3NzY3MzkwMCwiaWF0IjoxNjc1MDgxOTAwfQ.JZuwrsazVtGgrRz8vG60ISayG2bo8zCnT7YgqW-W-RWdxNxvMfexfLrQa_cMve7L77W9yn0-i1DrVaXjd2xziMXEOvpQqfrZeu0eNnC34uu0Q6BJZsXfVjJzPFJTNEAhlyb7m7dXAXufOdit8iYj_M95XbEffGbVCaQ2r-w9RTdalH7ieYo4Df20Z3KHdbP4jke55WuoLJGYYWVmQ6cxPzQ2d5MuUIkcQN5lYNsTG0p34utbiiem6JfOhukGS1EI0xCZjCkJpWVb49u-Ru9HJcSr6SuJ9w0FNuxkKJHLPtgzez2PULNZxDDKvnn9hEASCVt2rE0kVLMNY-LN6d6n0oZXuDKknqQJ0N-vmOIfXuQ43e0D3pbeYJhqU9XKlpnWxA8w0A-fqiDQNVVJ3YntuNYp3Okkog5pHjWqygr96iTu5fQRmG8NWf5JVq61cG-6S89sHUDUW0bT9DFrWs4WprsOVG9GbsjSiwNpPu_wxggBH8ZL5oFcGVGFaa4_TErCvvH2GI-EpQjOW2jR3Na_0tCS57Uc-jirQgXOowpB7VaVj5pIiJ9SZZs7OpWK8F_6o65Mi7DVHB6sY7aOP-mvT9FihtsuQMBsqukjV8GoIBLO03WmuaojtePEpUV-zLHslTriU5_OQFQoH_LVaAewQnmcGBZuVmn-FHwuIyfR38U");
            var request = Session.Instance.ApiClientConnecton.CreateGetByteArrayRequest("https://images.pexels.com/photos/12721650/pexels-photo-12721650.jpeg?cs=srgb&dl=pexels-aykut-aktas-109304778-12721650.jpg&fm=jpg&w=3975&h=4969&_gl=1*bjx1a9*_ga*MjE2MDk4NzI4LjE3MTc0MjI4ODU.*_ga_8JE65Q40S6*MTcxNzQyNDg3MC4yLjEuMTcxNzQyNDkyNi4wLjAuMA..", _streamRequestCts.Token, authentication);

            var byteArrayResponse = await request.Send(progress =>
            {
                Debug.Log($"Downloading progress: {(progress.TotalBytesRead / (float)progress.ContentSize) * 100f}, bytesRead:{progress.TotalBytesRead}/{progress.ContentSize}");
            });

            if (byteArrayResponse.HasNoErrors &&
                byteArrayResponse.ToSpriteImage(out Sprite sprite, out string errorMessage))
            {
                responseImage.sprite = sprite;
            }
            else
            {
                if (!byteArrayResponse.IsAborted)
                {
                    Debug.Log("error");
                }
            }
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

        public class RequestMatchmakingPostData
        {
            public bool refresh;

            public RequestMatchmakingPostData(bool refresh)
            {
                this.refresh = refresh;
            }
        }
    }
}