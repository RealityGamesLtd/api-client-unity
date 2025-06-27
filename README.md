# ApiClient with REST.API & GraphQl support for Unity

## Prerequisites
Check and install all [DEPENDENCIES](#dependencies) before importing this package to avoid errors.

## Usage
Create instance of `ApiClientConnection`
```csharp
IApiClientConnection apiClientConnecton = new ApiClientConnection(
        new ApiClientOptions()
        {
            GraphQLClientEndpoint = "url",
            Timeout = TimeSpan.FromSeconds(10)
        });
```

### Make REST.API request
```csharp
var request = apiClientConnecton.CreatePost<T>("url", null, cts.Token);
var httpResponse = await request.Send();
// process response
// ...
```
Other REST.API variations can be created this way

#### Get
```csharp
var request = apiClientConnecton.CreateGet<T>("url", cts.Token);
```
or
```csharp
var request = apiClientConnecton.CreateGet("url", cts.Token);
```
#### Post
```csharp
var request = apiClientConnecton.CreatePost<T>("url", "body", cts.Token);
```
or
```csharp
var request = apiClientConnecton.CreatePost("url", "body", cts.Token);
```
#### Put
```csharp
var request = apiClientConnecton.CreatePut<T>("url", "body", cts.Token);
```
or
```csharp
var request = apiClientConnecton.CreatePut("url", "body", cts.Token);
```
#### Delete
```csharp
var request = apiClientConnecton.CreateDelete<T>("url", cts.Token);
```
or
```csharp
var request = apiClientConnecton.CreateDelete("url", cts.Token);
```

### Make Stream request
```csharp
var request = apiClientConnecton.CreateGetStreamRequest("url, cts.Token);
await request.Send<StreamData>(streamResponse =>
{
    // process response
    // ...
});
```

### Make GraphQL request with query
By using Query Builder
More detailed informations about creating queries [HERE](GraphQLQueryBuilder/README.md).
```csharp
var query = new Query()
    .Name("__type")
    .Select("name")
    .Where("name", "users");
var request = apiClientConnecton.CreateGraphQLRequest(query, _cts.Token);
var httpResponse = await request.Send<ResponseType>();
// process response
// ...
```

or by using query string

```csharp
var request = apiClientConnecton.CreateGraphQLRequest("query {__type(name:\"users\"){name}}", _cts.Token);
```
or by using query string with variables
```csharp
var variables = new {
        name = "cGVvcGxlOjE="
    };
var request = apiClientConnecton.CreateGraphQLRequest("($name: String!) {__type(name: $name) { name }}", variables, _cts.Token);
```

### Set Default Headers
All requests will be using default headers by default.

Add new default header
```csharp
apiClientConnecton.SetDefaultHeader(key, value);
```

Sometimes though it's not desirable to make requests with them.
It can be achieved by setting `useDefaultHeaders` to false while creating
a request.
 Add new default header
```csharp
var request = _apiClientConnecton.CreateGet<T>("url", cts.Token, useDefaultHeaders: false);
```

## Error handling
Handling errors is as easy as checking `HasNoErrors`. 
It will return false if any error occured while making request.
`IsAborted` is also treated as an error so wont try to process the
response content. 
We don't have to log it or present it to users as this means that 
the task was simply canceled.
```csharp
if (httpResponse.HasNoErrors)
{
    var responseContent = httpResponse as HttpResponse<ResponseType>;
    // ...
}
else
{
    if (!httpResponse.IsAborted)
    {
        Debug.Log("error");
    }
}
```

## Error propagation
In order to show the error to users in a more accessible, valuable and friendly manner
with e.g. translated error messages or by not displaying error logs we can use a 
`Response` wrapper class.
```csharp
var httpResponse = await request.Send();
var response = new ResponseWithContent<ResponseType, ResponseErrorCode>(httpResponse);
```
then propagate the error
```csharp
if (httpResponse.HasNoErrors)
{
    var responseContent = httpResponse as HttpResponse<ResponseType>;
    // ...
}
else
{
    if (!httpResponse.IsAborted)
    {
        if (httpResponse is NetworkErrorHttpResponse ner)
        {
            response.SetError(ResponseErrorCode.Unknown, "User friendly Network Error message");
        }
        else if (httpResponse is TimeoutHttpResponse t)
        {
            response.SetError(ResponseErrorCode.Unknown, "User friendly Timeout Error message");
        }
        else if (httpResponse is ParsingErrorHttpResponse per)
        {
            response.SetError(ResponseErrorCode.Unknown, "User friendly Content Parsing Error message");
        }
    }
}
```

## Retry policy
By default each request will be sent only once.
To specify the exact rules of how the retry policy should
look like pass custom policy when creating `IApiClientConnection` instance.
```csharp
private static HttpStatusCode[] httpStatusCodesWorthRetrying = {
            HttpStatusCode.RequestTimeout, // 408
            HttpStatusCode.InternalServerError, // 500
            HttpStatusCode.BadGateway, // 502
            HttpStatusCode.GatewayTimeout // 504
        };

private readonly IApiClientConnection apiClientConnecton = new ApiClientConnection(
    new ApiClientOptions()
    {
        GraphQLClientEndpoint = "url",
        Timeout = TimeSpan.FromSeconds(10),
        RetryPolicy = Policy
            .Handle<HttpRequestException>()
            .OrResult<IHttpResponse>(r =>
            {
                var validStatusCode = false;
                if (r is IHttpResponseStatusCode responseWithStatusCode)
                {
                    validStatusCode = httpStatusCodesWorthRetrying.Contains(responseWithStatusCode.StatusCode);
                }
                return r.IsTimeout ||
                    r.IsNetworkError ||
                    validStatusCode;
            })
            // Exponential Backoff
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (response, timeSpan) =>
                {
                    // Logic to be executed before each retry
                }),
    });
```

## Authentication
Authentication can be passed as `AuthenticationHeaderValue` when creating request via
IApiClientConnection instance.
```csharp
var authentication = new AuthenticationHeaderValue("Bearer", "Token");
var request = _apiClientConnecton.CreateGet<T>("url", cts.Token, authentication: authentication);
```

## GZip Compression
GZip compression is supported by default. To enable it, set the `Accept-Encoding: gzip` header as a default header in the `ApiClientConnection` instance.

```csharp
var apiClientConnecton = new ApiClientConnection(...);
apiClientConnecton.SetDefaultHeader("Accept-Encoding", "gzip");
```

## Troubleshoot 
When using IL2CPP and ManagedStrippingLevel unused types won't be compiled.
To fix that there is an utility class to enforce ahead of time (AOT) compilation of types.
More info here: [INFO](https://github.com/jilleJr/Newtonsoft.Json-for-Unity/wiki/Reference-Newtonsoft.Json.Utilities.AotHelper)
```csharp
// call from any monobehaviout object to ensure types preservation.
AotEnsureTypes.EnsureTypes();
```

## Dependencies

### NuGetForUnity
https://github.com/GlitchEnzo/NuGetForUnity

### GrahpQL Client
https://github.com/graphql-dotnet/graphql-client

### Polly
https://github.com/App-vNext/Polly

### Polly WaitAndRetry
https://github.com/Polly-Contrib/Polly.Contrib.WaitAndRetry

### Newtonsoft.Json-for-Unity
https://github.com/jilleJr/Newtonsoft.Json-for-Unity