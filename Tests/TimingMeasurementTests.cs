using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime;
using ApiClient.Runtime.HttpResponses;
using NUnit.Framework;
using Newtonsoft.Json;

namespace ApiClient.Tests
{
    /// <summary>
    /// Unit tests for timing measurement functionality
    /// </summary>
    [TestFixture]
    public class TimingMeasurementTests
    {
        private ApiClientOptions _optionsWithTimingEnabled;
        private ApiClientOptions _optionsWithTimingDisabled;

        [SetUp]
        public void Setup()
        {
            _optionsWithTimingEnabled = new ApiClientOptions
            {
                Timeout = TimeSpan.FromSeconds(30),
                VerboseLogging = false,
                BodyLogging = false,
                EnableTimeMeasurements = true
            };

            _optionsWithTimingDisabled = new ApiClientOptions
            {
                Timeout = TimeSpan.FromSeconds(30),
                VerboseLogging = false,
                BodyLogging = false,
                EnableTimeMeasurements = false
            };
        }

        [Test]
        public void TimingInfo_DefaultConstructor_InitializesWithZeroValues()
        {
            // Act
            var timingInfo = new TimingInfo();

            // Assert
            Assert.AreEqual(TimeSpan.Zero, timingInfo.ResponseTime);
            Assert.AreEqual(TimeSpan.Zero, timingInfo.DeserializationTime);
            Assert.AreEqual(TimeSpan.Zero, timingInfo.TotalTime);
        }

        [Test]
        public void TimingInfo_ConstructorWithValues_InitializesCorrectly()
        {
            // Arrange
            var responseTime = TimeSpan.FromMilliseconds(100);
            var deserializationTime = TimeSpan.FromMilliseconds(50);

            // Act
            var timingInfo = new TimingInfo(responseTime, deserializationTime);

            // Assert
            Assert.AreEqual(responseTime, timingInfo.ResponseTime);
            Assert.AreEqual(deserializationTime, timingInfo.DeserializationTime);
            Assert.AreEqual(TimeSpan.FromMilliseconds(150), timingInfo.TotalTime);
        }

        [Test]
        public void TimingInfo_TotalTime_CalculatesCorrectly()
        {
            // Arrange
            var timingInfo = new TimingInfo
            {
                ResponseTime = TimeSpan.FromMilliseconds(200),
                DeserializationTime = TimeSpan.FromMilliseconds(75)
            };

            // Act
            var totalTime = timingInfo.TotalTime;

            // Assert
            Assert.AreEqual(TimeSpan.FromMilliseconds(275), totalTime);
        }

        [Test]
        public async Task DeserializeJson_WithTimingEnabled_MeasuresDeserializationTime()
        {
            // Arrange
            var apiClient = new ApiClientTestable(_optionsWithTimingEnabled);
            var testObject = new TestModel { Id = 123, Name = "Test" };
            var json = JsonConvert.SerializeObject(testObject);
            using var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json));
            var headers = GetContentHeaders();

            // Act
            var result = apiClient.TestDeserializeJson<TestModel>(stream, headers, "Test Label", out var bytesRead, out var deserializationTime);

            // Assert
            Assert.IsNotNull(result);
            Assert.Greater(deserializationTime, TimeSpan.Zero, "Deserialization time should be greater than zero when timing is enabled");
        }

        [Test]
        public async Task DeserializeJson_WithTimingDisabled_ReturnsZeroDeserializationTime()
        {
            // Arrange
            var apiClient = new ApiClientTestable(_optionsWithTimingDisabled);
            var testObject = new TestModel { Id = 123, Name = "Test" };
            var json = JsonConvert.SerializeObject(testObject);
            using var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(json));
            var headers = GetContentHeaders();

            // Act
            var result = apiClient.TestDeserializeJson<TestModel>(stream, headers, "Test Label", out var bytesRead, out var deserializationTime);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(TimeSpan.Zero, deserializationTime, "Deserialization time should be zero when timing is disabled");
        }

        [Test]
        public async Task ProcessJsonResponse_WithTimingEnabled_ReturnsDeserializationTime()
        {
            // Arrange
            var apiClient = new ApiClientTestable(_optionsWithTimingEnabled);
            var testContent = new TestModel { Id = 789, Name = "Content Test" };
            var json = JsonConvert.SerializeObject(testContent);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            using var responseMessage = CreateResponseMessage(json, HttpStatusCode.OK);

            // Act
            var result = await apiClient.TestProcessJsonResponse<TestModel, ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNotNull(result.content);
            Assert.AreEqual(789, result.content.Id);
            Assert.Greater(result.deserializationTime, TimeSpan.Zero, "Deserialization time should be measured");
        }

        [Test]
        public async Task ProcessJsonResponse_WithTimingDisabled_ReturnsZeroDeserializationTime()
        {
            // Arrange
            var apiClient = new ApiClientTestable(_optionsWithTimingDisabled);
            var testContent = new TestModel { Id = 789, Name = "Content Test" };
            var json = JsonConvert.SerializeObject(testContent);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            using var responseMessage = CreateResponseMessage(json, HttpStatusCode.OK);

            // Act
            var result = await apiClient.TestProcessJsonResponse<TestModel, ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNotNull(result.content);
            Assert.AreEqual(789, result.content.Id);
            Assert.AreEqual(TimeSpan.Zero, result.deserializationTime, "Deserialization time should be zero when timing is disabled");
        }

        [Test]
        public void HttpResponse_ImplementsIHttpResponseTiming()
        {
            // Arrange & Act
            var response = new HttpResponse<TestModel>(
                new TestModel { Id = 1, Name = "Test" },
                new HttpResponseMessage().Headers,
                new StringContent("").Headers,
                "{}",
                new HttpRequestMessage().RequestUri,
                HttpStatusCode.OK);

            // Assert
            Assert.IsInstanceOf<IHttpResponseTiming>(response);
            Assert.IsNotNull(response.TimingInfo);
        }

        [Test]
        public void AbortedHttpResponse_ImplementsIHttpResponseTiming()
        {
            // Arrange & Act
            var response = new AbortedHttpResponse(new Uri("http://test.com"));

            // Assert
            Assert.IsInstanceOf<IHttpResponseTiming>(response);
            Assert.IsNotNull(response.TimingInfo);
        }

        [Test]
        public void NetworkErrorHttpResponse_ImplementsIHttpResponseTiming()
        {
            // Arrange & Act
            var response = new NetworkErrorHttpResponse("Error", new Uri("http://test.com"));

            // Assert
            Assert.IsInstanceOf<IHttpResponseTiming>(response);
            Assert.IsNotNull(response.TimingInfo);
        }

        [Test]
        public void TimeoutHttpResponse_ImplementsIHttpResponseTiming()
        {
            // Arrange & Act
            var response = new TimeoutHttpResponse(new Uri("http://test.com"));

            // Assert
            Assert.IsInstanceOf<IHttpResponseTiming>(response);
            Assert.IsNotNull(response.TimingInfo);
        }

        [Test]
        public void ParsingErrorHttpResponse_ImplementsIHttpResponseTiming()
        {
            // Arrange & Act
            var response = new ParsingErrorHttpResponse(
                "Parsing error",
                new HttpResponseMessage().Headers,
                new Uri("http://test.com"));

            // Assert
            Assert.IsInstanceOf<IHttpResponseTiming>(response);
            Assert.IsNotNull(response.TimingInfo);
        }

        #region Helper Methods

        private HttpResponseMessage CreateResponseMessage(string content, HttpStatusCode statusCode, string contentType = "application/json")
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, contentType)
            };
            return response;
        }

        private System.Net.Http.Headers.HttpContentHeaders GetContentHeaders(string content = "", bool addGzip = false)
        {
            var httpContent = new StringContent(content, Encoding.UTF8, "application/json");
            if (addGzip)
            {
                httpContent.Headers.ContentEncoding.Add("gzip");
            }

            return httpContent.Headers;
        }

        #endregion

        #region Test Models

        private class TestModel
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        private class ErrorModel
        {
            public string Code { get; set; }
            public string Message { get; set; }
        }

        #endregion
    }
}
