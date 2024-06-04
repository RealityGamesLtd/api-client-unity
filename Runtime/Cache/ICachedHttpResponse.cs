namespace ApiClient.Runtime.Cache
{
    public interface ICachedHttpResponse
    {
        bool IsFromCache { get; internal set; }
    }
}