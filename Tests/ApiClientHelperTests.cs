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

namespace ApiClient.Tests
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
                BodyLogging = false  // Disable body logging for tests
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
            using var originalStream = new MemoryStream(Encoding.UTF8.GetBytes("test data"));
            var headers = GetContentHeaders();

            // Act
            var resultStream = _apiClient.TestPrepareJsonStream(originalStream, headers);

            // Assert
            Assert.AreEqual(originalStream, resultStream);
        }

        [Test]
        public void PrepareJsonStream_WithGzip_ReturnsGZipStream()
        {
            // Arrange
            using var originalStream = new MemoryStream();
            var headers = GetContentHeaders(addGzip: true);

            // Act
            var resultStream = _apiClient.TestPrepareJsonStream(originalStream, headers);

            // Assert
            Assert.IsInstanceOf<GZipStream>(resultStream);
        }

        [Test]
        public void PrepareJsonStream_WithMultipleEncodings_ReturnsGZipStreamWhenGzipPresent()
        {
            // Arrange
            using var originalStream = new MemoryStream();
            var headers = GetContentHeaders();
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
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var headers = GetContentHeaders();

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
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalidJson));
            var headers = GetContentHeaders();

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
            using var stream = new MemoryStream();
            var headers = GetContentHeaders();

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
            using var compressedStream = new MemoryStream();
            
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Compress, true))
            {
                var jsonBytes = Encoding.UTF8.GetBytes(json);
                gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
            }
            
            compressedStream.Position = 0;
            var headers = GetContentHeaders(addGzip: true);

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
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(bodyContent));
            var headers = GetContentHeaders();
            var loggingClient = CreateApiClient(bodyLoggingEnabled: true);

            // Act
            var result = await loggingClient.TestReadBodyForLoggingAsync(stream, headers);

            // Assert
            Assert.AreEqual(bodyContent, result);
        }

        [Test]
        public async Task ReadBodyForLoggingAsync_WithBodyLoggingDisabled_ReturnsEmptyString()
        {
            // Arrange
            var bodyContent = "Test body content";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(bodyContent));
            var headers = GetContentHeaders();

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
            using var compressedStream = new MemoryStream();
            
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Compress, true))
            {
                var contentBytes = Encoding.UTF8.GetBytes(bodyContent);
                gzipStream.Write(contentBytes, 0, contentBytes.Length);
            }
            
            compressedStream.Position = 0;
            var headers = GetContentHeaders(addGzip: true);
            var loggingClient = CreateApiClient(bodyLoggingEnabled: true);

            // Act
            var result = await loggingClient.TestReadBodyForLoggingAsync(compressedStream, headers);

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
            var initialCompressed = _apiClient.ResponseTotalCompressedBytes;
            var initialUncompressed = _apiClient.ResponseTotalUncompressedBytes;

            // Act
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() => _apiClient.TestUpdateResponseMetrics(bytesPerCall, bytesPerCall));
            }
            Task.WaitAll(tasks);

            var expectedDelta = tasks.Length * bytesPerCall;

            // Assert
            Assert.AreEqual(initialUncompressed + expectedDelta, _apiClient.ResponseTotalUncompressedBytes);
            Assert.AreEqual(initialCompressed + expectedDelta, _apiClient.ResponseTotalCompressedBytes);
        }

        #endregion

        #region ProcessJsonResponse Tests

        [Test]
        public async Task ProcessJsonResponse_WithValidContent_ReturnsContent()
        {
            // Arrange
            var testContent = new TestModel { Id = 789, Name = "Content" };
            var json = JsonConvert.SerializeObject(testContent);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            using var responseMessage = CreateResponseMessage(json, HttpStatusCode.OK);

            // Act
            var result = await _apiClient.TestProcessJsonResponse<TestModel, ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNotNull(result.content);
            Assert.AreEqual(789, result.content.Id);
            Assert.AreEqual("Content", result.content.Name);
            Assert.IsNull(result.error);
            Assert.IsNull(result.errorResponse);
        }

        [Test]
        public async Task ProcessJsonResponse_WithErrorStatusCode_ParsesError()
        {
            // Arrange
            var testError = new ErrorModel { Code = "ERR001", Message = "Error occurred" };
            var json = JsonConvert.SerializeObject(testError);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            using var responseMessage = CreateResponseMessage(json, HttpStatusCode.BadRequest);

            // Act
            var result = await _apiClient.TestProcessJsonResponse<TestModel, ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNull(result.content);
            Assert.IsNotNull(result.error);
            Assert.AreEqual("ERR001", result.error.Code);
            Assert.AreEqual("Error occurred", result.error.Message);
            Assert.IsNull(result.errorResponse);
        }

        [Test]
        public async Task ProcessJsonResponse_WithNonJsonContent_ReturnsEmpty()
        {
            // Arrange
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            using var responseMessage = CreateResponseMessage("plain text", HttpStatusCode.OK, "text/plain");

            // Act
            var result = await _apiClient.TestProcessJsonResponse<TestModel, ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNull(result.content);
            Assert.IsNull(result.error);
            Assert.AreEqual(string.Empty, result.body);
            Assert.IsNull(result.errorResponse);
        }

        [Test]
        public async Task ProcessJsonResponse_WithInvalidJson_ReturnsErrorResponse()
        {
            // Arrange
            var invalidJson = "{ invalid json structure";
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            using var responseMessage = CreateResponseMessage(invalidJson, HttpStatusCode.OK);

            // Act
            var result = await _apiClient.TestProcessJsonResponse<TestModel, ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNull(result.content);
            Assert.IsNull(result.error);
            Assert.IsNotNull(result.errorResponse);
            Assert.IsInstanceOf<ParsingErrorHttpResponse>(result.errorResponse);
        }

        [Test]
        public async Task ProcessJsonResponse_With399StatusCode_TreatedAsSuccess()
        {
            // Arrange
            var testContent = new TestModel { Id = 399, Name = "Boundary" };
            var json = JsonConvert.SerializeObject(testContent);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            using var responseMessage = CreateResponseMessage(json, (HttpStatusCode)399);

            // Act
            var result = await _apiClient.TestProcessJsonResponse<TestModel, ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNotNull(result.content);
            Assert.AreEqual(399, result.content.Id);
            Assert.AreEqual("Boundary", result.content.Name);
            Assert.IsNull(result.error);
            Assert.IsNull(result.errorResponse);
        }

        #endregion

        #region ProcessJsonErrorResponse Tests

        [Test]
        public async Task ProcessJsonErrorResponse_WithErrorStatusCode_ParsesError()
        {
            // Arrange
            var testError = new ErrorModel { Code = "ERR002", Message = "Server error" };
            var json = JsonConvert.SerializeObject(testError);
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            using var responseMessage = CreateResponseMessage(json, HttpStatusCode.InternalServerError);

            // Act
            var result = await _apiClient.TestProcessJsonErrorResponse<ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNotNull(result.error);
            Assert.AreEqual("ERR002", result.error.Code);
            Assert.AreEqual("Server error", result.error.Message);
            Assert.IsNull(result.errorResponse);
        }

        [Test]
        public async Task ProcessJsonErrorResponse_WithSuccessStatusCode_ReturnsNull()
        {
            // Arrange
            var json = JsonConvert.SerializeObject(new ErrorModel { Code = "OK", Message = "Success" });
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            using var responseMessage = CreateResponseMessage(json, HttpStatusCode.OK);

            // Act
            var result = await _apiClient.TestProcessJsonErrorResponse<ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNull(result.error);
            Assert.IsNull(result.errorResponse);
        }

        [Test]
        public async Task ProcessJsonErrorResponse_WithNonJsonContent_ReturnsEmpty()
        {
            // Arrange
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, "http://test.com");
            using var responseMessage = CreateResponseMessage("plain text", HttpStatusCode.BadRequest, "text/plain");

            // Act
            var result = await _apiClient.TestProcessJsonErrorResponse<ErrorModel>(
                responseMessage, requestMessage);

            // Assert
            Assert.IsNull(result.error);
            Assert.AreEqual(string.Empty, result.body);
            Assert.IsNull(result.errorResponse);
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

        private HttpContentHeaders GetContentHeaders(string content = "", bool addGzip = false)
        {
            var httpContent = new StringContent(content, Encoding.UTF8, "application/json");
            if (addGzip)
            {
                httpContent.Headers.ContentEncoding.Add("gzip");
            }

            return httpContent.Headers;
        }

        private ApiClientTestable CreateApiClient(bool bodyLoggingEnabled)
        {
            var options = new ApiClientOptions
            {
                Timeout = _options.Timeout,
                GraphQLClientEndpoint = _options.GraphQLClientEndpoint,
                RetryPolicies = _options.RetryPolicies,
                Middleware = _options.Middleware,
                StreamBufferSize = _options.StreamBufferSize,
                ByteArrayBufferSize = _options.ByteArrayBufferSize,
                Version = _options.Version,
                StreamReadDeltaUpdateTime = _options.StreamReadDeltaUpdateTime,
                VerboseLogging = _options.VerboseLogging,
                BodyLogging = bodyLoggingEnabled
            };

            return new ApiClientTestable(options);
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
    public class ApiClientTestable : ApiClient.Runtime.ApiClient
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
    }
}
