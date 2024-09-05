using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading;
using ApiClient.Runtime.HttpResponses;
using UnityEngine;

namespace ApiClient.Runtime
{
    public static class Extensions
    {
        public static void PostOnMainThread<T>(this Action<T> callback, T value, SynchronizationContext context)
        {
            context.Post((o) => callback?.Invoke(value), null);
        }

        /// <summary>
        /// Convert byte[] <see cref="HttpResponse"/> to Sprite Image
        /// </summary>
        /// <param name="byteArrayResponse">Input response with byte[] content</param>
        /// <param name="sprite">Output sprite</param>
        /// <param name="errorMessage">Output Error message if conversion was unsuccesful</param>
        /// <typeparam name="T">Response of <see cref="IHttpResponse"/> type</typeparam>
        /// <returns>True - if converted, false if failed</returns>
        public static bool ToSpriteImage<T>(this T byteArrayResponse, out Sprite sprite, out string errorMessage) where T : IHttpResponse
        {
            sprite = null;
            errorMessage = null;

            if ((byteArrayResponse is HttpResponse<byte[]> response) == false)
            {
                errorMessage = $"{nameof(ToSpriteImage)} -> Invalid response type. Expecting:{typeof(HttpResponse<byte[]>)}";
                return false;
            }

            if (!byteArrayResponse.HasNoErrors)
            {
                errorMessage = $"{nameof(ToSpriteImage)} -> Response has errors";
                return false;
            }

            try
            {
                Texture2D tex = new(2, 2);
                if (ImageConversion.LoadImage(tex, response.Content))
                {
                    sprite = Sprite.Create(
                        tex,
                        new Rect(
                            0,
                            0,
                            tex.width,
                            tex.height),
                        Vector2.one / 2f /* center pivot */);
                }
                else
                {
                    errorMessage = $"{nameof(ToSpriteImage)} -> Could not load image from:{response.RequestUri}";
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"{nameof(ToSpriteImage)} -> {ex}";
            }

            return sprite != null;
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
            if (httpResponseHeaders?.TryGetValues(name, out IEnumerable<string> headerValuesValues) ?? false)
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

        public static Dictionary<string, string> ToHeadersDictionary(this HttpHeaders headers)
        {
            return headers?.ToDictionary(
                                x => x.Key,
                                x => string.Join(";", x.Value));
        }
    }
}