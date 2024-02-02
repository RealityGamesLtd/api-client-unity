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
            var authentication = new AuthenticationHeaderValue("Bearer", "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9.eyJzdWIiOiI3ODkxMjk0Zi1lMTM4LTRiN2EtYjQ4Zi05NmE1ZDA0ODg3YmYiLCJyb2xlIjoicGxheWVyIiwidG9rZW5UeXBlIjoibXdvLWFjY2VzcyIsImV4cCI6MTY5OTA4MjE3MSwiaWF0IjoxNjk2NDkwMTcxfQ.DWJ7FzTUvsp2TPjfUxt5Dpg7yFHtAK61Zh2Z6i2LlzyofZa023qSla7G10PTCBd24s4seCFH1MxXnuY798rFbGGvLiGwLMnePeJXBEOmTYsJpCrxcp2vNxbIPvpX4xa5qPekPlwiNJb1sREF-PLdclO7fSxlIYIDnjaL8Iui9ujJWw8Rg5gL4Wv9nNcDnZv7FUF7qLwn4p5HfTAvNPB17rgoR8_6xt5pVP1G8rfm_G8cP_G7_4HJ_nnLtlT8sv1hnNSXG4S93i0clxzIF0emgwe_W_Jux_XItW6Qhx-XMs9jdJUjzAfx9V3WKEQDXtnxdIH5tr-QhyYyR_9NTMOiUpacpPdvaN8aWoS6iOsdJc9Y7sKzTwK920o4e0DJEVAie07JSzMgRitLmY4Dy01L7XWwIsJ5-yvSzXKimcC8bd-vakoQR3qFUz0vNLiN8OuVk8_gHbCFCD2sl-IvBwrRbJGzB27CAmTke5wjjtZYsBPSnD9RcZG6nkeAuuVN82cB_7aGFtP_Scyk0d4UUdwIC24p4HQ-MN8aZL-03Vr3_am3SfdpJtLf-M8hME8UDPAF72pwV1-iV2ikmnRzTc_qciAvF14DpJem7sU8o-JefZla8_s4TAMqCVeF2QITXC6xDFUuU7YIIOHj5s15JqK8Bz6qxwuzh0KTm1IiwmGZtZw");
            var request = Session.Instance.ApiClientConnecton.CreateGetStreamRequest<StreamData>($"http://sse-player-messages.mwo.r10s.r5y.io/stream?offset={offset}", ct, authentication);

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