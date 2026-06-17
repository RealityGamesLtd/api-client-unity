using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using ApiClient.Runtime;
using ApiClient.Runtime.Requests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
// The bare type name "ApiClient" collides with the enclosing "ApiClient" namespace, so
// alias the concrete client type to reference it unambiguously from this assembly.
using ApiClientImpl = ApiClient.Runtime.ApiClient;

namespace ApiClient.Tests
{
    /// <summary>
    /// Covers the per-request <see cref="IHttpRequest.ExpectedStatusCodes"/> hint added so
    /// callers can mark a non-success code (typically 404) as normal for a given request.
    /// Two things matter and are tested here:
    ///   1. Wiring — the <c>Create*</c> factories stamp the codes onto the request, including
    ///      across the retry-recreate path (otherwise the hint would be lost on the first retry).
    ///   2. Severity — when verbose logging is on, an expected code logs at info level instead
    ///      of as an error, while everything else still logs as an error (legacy behaviour).
    /// The transport is not mockable, so severity is verified by invoking the logging decision
    /// directly rather than over the network, keeping these tests deterministic. The severity
    /// tests use a typed JSON request (<c>CreateGet&lt;T, E&gt;</c>) on purpose — that is the
    /// path the hint is most commonly used on and the one wired up most recently.
    /// </summary>
    public class ExpectedStatusCodesTests
    {
        private const string Url = "https://example.com/resource";

        private static ApiClientOptions Options(bool verbose) =>
            new ApiClientOptions { VerboseLogging = verbose };

        private static IApiClientConnection Connection(bool verbose = false) =>
            new ApiClientConnection(Options(verbose));

        // ---- Wiring: factories stamp the hint onto the request ----

        [Test]
        public void CreateGet_WithExpectedStatusCodes_StampsThemOnRequest()
        {
            var request = Connection().CreateGet<object, object>(
                Url,
                CancellationToken.None,
                expectedStatusCodes: new[] { HttpStatusCode.NotFound });

            Assert.IsNotNull(request.ExpectedStatusCodes);
            Assert.Contains(HttpStatusCode.NotFound, new List<HttpStatusCode>(request.ExpectedStatusCodes));
        }

        [Test]
        public void CreateGet_WithoutExpectedStatusCodes_DefaultsToNull()
        {
            // Backward compatibility: existing callers that don't pass the hint must be
            // indistinguishable from before, so the helper falls back to error logging.
            var request = Connection().CreateGet<object, object>(Url, CancellationToken.None);

            Assert.IsNull(request.ExpectedStatusCodes);
        }

        [Test]
        public void CreateGetByteArrayRequest_WithExpectedStatusCodes_StampsThemOnRequest()
        {
            // The byte-array path is one of the paths that logs status codes, so verify the
            // hint reaches it too (not just the JSON factories).
            var request = Connection().CreateGetByteArrayRequest(
                Url,
                CancellationToken.None,
                expectedStatusCodes: new[] { HttpStatusCode.NotFound, HttpStatusCode.Gone });

            Assert.IsNotNull(request.ExpectedStatusCodes);
            CollectionAssert.AreEquivalent(
                new[] { HttpStatusCode.NotFound, HttpStatusCode.Gone },
                request.ExpectedStatusCodes);
        }

        [Test]
        public void RecreateWithHttpRequestMessage_PreservesExpectedStatusCodes()
        {
            // Retries re-create the request via the factory's recreate func. If the hint
            // weren't threaded through, the second attempt of an expected 404 would log an
            // error. Guard against that regression.
            var request = Connection().CreateGet<object, object>(
                Url,
                CancellationToken.None,
                expectedStatusCodes: new[] { HttpStatusCode.NotFound });

            var recreated = request.RecreateWithHttpRequestMessage();

            Assert.IsNotNull(recreated.ExpectedStatusCodes);
            Assert.Contains(HttpStatusCode.NotFound, new List<HttpStatusCode>(recreated.ExpectedStatusCodes));
        }

        // ---- Severity: expected -> info, otherwise -> error, gated by verbose logging ----

        [Test]
        public void LogNonSuccessStatus_ExpectedCode_LogsAsInfoNotError()
        {
            var options = Options(verbose: true);
            var apiClient = new ApiClientImpl(options);
            var request = new ApiClientConnection(options, apiClient).CreateGet<object, object>(
                Url,
                CancellationToken.None,
                expectedStatusCodes: new[] { HttpStatusCode.NotFound });

            // Expect an info log. The test runner fails on any *unexpected* error log, so
            // this both asserts the info log fired and that no error was emitted.
            LogAssert.Expect(LogType.Log, new Regex("statusCode:NotFound"));

            InvokeLogNonSuccessStatus(apiClient, request, HttpStatusCode.NotFound);
        }

        [Test]
        public void LogNonSuccessStatus_UnexpectedCode_LogsAsError()
        {
            var options = Options(verbose: true);
            var apiClient = new ApiClientImpl(options);
            // No expected codes declared -> ExpectedStatusCodes is null -> legacy error path.
            var request = new ApiClientConnection(options, apiClient).CreateGet<object, object>(
                Url,
                CancellationToken.None);

            LogAssert.Expect(LogType.Error, new Regex("statusCode:NotFound"));

            InvokeLogNonSuccessStatus(apiClient, request, HttpStatusCode.NotFound);
        }

        [Test]
        public void LogNonSuccessStatus_ExpectedCodeButNotTheReturnedOne_LogsAsError()
        {
            // The declared expected set must match the actual code. A request that expects a
            // 404 but gets a 500 should still log the 500 as an error.
            var options = Options(verbose: true);
            var apiClient = new ApiClientImpl(options);
            var request = new ApiClientConnection(options, apiClient).CreateGet<object, object>(
                Url,
                CancellationToken.None,
                expectedStatusCodes: new[] { HttpStatusCode.NotFound });

            LogAssert.Expect(LogType.Error, new Regex("statusCode:InternalServerError"));

            InvokeLogNonSuccessStatus(apiClient, request, HttpStatusCode.InternalServerError);
        }

        [Test]
        public void LogNonSuccessStatus_VerboseLoggingOff_LogsNothing()
        {
            var options = Options(verbose: false);
            var apiClient = new ApiClientImpl(options);
            var request = new ApiClientConnection(options, apiClient).CreateGet<object, object>(
                Url,
                CancellationToken.None);

            var logged = new List<string>();
            Application.LogCallback handler = (condition, _, __) =>
            {
                if (condition.Contains("statusCode"))
                {
                    logged.Add(condition);
                }
            };
            Application.logMessageReceived += handler;
            try
            {
                InvokeLogNonSuccessStatus(apiClient, request, HttpStatusCode.NotFound);
            }
            finally
            {
                Application.logMessageReceived -= handler;
            }

            CollectionAssert.IsEmpty(logged, "Verbose logging is off; the status code must not be logged at any level.");
        }

        // Severity is decided by a private method since the send pipeline owns logging and
        // the transport isn't injectable. Invoke it directly so the test stays deterministic.
        private static void InvokeLogNonSuccessStatus(
            ApiClientImpl apiClient,
            IHttpRequest request,
            HttpStatusCode statusCode)
        {
            var method = typeof(ApiClientImpl).GetMethod(
                "LogNonSuccessStatus",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(
                method,
                "ApiClient.LogNonSuccessStatus(IHttpRequest, HttpStatusCode, string) was not found. " +
                "If it was renamed or its signature changed, update this test to match.");

            method.Invoke(apiClient, new object[] { request, statusCode, "TestContext" });
        }
    }
}
