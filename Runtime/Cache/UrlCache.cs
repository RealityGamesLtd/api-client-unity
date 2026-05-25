using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Requests;

namespace ApiClient.Runtime.Cache
{
    public class UrlCache
    {
        private readonly ConcurrentDictionary<string, CacheData<IHttpResponse>> _cachedData = new();
        private readonly List<Regex> _rules = new();

        private IHttpDiskCacheStore _diskStore;
        private IHttpCacheBridge _bridge;

        /// <summary>Currently attached disk store, or null. Read-only outside <see cref="AttachDiskCache"/>.</summary>
        public IHttpDiskCacheStore DiskStore => _diskStore;

        /// <summary>
        /// Wire a persistent disk store + the bridge that knows how to rehydrate
        /// typed responses from cached bytes. Calling this with non-null arguments
        /// switches <see cref="Process"/> into HTTP-conditional mode whenever the
        /// caller supplies a <see cref="HttpCachePolicy"/>. Pass nulls to detach.
        /// </summary>
        public void AttachDiskCache(IHttpDiskCacheStore diskStore, IHttpCacheBridge bridge)
        {
            _diskStore = diskStore;
            _bridge = bridge;
        }

        public void AddRule(Regex rx)
        {
            if (_rules.Contains(rx))
            {
                UnityEngine.Debug.LogError($"{nameof(UrlCache)} -> Could not add url cache rule: duplicate rule {rx}");
                return;
            }

            _rules.Add(rx);
        }

        public bool MatchRules(string str)
        {
            foreach (var rule in _rules)
            {
                if (rule.IsMatch(str))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Process the request
        /// </summary>
        /// <param name="request">Request that is suppose to be made</param>
        /// <param name="cachePolicy">If null cache wont be used, otherwise it will follow the specified policy</param>
        /// <param name="continuationAction">Action that will be invoked to make the request</param>
        /// <param name="typedRehydrate">
        /// Optional callback that rebuilds a typed <see cref="IHttpResponse"/> from a
        /// cached body stream + metadata. Only invoked when (a) a disk store is attached,
        /// (b) <paramref name="cachePolicy"/> is a <see cref="HttpCachePolicy"/> with
        /// <see cref="HttpCachePolicy.UseConditionalRequests"/> true, and (c) the origin
        /// answers 304 Not Modified to the conditional GET. The caller supplies this
        /// because <see cref="UrlCache"/> is type-erased while the typed deserialiser
        /// lives on <see cref="ApiClient"/>.
        /// </param>
        /// <returns>Response either from cache or from the server</returns>
        public async Task<IHttpResponse> Process(
            IHttpRequest request,
            CachePolicy cachePolicy,
            Func<Task<IHttpResponse>> continuationAction,
            Func<DiskCacheEntry, Stream, Task<IHttpResponse>> typedRehydrate = null)
        {
            var httpPolicy = cachePolicy as HttpCachePolicy;
            var diskStore = _diskStore;
            var bridge = _bridge;
            var diskEligible = httpPolicy != null
                && diskStore != null
                && request?.RequestMessage != null
                && request.RequestMessage.Method == HttpMethod.Get
                && !(cachePolicy?.ForceExpire ?? false);

            string diskKey = null;
            DiskCacheEntry diskMeta = null;

            // Pre-flight: read meta from disk and stamp conditional headers
            if (diskEligible && httpPolicy.UseConditionalRequests)
            {
                diskKey = DiskCacheKey.Compute(
                    request.RequestMessage.Method,
                    request.Uri,
                    httpPolicy.VaryKey?.Invoke(request));

                if (diskKey != null)
                {
                    try
                    {
                        diskMeta = await diskStore.TryReadMetaAsync(diskKey, request.CancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        diskMeta = null;
                    }

                    if (diskMeta != null)
                    {
                        if (!string.IsNullOrEmpty(diskMeta.ETag))
                        {
                            request.RequestMessage.Headers.TryAddWithoutValidation("If-None-Match", diskMeta.ETag);
                        }
                        if (!string.IsNullOrEmpty(diskMeta.LastModified))
                        {
                            request.RequestMessage.Headers.TryAddWithoutValidation("If-Modified-Since", diskMeta.LastModified);
                        }
                    }
                }
            }

            IHttpResponse response = null;

            // override
            if (cachePolicy != null && cachePolicy.ForceExpire)
            {
                Invalidate(request.Uri.ToString());
            }
            // check in-memory cache
            else if (GetFromCache(request.Uri.ToString(), out IHttpResponse cachedResponse))
            {
                response = cachedResponse;
            }

            // make request
            if (response == null)
            {
                response = await continuationAction.Invoke().ConfigureAwait(false);

                // 304 Not Modified — hydrate from disk
                if (diskMeta != null && bridge != null && typedRehydrate != null
                    && response is IHttpResponseStatusCode status304
                    && status304.StatusCode == HttpStatusCode.NotModified)
                {
                    Stream bodyStream = null;
                    try
                    {
                        bodyStream = await diskStore.OpenBodyAsync(diskKey, request.CancellationToken).ConfigureAwait(false);
                        if (bodyStream != null)
                        {
                            var rehydrated = await typedRehydrate(diskMeta, bodyStream).ConfigureAwait(false);
                            if (rehydrated != null)
                            {
                                if (rehydrated is ICachedHttpResponse cached)
                                {
                                    cached.IsFromCache = true;
                                    cached.IsConditionalHit = true;
                                }
                                response = rehydrated;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogWarning($"{nameof(UrlCache)} -> rehydrate from disk failed for {request.Uri}: {ex.Message}");
                    }
                    finally
                    {
                        bodyStream?.Dispose();
                    }
                }
                // Fresh 200 with validators — persist to disk
                else if (diskEligible && httpPolicy.PersistToDisk
                         && response != null && response.HasNoErrors
                         && response is IHttpResponseStatusCode statusOk
                         && statusOk.StatusCode == HttpStatusCode.OK)
                {
                    var meta = BuildEntryFromResponse(response);
                    if (meta != null && (meta.ETag != null || meta.LastModified != null))
                    {
                        var bodyText = (response as IHttpResponseBody)?.Body;
                        if (!string.IsNullOrEmpty(bodyText))
                        {
                            var bodyBytes = Encoding.UTF8.GetBytes(bodyText);
                            diskKey ??= DiskCacheKey.Compute(
                                request.RequestMessage.Method,
                                request.Uri,
                                httpPolicy.VaryKey?.Invoke(request));
                            if (diskKey != null)
                            {
                                // fire-and-forget; failures already logged inside the store
                                _ = diskStore.WriteAsync(diskKey, meta, bodyBytes, CancellationToken.None);
                            }
                        }
                    }
                }

                if (cachePolicy != null && response != null && response.HasNoErrors)
                {
                    // in-memory cache
                    PutIntoCache(response.RequestUri.ToString(), response, cachePolicy.Expiration);
                }
            }

            return response;
        }

        public bool GetFromCache(string url, out IHttpResponse data)
        {
            data = null;

            if (_cachedData.TryGetValue(url, out var dataFromCache))
            {
                data = dataFromCache.Data;

                // mark as from cache
                if (data is ICachedHttpResponse cachedResponse)
                {
                    cachedResponse.IsFromCache = true;
                }

                // check if not expired
                if (dataFromCache.Expired)
                {
                    data = null;
                    Invalidate(url);
                }
            }

            return data != null;
        }

        public void PutIntoCache(string url, IHttpResponse data, TimeSpan expireIn)
        {
            if (data == null)
            {
                // log error
                UnityEngine.Debug.LogError($"{nameof(UrlCache)} -> InvalidOperation. Cannot add null data to cache");
                return;
            }

            if (_cachedData.ContainsKey(url))
            {
                if (!_cachedData.TryRemove(url, out CacheData<IHttpResponse> removedData))
                {
                    // log error
                    UnityEngine.Debug.LogError($"{nameof(UrlCache)} -> Couldn't remove previous data from cache for:{url}");
                }
            }

            // TODO: check if cache size is not exceeded
            // log amount left

            if (!_cachedData.TryAdd(url,
                new CacheData<IHttpResponse>(
                    data,
                    expireIn,
                    (data as ICachedHttpResponse)?.CacheContentSize() ?? 0)))
            {
                // log error
                UnityEngine.Debug.LogError($"{nameof(UrlCache)} -> Couldn't add data to cache for:{url}");
            }
        }

        public void Invalidate(string url)
        {
            if (_cachedData.ContainsKey(url))
            {
                if (!_cachedData.TryRemove(url, out CacheData<IHttpResponse> removedData))
                {
                    // log error
                    UnityEngine.Debug.LogError($"{nameof(UrlCache)} -> Couldn't remove data from cache for:{url}");
                }
            }
        }

        public void InvalidateAll()
        {
            _cachedData.Clear();
        }

        private long GetCacheTotalSize()
        {
            long size = 0;

            foreach (var cacheKv in _cachedData)
            {
                size += cacheKv.Value.Size;
            }

            return size;
        }

        // Read ETag / Last-Modified / Content-Type from a live response so we can
        // re-issue a conditional GET later and reconstruct headers on a 304 hit.
        private static DiskCacheEntry BuildEntryFromResponse(IHttpResponse response)
        {
            if (response == null) return null;

            string etag = null;
            string lastModified = null;
            string contentType = null;
            int statusCode = 0;

            if (response is IHttpResponseStatusCode status) statusCode = (int)status.StatusCode;

            if (response.Headers != null)
            {
                response.Headers.TryGetValue("ETag", out etag);
                if (etag == null) response.Headers.TryGetValue("Etag", out etag);
                response.Headers.TryGetValue("Last-Modified", out lastModified);
            }
            if (response.ContentHeaders != null)
            {
                response.ContentHeaders.TryGetValue("Content-Type", out contentType);
                // Last-Modified can land on either headers dict depending on server
                if (lastModified == null) response.ContentHeaders.TryGetValue("Last-Modified", out lastModified);
            }

            if (etag == null && lastModified == null) return null;

            var selected = new Dictionary<string, string>();
            if (etag != null) selected["ETag"] = etag;
            if (lastModified != null) selected["Last-Modified"] = lastModified;
            if (contentType != null) selected["Content-Type"] = contentType;

            return new DiskCacheEntry
            {
                ETag = etag,
                LastModified = lastModified,
                ContentType = contentType,
                StatusCode = statusCode,
                SelectedHeaders = selected,
            };
        }
    }
}
