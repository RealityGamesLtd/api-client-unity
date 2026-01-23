using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using ApiClient.Runtime.Auxiliary;
using ApiClient.Runtime.HttpResponses;
using NUnit.Framework;
using UnityEngine;

namespace ApiClient.Tests
{
    [TestFixture]
    public class ExtensionsTests
    {
        [Test]
        public void PostOnMainThread_InvokesCalled_WhenContextProvided()
        {
            // Arrange
            var syncContext = new TestSynchronizationContext();
            var callbackInvoked = false;
            var expectedValue = 42;
            var actualValue = 0;

            Action<int> callback = (value) =>
            {
                callbackInvoked = true;
                actualValue = value;
            };

            // Act
            callback.PostOnMainThread(expectedValue, syncContext);
            syncContext.ExecutePendingCallbacks();

            // Assert
            Assert.IsTrue(callbackInvoked, "Callback should have been invoked");
            Assert.AreEqual(expectedValue, actualValue, "Value should match");
        }

        [Test]
        public void PostOnMainThread_NullCallback_DoesNotThrow()
        {
            // Arrange
            var syncContext = new TestSynchronizationContext();
            Action<int> callback = null;

            // Act & Assert
            Assert.DoesNotThrow(() =>
            {
                callback.PostOnMainThread(42, syncContext);
                syncContext.ExecutePendingCallbacks();
            });
        }

        [Test]
        public void GetHeader_SingleValue_ReturnsTrue()
        {
            // Arrange
            var response = new HttpResponseMessage();
            response.Headers.Add("X-Custom-Header", "test-value");

            // Act
            var result = response.Headers.GetHeader("X-Custom-Header", out var headerValue);

            // Assert
            Assert.IsTrue(result, "Should return true for existing single-value header");
            Assert.AreEqual("test-value", headerValue);
        }

        [Test]
        public void GetHeader_MultipleValues_ReturnsFalse()
        {
            // Arrange
            var response = new HttpResponseMessage();
            response.Headers.Add("X-Custom-Header", new[] { "value1", "value2" });

            // Act
            var result = response.Headers.GetHeader("X-Custom-Header", out var headerValue);

            // Assert
            Assert.IsFalse(result, "Should return false for multi-value header");
            Assert.IsNull(headerValue);
        }

        [Test]
        public void GetHeader_NonExistentHeader_ReturnsFalse()
        {
            // Arrange
            var response = new HttpResponseMessage();

            // Act
            var result = response.Headers.GetHeader("X-Non-Existent", out var headerValue);

            // Assert
            Assert.IsFalse(result, "Should return false for non-existent header");
            Assert.IsNull(headerValue);
        }

        [Test]
        public void GetHeader_NullHeaders_ReturnsFalse()
        {
            // Arrange
            HttpResponseHeaders headers = null;

            // Act
            var result = headers.GetHeader("X-Custom-Header", out var headerValue);

            // Assert
            Assert.IsFalse(result, "Should return false for null headers");
            Assert.IsNull(headerValue);
        }

        [Test]
        public void ToHeadersDictionary_ValidHeaders_ReturnsDictionary()
        {
            // Arrange
            var response = new HttpResponseMessage();
            response.Headers.Add("X-Header-1", "value1");
            response.Headers.Add("X-Header-2", "value2");

            // Act
            var dictionary = response.Headers.ToHeadersDictionary();

            // Assert
            Assert.IsNotNull(dictionary);
            Assert.AreEqual(2, dictionary.Count);
            Assert.AreEqual("value1", dictionary["X-Header-1"]);
            Assert.AreEqual("value2", dictionary["X-Header-2"]);
        }

        [Test]
        public void ToHeadersDictionary_MultipleValuesInHeader_JoinsWithSemicolon()
        {
            // Arrange
            var response = new HttpResponseMessage();
            response.Headers.Add("X-Multi-Header", new[] { "value1", "value2", "value3" });

            // Act
            var dictionary = response.Headers.ToHeadersDictionary();

            // Assert
            Assert.IsNotNull(dictionary);
            Assert.AreEqual("value1;value2;value3", dictionary["X-Multi-Header"]);
        }

        [Test]
        public void ToHeadersDictionary_EmptyHeaders_ReturnsEmptyDictionary()
        {
            // Arrange
            var response = new HttpResponseMessage();

            // Act
            var dictionary = response.Headers.ToHeadersDictionary();

            // Assert
            Assert.IsNotNull(dictionary);
            Assert.AreEqual(0, dictionary.Count);
        }

        [Test]
        public void ToHeadersDictionary_NullHeaders_ReturnsNull()
        {
            // Arrange
            HttpHeaders headers = null;

            // Act
            var dictionary = headers.ToHeadersDictionary();

            // Assert
            Assert.IsNull(dictionary);
        }

        [Test]
        public void ToSpriteImage_ResponseWithErrors_ReturnsFalse()
        {
            // Arrange
            var response = new HttpResponse<byte[]>(
                null,
                null,
                null,
                null,
                new Uri("http://test.com"),
                HttpStatusCode.BadRequest);

            // Act
            var result = response.ToSpriteImage(out var sprite, out var errorMessage);

            // Assert
            Assert.IsFalse(result, "Should return false when response has errors");
            Assert.IsNull(sprite);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("Response has errors"));
        }

        [Test]
        public void ToSpriteImage_NullContent_ReturnsFalse()
        {
            // Arrange
            var response = new HttpResponse<byte[]>(
                null,
                null,
                null,
                null,
                new Uri("http://test.com"),
                HttpStatusCode.OK);

            // Act
            var result = response.ToSpriteImage(out var sprite, out var errorMessage);

            // Assert
            Assert.IsFalse(result, "Should return false with null content");
            Assert.IsNull(sprite);
            Assert.IsNotNull(errorMessage);
        }

        [Test]
        public void ToSpriteImage_InvalidImageData_ReturnsFalse()
        {
            // Arrange
            var invalidImageData = new byte[] { 0x00, 0x01, 0x02, 0x03 }; // Not a valid image
            var response = new HttpResponse<byte[]>(
                invalidImageData,
                null,
                null,
                null,
                new Uri("http://test.com/image.png"),
                HttpStatusCode.OK);

            // Act
            var result = response.ToSpriteImage(out var sprite, out var errorMessage);

            // Assert
            Assert.IsFalse(result, "Should return false with invalid image data");
            Assert.IsNull(sprite);
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage, Does.Contain("Could not load image"));
        }

        // Helper class for testing SynchronizationContext
        private class TestSynchronizationContext : SynchronizationContext
        {
            private readonly Queue<(SendOrPostCallback callback, object state)> _callbacks = new();

            public override void Post(SendOrPostCallback d, object state)
            {
                _callbacks.Enqueue((d, state));
            }

            public void ExecutePendingCallbacks()
            {
                while (_callbacks.Count > 0)
                {
                    var (callback, state) = _callbacks.Dequeue();
                    callback?.Invoke(state);
                }
            }
        }
    }
}
