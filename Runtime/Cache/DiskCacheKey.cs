using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace ApiClient.Runtime.Cache
{
    /// <summary>
    /// Deterministic key derivation for the disk cache. Hash of
    /// <c>method + "\n" + url + "\n" + varyKey</c>. SHA-256 hex.
    /// </summary>
    public static class DiskCacheKey
    {
        public static string Compute(HttpMethod method, Uri uri, string varyKey)
        {
            if (uri == null) return null;
            var payload = (method?.Method ?? "GET")
                + "\n" + uri.ToString()
                + "\n" + (varyKey ?? string.Empty);

            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>First two hex chars — used as a directory shard.</summary>
        public static string Shard(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length < 2) return "00";
            return key.Substring(0, 2);
        }
    }
}
