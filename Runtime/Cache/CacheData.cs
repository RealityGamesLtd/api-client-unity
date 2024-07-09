using System;
using ApiClient.Runtime.HttpResponses;

public class CacheData<T> where T : IHttpResponse
{
    public T Data { get; }
    public DateTime ExpiryDateTime { get; }
    public long Size { get; }

    public bool Expired
    {
        get
        {
            return ExpiryDateTime < DateTime.UtcNow;
        }
    }

    public CacheData(T data, TimeSpan expiry, long size)
    {
        Data = data;
        ExpiryDateTime = DateTime.UtcNow + expiry;
        Size = size;
    }
}
