using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ApiClient.Runtime.HttpResponses;
using ApiClient.Runtime.Requests;

namespace ApiClient.Runtime.Cache
{
    public class UrlCache
    {
        private readonly ConcurrentDictionary<string, CacheData<IHttpResponse>> _cachedData = new();
        private readonly List<Regex> _rules = new();

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
        /// <returns>Response either from cache or from the server</returns>
        public async Task<IHttpResponse> Process(
            IHttpRequest request, 
            CachePolicy cachePolicy, 
            Func<Task<IHttpResponse>> continuationAction)
        {
            IHttpResponse response = null;

            // override
            if (cachePolicy != null && cachePolicy.ForceExpire)
            {
                Invalidate(request.Uri.ToString());
            }
            // check cache
            else if (GetFromCache(request.Uri.ToString(), out IHttpResponse cachedResponse))
            {
                response = cachedResponse;
            }

            // make request
            if (response == null)
            {
                // didn't get cached response -> we should make a request
                response = await continuationAction.Invoke();

                if (cachePolicy != null && response.HasNoErrors)
                {
                    // response should be cached
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

            // check if cache size is not exceeded
            // log amount left

            if (!_cachedData.TryAdd(url,
                new CacheData<IHttpResponse>(
                    data,
                    expireIn,
                    (data as ICachedHttpResponse).CacheContentSize())))
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
    }
}