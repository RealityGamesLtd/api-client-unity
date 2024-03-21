using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Threading;
using ApiClient.Runtime.GraphQLBuilder;
using ApiClient.Runtime.Requests;

namespace ApiClient.Runtime
{
    public interface IApiClientConnection
    {
        void SetDefaultHeader(string key, string value);
        HttpClientStreamRequest<T> CreateGetStreamRequest<T>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        GraphQLClientRequest<T> CreateGraphQLRequest<T>(IQuery query, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        GraphQLClientRequest<T> CreateGraphQLRequest<T>(string query, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        GraphQLClientRequest<T> CreateGraphQLRequest<T>(string query, object variables, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest<T, E> CreateGet<T, E>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest<T> CreateGet<T>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest CreateGet(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest<T> CreatePost<T>(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest<T, E> CreatePost<T, E>(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest CreatePost(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest<T> CreatePut<T>(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest<T, E> CreatePut<T, E>(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest CreatePut(string url, string jsonBody, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest<T> CreateDelete<T>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest<T, E> CreateDelete<T, E>(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
        HttpClientRequest CreateDelete(string url, CancellationToken ct, AuthenticationHeaderValue authentication = null, Dictionary<string, string> headers = null, bool useDefaultHeaders = true);
    }
}