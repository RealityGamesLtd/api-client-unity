using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime;
using ApiClient.Runtime.HttpResponses;
using NUnit.Framework;
using Newtonsoft.Json;

namespace Tests
{
    /// <summary>
    /// Unit tests for ApiClient helper methods
    /// </summary>
    [TestFixture]
    public class ApiClientHelperMethodsTests
    {
        private ApiClientTestable _apiClient;
        private ApiClientOptions _options;

        [SetUp]
        public void Setup()
        {
            _options = new ApiClientOptions
            {
                Timeout = TimeSpan.FromSeconds(30),
                VerboseLogging = false,
                BodyLogging = true
            };
            _apiClient = new ApiClientTestable(_options);
        }

        [TearDown]
        public void TearDown()
        {
            _apiClient = null;
        }

        #region PrepareJsonStream Tests

        [Test]
        public void PrepareJsonStream_WithoutGzip_ReturnsOriginalStream()
        {
            // Arrange
            var originalStream = new MemoryStream(Encoding.UTF8.GetBytes("test data"));
            var headers = new HttpContentHeaders();

            // Act
            var resultStream = _apiClient.TestPrepareJsonStream(originalStream, headers);

            // Assert
            Assert.AreEqual(originalStream, resultStream);
        }

        [Test]
        public void PrepareJsonStream_WithGzip_ReturnsGZipStream()
        {
            // Arrange
            var originalStream = new MemoryStream();
            var headers = new HttpContentHeaders();
            headers.ContentEncoding.Add("gzip");

            // Act
            var resultStream = _apiClient.TestPrepareJsonStream(originalStream, headers);

            // Assert
            Assert.IsInstanceOf<GZipStream>(resultStream);
        }

        [Test]
        public void PrepareJsonStream_WithMultipleEncodings_ReturnsGZipStreamWhenGzipPresent()
        {
            // Arrange
            var originalStream = new MemoryStream();
            var headers = new HttpContentHeaders();
            headers.ContentEncoding.Add("deflate");
            headers.ContentEncoding.Add("gzip");

            // Act
            var resultStream = _apiClient.TestPrepareJsonStream(originalStream, headers);

            // Assert
            Assert.IsInstanceOf<GZipStream>(resultStream);
        }

        #endregion

        #region DeserializeJson Tests

        [Test]
        public void DeserializeJson_ValidJson_DeserializesCorrectly()
        {
            // Arrange
            var testObject = new TestModel { Id = 123, Name = "Test" };
            var json = JsonConvert.SerializeObject(testObject);
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var headers = new HttpContentHeaders();

            // Act
            var result = _apiClient.TestDeserializeJson<TestModel>(stream, headers, "Test Label", out var bytesRead);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(123, result.Id);
            Assert.AreEqual("Test", result.Name);
            Assert.Greater(bytesRead, 0);
        }

        [Test]
        public void DeserializeJson_InvalidJson_ThrowsException()
        {
            // Arrange
            var invalidJson = "{ invalid json }";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalidJson));
            var headers = new HttpContentHeaders();

            // Act & Assert
            Assert.Throws<JsonReaderException>(() =>
            {
                _apiClient.TestDeserializeJson<TestModel>(stream, headers, "Test Label", out var bytesRead);
            });
        }

        [Test]
        public void DeserializeJson_EmptyStream_ReturnsNull()
        {
            // Arrange
            var stream = new MemoryStream();
            var headers = new HttpContentHeaders();

            // Act
            var result = _apiClient.TestDeserializeJson<TestModel>(stream, headers, "Test Label", out var bytesRead);

            // Assert
            Assert.IsNull(result);
            Assert.AreEqual(0, bytesRead);
        }

        [Test]
        public void DeserializeJson_WithGzipCompression_DeserializesCorrectly()
        {
            // Arrange
            var testObject = new TestModel { Id = 456, Name = "Compressed" };
            var json = JsonConvert.SerializeObject(testObject);
            var compressedStream = new MemoryStream();
            
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Compress, true))
            {
                var jsonBytes = Encoding.UTF8.GetBytes(json);
                gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
            }
            
            compressedStream.Position = 0;
            var headers = new HttpContentHeaders();
            headers.ContentEncoding.Add("gzip");

            // Act
            var result = _apiClient.TestDeserializeJson<TestModel>(compressedStream, headers, "Test Label", out var bytesRead);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(456, result.Id);
            Assert.AreEqual("Compressed", result.Name);
            Assert.Greater(bytesRead, 0);
        }

        #endregion

        #region ReadBodyForLoggingAsync Tests

        [Test]
        public async Task ReadBodyForLoggingAsync_WithBodyLoggingEnabled_ReturnsBody()
        {
            // Arrange
            var bodyContent = "Test body content";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(bodyContent));
            var headers = new HttpContentHeaders();
            _apiClient.SetBodyLogging(true);

            // Act
            var result = await _apiClient.TestReadBodyForLoggingAsync(stream, headers);

            // Assert
            Assert.AreEqual(bodyContent, result);
        }

        [Test]
        public async Task ReadBodyForLoggingAsync_WithBodyLoggingDisabled_ReturnsEmptyString()
        {
            // Arrange
            var bodyContent = "Test body content";
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(bodyContent));
            var headers = new HttpContentHeaders();
            _apiClient.SetBodyLogging(false);

            // Act
            var result = await _apiClient.TestReadBodyForLoggingAsync(stream, headers);

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [Test]
        public async Task ReadBodyForLoggingAsync_WithGzipContent_DecompressesAndReturnsBody()
        {
            // Arrange
            var bodyContent = "Compressed body content";
            var compressedStream = new MemoryStream();
            
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Compress, true))
            {
                var contentBytes = Encoding.UTF8.GetBytes(bodyContent);
                gzipStream.Write(contentBytes, 0, contentBytes.Length);
            }
            
            compressedStream.Position = 0;
            var headers = new HttpContentHeaders();
            headers.ContentEncoding.Add("gzip");
            _apiClient.SetBodyLogging(true);

            // Act
            var result = await _apiClient.TestReadBodyForLoggingAsync(compressedStream, headers);

            // Assert
            Assert.AreEqual(bodyContent, result);
        }

        #endregion

        #region UpdateResponseMetrics Tests

        [Test]
        public void UpdateResponseMetrics_UpdatesCounters()
        {
            // Arrange
            var initialCompressed = _apiClient.ResponseTotalCompressedBytes;
            var initialUncompressed = _apiClient.ResponseTotalUncompressedBytes;
            long bytesRead = 1000;
            long? headerContentLength = 500;

            // Act
            _apiClient.TestUpdateResponseMetrics(bytesRead, headerContentLength);

            // Assert
            Assert.AreEqual(initialUncompressed + bytesRead, _apiClient.ResponseTotalUncompressedBytes);
            Assert.AreEqual(initialCompressed + headerContentLength.Value, _apiClient.ResponseTotalCompressedBytes);
        }

        [Test]
        public void UpdateResponseMetrics_WithNullHeaderLength_UsesBytesRead()
        {
            // Arrange
            var initialCompressed = _apiClient.ResponseTotalCompressedBytes;
            var initialUncompressed = _apiClient.ResponseTotalUncompressedBytes;
            long bytesRead = 1000;

            // Act
            _apiClient.TestUpdateResponseMetrics(bytesRead, null);

            // Assert
            Assert.AreEqual(initialUncompressed + bytesRead, _apiClient.ResponseTotalUncompressedBytes);
            Assert.AreEqual(initialCompressed + bytesRead, _apiClient.ResponseTotalCompressedBytes);
        }

        [Test]
        public void UpdateResponseMetrics_ThreadSafe_HandlesMultipleCalls()
        {
            // Arrange
            var tasks = new Task[10];
            long bytesPerCall = 100;

            // Act
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => _apiClient.TestUpdateResponseMetrics(bytesPerCall, bytesPerCall));
            }
            Task.WaitAll(tasks);

            // Assert
            // Should have accumulated all the bytes from parallel calls
            Assert.Greater(_apiClient.ResponseTotalUncompressedBytes, 0);
            Assert.Greater(_apiClient.ResponseTotalCompressedBytes, 0);
        }

        #endregion

        #region ProcessJsonResponse Tests

        [Test]
        public async Task ProcessJsonResponse_WithValidContent_ReturnsContent()
        {
            // Arrange
            var testContent = new TestModel { Id = 789, Name = "Content" };
            var json = JsonConvert.SerializeObject(testContent);
            var responseMessage = CreateResponseMessage(json, HttpStatusCode.OK);
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");

            // Act
            var (content, error, body, errorResponse) = await _apiClient.TestProcessJsonResponse<TestModel, ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNotNull(content);
            Assert.AreEqual(789, content.Id);
            Assert.AreEqual("Content", content.Name);
            Assert.IsNull(error);
            Assert.IsNull(errorResponse);
        }

        [Test]
        public async Task ProcessJsonResponse_WithErrorStatusCode_ParsesError()
        {
            // Arrange
            var testError = new ErrorModel { Code = "ERR001", Message = "Error occurred" };
            var json = JsonConvert.SerializeObject(testError);
            var responseMessage = CreateResponseMessage(json, HttpStatusCode.BadRequest);
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");

            // Act
            var (content, error, body, errorResponse) = await _apiClient.TestProcessJsonResponse<TestModel, ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNull(content);
            Assert.IsNotNull(error);
            Assert.AreEqual("ERR001", error.Code);
            Assert.AreEqual("Error occurred", error.Message);
            Assert.IsNull(errorResponse);
        }

        [Test]
        public async Task ProcessJsonResponse_WithNonJsonContent_ReturnsEmpty()
        {
            // Arrange
            var responseMessage = CreateResponseMessage("plain text", HttpStatusCode.OK, "text/plain");
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");

            // Act
            var (content, error, body, errorResponse) = await _apiClient.TestProcessJsonResponse<TestModel, ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNull(content);
            Assert.IsNull(error);
            Assert.AreEqual(string.Empty, body);
            Assert.IsNull(errorResponse);
        }

        [Test]
        public async Task ProcessJsonResponse_WithInvalidJson_ReturnsErrorResponse()
        {
            // Arrange
            var invalidJson = "{ invalid json structure";
            var responseMessage = CreateResponseMessage(invalidJson, HttpStatusCode.OK);
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");

            // Act
            var (content, error, body, errorResponse) = await _apiClient.TestProcessJsonResponse<TestModel, ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNull(content);
            Assert.IsNull(error);
            Assert.IsNotNull(errorResponse);
            Assert.IsInstanceOf<ParsingErrorHttpResponse>(errorResponse);
        }

        #endregion

        #region ProcessJsonErrorResponse Tests

        [Test]
        public async Task ProcessJsonErrorResponse_WithErrorStatusCode_ParsesError()
        {
            // Arrange
            var testError = new ErrorModel { Code = "ERR002", Message = "Server error" };
            var json = JsonConvert.SerializeObject(testError);
            var responseMessage = CreateResponseMessage(json, HttpStatusCode.InternalServerError);
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");

            // Act
            var (error, body, errorResponse) = await _apiClient.TestProcessJsonErrorResponse<ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNotNull(error);
            Assert.AreEqual("ERR002", error.Code);
            Assert.AreEqual("Server error", error.Message);
            Assert.IsNull(errorResponse);
        }

        [Test]
        public async Task ProcessJsonErrorResponse_WithSuccessStatusCode_ReturnsNull()
        {
            // Arrange
            var json = JsonConvert.SerializeObject(new ErrorModel { Code = "OK", Message = "Success" });
            var responseMessage = CreateResponseMessage(json, HttpStatusCode.OK);
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");

            // Act
            var (error, body, errorResponse) = await _apiClient.TestProcessJsonErrorResponse<ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNull(error);
            Assert.IsNull(errorResponse);
        }

        [Test]
        public async Task ProcessJsonErrorResponse_WithNonJsonContent_ReturnsEmpty()
        {
            // Arrange
            var responseMessage = CreateResponseMessage("plain text", HttpStatusCode.BadRequest, "text/plain");
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");

            // Act
            var (error, body, errorResponse) = await _apiClient.TestProcessJsonErrorResponse<ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNull(error);
            Assert.AreEqual(string.Empty, body);
            Assert.IsNull(errorResponse);
        }

        #endregion

        #region Helper Methods

        private HttpResponseMessage CreateResponseMessage(string content, HttpStatusCode statusCode, string contentType = "application/json")
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, contentType)
            };
            return response;
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

    /// <summary>
    /// Testable version of ApiClient that exposes helper methods for testing
    /// </summary>
    public class ApiClientTestable : ApiClient
    {
        public ApiClientTestable(ApiClientOptions options) : base(options)
        {
        }

        public Stream TestPrepareJsonStream(Stream source, HttpContentHeaders headers)
        {
            return PrepareJsonStream(source, headers);
        }

        public T TestDeserializeJson<T>(Stream memoryStream, HttpContentHeaders headers, string profilerLabel, out long bytesRead)
        {
            return DeserializeJson<T>(memoryStream, headers, profilerLabel, out bytesRead);
        }

        public Task<string> TestReadBodyForLoggingAsync(Stream memoryStream, HttpContentHeaders headers)
        {
            return ReadBodyForLoggingAsync(memoryStream, headers);
        }

        public void TestUpdateResponseMetrics(long bytesRead, long? headerContentLength)
        {
            UpdateResponseMetrics(bytesRead, headerContentLength);
        }

        public Task<(T content, E error, string body, IHttpResponse errorResponse)> TestProcessJsonResponse<T, E>(
            HttpResponseMessage responseMessage,
            HttpRequestMessage requestMessage)
        {
            return ProcessJsonResponse<T, E>(responseMessage, requestMessage);
        }

        public Task<(E error, string body, IHttpResponse errorResponse)> TestProcessJsonErrorResponse<E>(
            HttpResponseMessage responseMessage,
            HttpRequestMessage requestMessage)
        {
            return ProcessJsonErrorResponse<E>(responseMessage, requestMessage);
        }

        public void SetBodyLogging(bool enabled)
        {
            // Use reflection to set the private field
            var field = typeof(ApiClient).GetField("_bodyLogging", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(this, enabled);
        }
    }
}
