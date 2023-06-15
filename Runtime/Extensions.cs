using System;
using ApiClient.Runtime.HttpResponses;

namespace ApiClient.Runtime
{
    public static class Extensions
    {
        public static void InvokeOnMainThread(this Action<IHttpResponse> responseCallback, IHttpResponse response)
        {
            ThreadDispatcher.RunOnMainThread(() =>
            {
                responseCallback?.Invoke(response);
            });
        }
    }
}