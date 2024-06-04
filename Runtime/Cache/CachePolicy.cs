using System;

namespace ApiClient.Runtime.Cache
{
    public class CachePolicy
    {
        /// <summary>
        /// After what time cashed data should be expired
        /// </summary>
        public TimeSpan Expiration { get; set; } = TimeSpan.FromMinutes(60);

        /// <summary>
        /// Should ignore <see cref="Expiration"/> and override previously stored data
        /// </summary>
        public bool OverridePrevious { get; set; } = false;
    }
}