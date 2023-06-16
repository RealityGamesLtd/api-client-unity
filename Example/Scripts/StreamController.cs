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
            var authentication = new AuthenticationHeaderValue("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiI5YTViNWViNi00NWE4LTQ3MGQtYTA1MC03MTdiODA5MzEyNjAiLCJyb2xlIjoicGxheWVyIiwidG9rZW5UeXBlIjoibWMxLWFjY2VzcyIsImV4cCI6MTY3NzY3MzkwMCwiaWF0IjoxNjc1MDgxOTAwfQ.JZuwrsazVtGgrRz8vG60ISayG2bo8zCnT7YgqW-W-RWdxNxvMfexfLrQa_cMve7L77W9yn0-i1DrVaXjd2xziMXEOvpQqfrZeu0eNnC34uu0Q6BJZsXfVjJzPFJTNEAhlyb7m7dXAXufOdit8iYj_M95XbEffGbVCaQ2r-w9RTdalH7ieYo4Df20Z3KHdbP4jke55WuoLJGYYWVmQ6cxPzQ2d5MuUIkcQN5lYNsTG0p34utbiiem6JfOhukGS1EI0xCZjCkJpWVb49u-Ru9HJcSr6SuJ9w0FNuxkKJHLPtgzez2PULNZxDDKvnn9hEASCVt2rE0kVLMNY-LN6d6n0oZXuDKknqQJ0N-vmOIfXuQ43e0D3pbeYJhqU9XKlpnWxA8w0A-fqiDQNVVJ3YntuNYp3Okkog5pHjWqygr96iTu5fQRmG8NWf5JVq61cG-6S89sHUDUW0bT9DFrWs4WprsOVG9GbsjSiwNpPu_wxggBH8ZL5oFcGVGFaa4_TErCvvH2GI-EpQjOW2jR3Na_0tCS57Uc-jirQgXOowpB7VaVj5pIiJ9SZZs7OpWK8F_6o65Mi7DVHB6sY7aOP-mvT9FihtsuQMBsqukjV8GoIBLO03WmuaojtePEpUV-zLHslTriU5_OQFQoH_LVaAewQnmcGBZuVmn-FHwuIyfR38U");
            var request = Session.Instance.ApiClientConnecton.CreateGetStreamRequest<StreamData>($"http://sse-poc.monopoly-concept1.r10s.r5y.io/sse/stream/{playerName}?offset={offset}", ct, authentication);

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