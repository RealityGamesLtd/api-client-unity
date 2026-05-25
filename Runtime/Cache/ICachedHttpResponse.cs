namespace ApiClient.Runtime.Cache
{
    public interface ICachedHttpResponse
    {
        bool IsFromCache { get; internal set; }

        /// <summary>
        /// True when the response was hydrated from disk because the origin replied
        /// 304 Not Modified to a conditional GET. <see cref="IsFromCache"/> is also
        /// true in that case; the distinction is useful for telemetry (in-memory hit
        /// vs disk-validated hit).
        /// </summary>
        bool IsConditionalHit { get; internal set; }

        long CacheContentSize();
    }
}
