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
            var authentication = new AuthenticationHeaderValue("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIwMTkwOTc2Ni02NzQ4LTYyMzItOTY0ZC1lN2NhMWZiM2VlYjgiLCJyb2xlIjoicGxheWVyIiwidG9rZW5UeXBlIjoibXdvLWFjY2VzcyIsImV4cCI6MTcyMDUzMDU5MCwiaWF0IjoxNzIwNTI2OTkwfQ.M49L-VNpL0TT9BBftr4EjDIAyNABquoVqeS2ddeq8OdLGfhfCuSV5l4xv8qeUrweDMQAF3emKdfjQWYVvzWIvvMmohBxyruk5PXKeoUu_HzUxM5oWGSRtaE9KVL_buaVtfxEZ9huMV8-jTOF0fREvRIf50gaXFc5ayRhAh2id5oSxBWNiZpjEmPfoG-n96R3oRZYRdXplwxCL26EaV9Yp9EsRa-6kzn0ob0eSN2fWjZbA-xBTg-Fq38DbpfBP62a0Ozu6zFhz-Jn9AK8uNp0AHPweZpxoCNeqDrp2DaR5s5Uvnbl7YefPcAK1s04xYiAFSubjd87sEzt9k3IM8qF9oQUA5u-mIpek2qQP9J9b9K0kEUysS79wSY7Ubrqi7JjCjhxj43EGYIBPZHf9E6_SYcOE7KhMgQcmiQyqhIIEdPBVyiBocdWda2cIfxdxsDZmjUCXGQqQHmvPJn0k3sXTrJdOnw7aVndmIAd8vsV2yevwbLPThR-RRIr99Jp-ef86P8X02Dn_eQnZW2pcUCClNyY5ZazYr2LbvCv21w03oBkkR54Docfarz6olNAVC-t8wxmofWT_56WMG0nPezkSkxsQucmJwmy9D_Xit19LSDepn83VcXVa8juQowplww2QptSjyB8LMZbPT5t4N-XbRcBIXXVK6PIVxk-mBWVE5g");
            var request = Session.Instance.ApiClientConnecton.CreateGetStreamRequest<StreamData>($"https://sse-player-messages.mwodev.r10s.r5y.io/stream?offset={offset}", ct, authentication);

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