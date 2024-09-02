using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using ApiClient.Runtime.HttpResponses;

namespace ApiClient.Runtime
{
    public static class Extensions
    {
        public static void InvokeOnMainThread(this Action<IHttpResponse> responseCallback, IHttpResponse response)
        {
            ThreadDispatcher.RunOnMainThread(() =>
            {
                responseCallback?.Invoke(response);
            });
        }

        public static void InvokeOnMainThread<T>(this Action<T> callback, T value)
        {
            ThreadDispatcher.RunOnMainThread(() =>
            {
                callback?.Invoke(value);
            });
        }

        /// <summary>
        /// Helper method to get single value header.
        /// </summary>
        /// <param name="httpResponseHeaders">Headers</param>
        /// <param name="name">Header name to get</param>
        /// <param name="headerValue">Extracted value</param>
        /// <returns>True - if header exists and has only one value</returns>
        public static bool GetHeader(this HttpResponseHeaders httpResponseHeaders, string name, out string headerValue)
        {
            headerValue = null;
            if (httpResponseHeaders.TryGetValues(name, out IEnumerable<string> headerValuesValues))
            {
                // we are expecting only one value here
                if (headerValuesValues != null && headerValuesValues.Count() == 1)
                {
                    headerValue = headerValuesValues.ElementAt(0);
                    return true;
                }
            }
            return false;
        }

        public static  Dictionary<string, string> ToHeadersDictionary(this HttpHeaders headers)
        {
            return headers.ToDictionary(
                                x => x.Key,
                                x => string.Join(";", x.Value));
        }
    }
}