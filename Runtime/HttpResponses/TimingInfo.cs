using System;

namespace ApiClient.Runtime.HttpResponses
{
    /// <summary>
    /// Contains timing information for HTTP requests.
    /// </summary>
    public class TimingInfo
    {
        /// <summary>
        /// Time elapsed to receive the HTTP response (network time).
        /// </summary>
        public TimeSpan ResponseTime { get; set; }

        /// <summary>
        /// Time elapsed to deserialize the response body.
        /// </summary>
        public TimeSpan DeserializationTime { get; set; }

        /// <summary>
        /// Total time elapsed (ResponseTime + DeserializationTime).
        /// </summary>
        public TimeSpan TotalTime => ResponseTime + DeserializationTime;

        public TimingInfo()
        {
            ResponseTime = TimeSpan.Zero;
            DeserializationTime = TimeSpan.Zero;
        }

        public TimingInfo(TimeSpan responseTime, TimeSpan deserializationTime)
        {
            ResponseTime = responseTime;
            DeserializationTime = deserializationTime;
        }
    }
}
