using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using ApiClient.Runtime.HttpResponses;
using UnityEngine;

namespace ApiClient.Runtime.Auxiliary
{
    public static class Extensions
    {
        /// <summary>
        /// Convert byte[] <see cref="HttpResponse"/> to Sprite Image
        /// </summary>
        /// <param name="byteArrayResponse">Input response with byte[] content</param>
        /// <param name="sprite">Output sprite</param>
        /// <param name="errorMessage">Output Error message if conversion was unsuccesful</param>
        /// <typeparam name="T">Response of <see cref="IHttpResponse"/> type</typeparam>
        /// <returns>True - if converted, false if failed</returns>
        public static bool ToSpriteImage<T>(this T byteArrayResponse, out Sprite sprite, out string errorMessage) where T : HttpResponse<byte[]>
        {
            sprite = null;
            errorMessage = null;

            if (!(byteArrayResponse as IHttpResponse).HasNoErrors)
            {
                errorMessage = $"{nameof(ToSpriteImage)} -> Response has errors";
                return false;
            }

            try
            {
                Texture2D tex = new(2, 2);
                if (ImageConversion.LoadImage(tex, byteArrayResponse.Content))
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
                    errorMessage = $"{nameof(ToSpriteImage)} -> Could not load image from:{byteArrayResponse.RequestUri}";
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
                if (headerValuesValues == null)
                {
                    return false;
                }

                // We are expecting only one value here. Count() followed by ElementAt(0) walked the
                // sequence twice and allocated an enumerator each time, on every header lookup.
                int count = 0;
                string first = null;
                foreach (var value in headerValuesValues)
                {
                    if (++count > 1)
                    {
                        return false;
                    }
                    first = value;
                }

                if (count == 1)
                {
                    headerValue = first;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Flattens headers into a dictionary, joining multi-valued headers with ';'.
        /// </summary>
        /// <remarks>
        /// Called twice for every response constructed (headers + content headers), and once per
        /// message on a stream, so it runs on the hot path. The former LINQ ToDictionary(x => x.Key,
        /// x => string.Join(";", x.Value)) allocated two closures, an enumerator chain and a joined
        /// string per header even though virtually every header carries exactly one value.
        /// </remarks>
        public static Dictionary<string, string> ToHeadersDictionary(this HttpHeaders headers)
        {
            if (headers == null)
            {
                return null;
            }

            var result = new Dictionary<string, string>();
            foreach (var header in headers)
            {
                result[header.Key] = FlattenHeaderValues(header.Value);
            }
            return result;
        }

        /// <summary>
        /// Equivalent to <c>string.Join(";", values)</c>, without allocating for the single-value case.
        /// </summary>
        private static string FlattenHeaderValues(IEnumerable<string> values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            int count = 0;
            string first = null;
            StringBuilder joined = null;

            foreach (var value in values)
            {
                count++;
                if (count == 1)
                {
                    first = value;
                    continue;
                }
                if (count == 2)
                {
                    joined = new StringBuilder(first ?? string.Empty);
                }
                joined.Append(';').Append(value);
            }

            if (joined != null)
            {
                return joined.ToString();
            }
            // string.Join renders a lone null element as an empty string; match that.
            return count == 1 ? first ?? string.Empty : string.Empty;
        }
    }
}