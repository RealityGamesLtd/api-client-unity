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
var request = _apiClientConnecton.CreatePost<Response<AnonRegisterResponse>>("url", null, _cts.Token);
var httpResponse = await request.Send();
// process response
// ...
```
Other REST.API variations can be created this way

#### Get
```csharp
var request = _apiClientConnecton.CreateGet<T>("url", _cts.Token);
```
or
```csharp
var request = _apiClientConnecton.CreateGet("url", _cts.Token);
```
#### Post
```csharp
var request = _apiClientConnecton.CreatePost<T>("url", "body", _cts.Token);
```
or
```csharp
var request = _apiClientConnecton.CreatePost("url", "body", _cts.Token);
```
#### Put
```csharp
var request = _apiClientConnecton.CreatePut<T>("url", "body", _cts.Token);
```
or
```csharp
var request = _apiClientConnecton.CreatePut("url", "body", _cts.Token);
```
#### Delete
```csharp
var request = _apiClientConnecton.CreateDelete<T>("url", _cts.Token);
```
or
```csharp
var request = _apiClientConnecton.CreateDelete("url", _cts.Token);
```

### Make Stream request
```csharp
var request = apiClientConnecton.CreateGetStreamRequest("url, _streamRequestCts.Token);
await request.Send<StreamData>(streamResponse =>
{
    // process response
    // ...
});
```

### Make GraphQL request with query
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
More detailed informations about creating queries [HERE](GraphQLQueryBuilder/README.md)

or

```csharp
var request = apiClientConnecton.CreateGraphQLRequest("query {__type(name:\"users\"){name}}", _cts.Token);
```
or
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
var request = _apiClientConnecton.CreateGet<T>("url", _cts.Token, useDefaultHeaders: false);
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
```csharp

## Retry policy
By dafault each request will be sent only once.
To specify the exact rules of how the retry policy should
look like pass custom policy when creating `IApiClientConnection` instance.
```csharp
private static HttpStatusCode[] _httpStatusCodesWorthRetrying = {
            HttpStatusCode.RequestTimeout, // 408
            HttpStatusCode.InternalServerError, // 500
            HttpStatusCode.BadGateway, // 502
            HttpStatusCode.GatewayTimeout // 504
        };

private readonly IApiClientConnection _apiClientConnecton = new ApiClientConnection(
    new ApiClientOptions()
    {
        GraphQLClientEndpoint = "https://spacex-production.up.railway.app/",
        Timeout = TimeSpan.FromSeconds(10),
        RetryPolicy = Policy
            .Handle<HttpRequestException>()
            .OrResult<IHttpResponse>(r =>
            {
                var validStatusCode = false;
                if (r is IHttpResponseStatusCode responseWithStatusCode)
                {
                    validStatusCode = _httpStatusCodesWorthRetrying.Contains(responseWithStatusCode.StatusCode);
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
var request = _apiClientConnecton.CreateGet<T>("url", _cts.Token, authentication: authentication);
```


## Dependencies

### NuGetForUnity
https://github.com/GlitchEnzo/NuGetForUnity

### GrahpQL Client
https://github.com/graphql-dotnet/graphql-client

### Polly
https://github.com/App-vNext/Polly

### Newtonsoft.Json-for-Unity
https://github.com/jilleJr/Newtonsoft.Json-for-Unity