using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime;
using ApiClient.Runtime.HttpResponses;
using UnityEngine;

namespace ApiClientExample
{
    public class StreamController : MonoBehaviour
    {
        public class StreamProcessor
        {

        }

        private const string LOG_TAG = nameof(StreamController);

        private CancellationTokenSource _streamRequestCts;

        public async void StartStream()
        {
            Debug.Log($"{LOG_TAG}: Starting Stream");

            do
            {
                if (_streamRequestCts != null && !_streamRequestCts.IsCancellationRequested)
                {
                    throw new System.Exception("Trying to start a new stream while another is running. Cancel it first.");
                }

                _streamRequestCts = new();

                await MakeStreamRequest("john", 3, _streamRequestCts.Token);

                Debug.Log($"{LOG_TAG}: Stream ended!");

                if (!_streamRequestCts.IsCancellationRequested)
                {
                    Debug.Log($"{LOG_TAG}: Retrying...");
                }
            }
            while (!_streamRequestCts.IsCancellationRequested);
        }

        public void CancelStream()
        {
            Debug.Log($"{LOG_TAG}: Cancelling Stream");
            _streamRequestCts?.Cancel();
        }

        private static async Task MakeStreamRequest(string playerName, long offset, CancellationToken ct)
        {
            var authentication = new AuthenticationHeaderValue("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIwMTkwNzM3Ni1mNzk2LWYwODItNjBhOC0zMjQwMDlkOGVjNDgiLCJyb2xlIjoicGxheWVyIiwidG9rZW5UeXBlIjoibXdvLWFjY2VzcyIsImV4cCI6MTcyMDUyMDQxNiwiaWF0IjoxNzIwNTE2ODE2fQ.HBHp3Y9YE7Ei4gXh0K7JwHjK52OVHo64jjj8oIQxWKrPBL8VhI3aeaiEvoW0NXTfczcxP-1xAid0jVdJUddHo2ur1IrtuCTh8FB9LkARkQTJub1FYlMzotpYTq_2zxlwy-VrgKNPZYROZhbldr0s48lWDywzdnRotjejl0MOZMmkpPakaqq1Yri_bYrMS5lbZMkmNSQzflrA-bV5dgDIzDsa1TsoYX5iPicNN53swwhRA9H78ZaEalgULd5WEzLNL4v3Yv6K9P7PywCOcftaAnbD5UU_RX5i8lMMYtDa0nlq0AsMExucTtHihVpjh4jL0UYYBq3AFKBdSHxAl78MQOXsCE5jEyynVOHEk09U8A7siRDwLxB-RDLF1kqdqc8XJj6FSWWEfEWKZW7vTD3QO88wIaTYCuwiDsQjUdT308lTjAe9tBsGJeXipz3qeugqidSnOmvIOY8JSSUMtcJDl8bhYHWBOS6ETctkv801-cfd_ePx4_oCbHFMKtu-i6A5ig7YOleA_QTJLFPzgVt6sv7gwGGB7pnAEHI-gW-YTnIrnsZEaisRC3N92BuwTc8_ZX_stSPZf3Un-TYJVL5hpFBHyb4QL64lcBuMMHYC8JtVdWndlxiUQUim8M7FbYN2Chb6Q54QoWS03aNFZ7_R3V5xaOBEt_sYL-vRRVTNeic");
            var request = Session.Instance.ApiClientConnecton.CreateGetStreamRequest<StreamData>($"https://sse-player-messages.mwostg.r10s.r5y.io/stream?offset={offset}", ct, authentication);

            await request.Send(streamResponse =>
            {
                var response = new ResponseWithContent<StreamData, StreamResponseErrorCode>(streamResponse);

                if (streamResponse.HasNoErrors)
                {
                    var responseContent = streamResponse as HttpResponse<StreamData>;
                    Debug.Log(responseContent.Content);
                }
                else
                {
                    if (!streamResponse.IsAborted)
                    {
                        Debug.Log("error");

                        if (streamResponse is NetworkErrorHttpResponse ner)
                        {
                            Debug.Log(ner.Message);
                            response.SetError(StreamResponseErrorCode.Unknown, "User friendly Network Error message");
                        }
                        else if (streamResponse is TimeoutHttpResponse t)
                        {
                            Debug.Log("Timeout");
                            response.SetError(StreamResponseErrorCode.Unknown, "User friendly Timeout Error message");
                        }
                        else if (streamResponse is ParsingErrorHttpResponse per)
                        {
                            Debug.Log(per.Message);
                            response.SetError(StreamResponseErrorCode.Unknown, per.Message);
                        }
                    }
                }
            },
            (readDelta) =>
            {
                Debug.Log($"Stream read delta:{readDelta}");
            });

        }

        /*
            App Lifecycle

            1. Stream should start automaticly at the begining of the game.
            2. It should stop when app is in background.
            3. And start again when app is back in the foreground.
        */

        private void OnApplicationPause(bool pauseStatus)
        {
            if (_streamRequestCts == null)
            {
                return;
            }

            if (pauseStatus)
            {
                CancelStream();
            }
            else
            {
                StartStream();
            }
        }

        private void OnDisable()
        {
            CancelStream();
        }


        // Response

        public enum StreamResponseErrorCode
        {
            Unknown
        }

        public class StreamData
        {
            public string type;

            public StreamDataType Type
            {
                get
                {
                    return type switch
                    {
                        "ping" => StreamDataType.Ping,
                        "level-up" => StreamDataType.LevelUp,
                        _ => StreamDataType.NotSupported
                    };
                }
            }

            public override string ToString()
            {
                return $"type:{type}";
            }

            public enum StreamDataType
            {
                NotSupported = 0,
                Ping = 1,
                LevelUp = 2,
            }
        }
    }
}