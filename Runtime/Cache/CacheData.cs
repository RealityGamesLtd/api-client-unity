using System;
using ApiClient.Runtime.HttpResponses;

public class CacheData<T> where T : IHttpResponse
{
    public T Data { get; }
    public DateTime ExpiryDateTime { get; }

    public bool Expired
    {
        get
        {
            return ExpiryDateTime < DateTime.UtcNow;
        }
    }

    public CacheData(T data, TimeSpan expiry)
    {
        Data = data;
        ExpiryDateTime = DateTime.UtcNow + expiry;
    }
}
