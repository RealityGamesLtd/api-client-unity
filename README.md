# ApiClient based on HttpClient with GraphQl support for Unity

## Prerequisites
Check and install all [DEPENDENCIES](#package_dependencies:) before importing this package to avoid errors.

## Usage
Create instance of `ApiClientConnection`
```csharp
IApiClientConnection apiClientConnecton = new ApiClientConnection(
        new ApiClientOptions()
        {
            GraphQLClientEndpoint = "https://spacex-production.up.railway.app/",
            Timeout = TimeSpan.FromSeconds(10)
        });
```

### Make Stream request
```csharp
var request = apiClientConnecton.CreateGetStreamRequest("http://venue-explorer.monopoly-concept1.r10s.r5y.io/public-stream", _streamRequestCts.Token);
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
var response = await request.Send<ResponseType>();
// process response
// ...
```
More detailed informations about creating queries [HERE](GraphQLQueryBuilder/README.md)

### Set Default Headers
```csharp
apiClientConnecton.SetDefaultHeader(key, value);
```

## Error handling
Handling errors is as easy as checking `HasNoErrors`. 
It will return false if any error occured while making request.
`IsAborted` is also treated as an error so we do not try to process the
response content but don't have to log it as this means that the task was simply
canceled.
```csharp
if (response.HasNoErrors)
{
    var responseContent = response as HttpResponse<ResponseType>;
    // ...
}
else
{
    if (!response.IsAborted)
    {
        Debug.Log("error");
    }
}
```

## Package dependencies:

### NuGetForUnity
https://github.com/GlitchEnzo/NuGetForUnity

### GrahpQL Client
https://github.com/graphql-dotnet/graphql-client

### Polly
https://github.com/App-vNext/Polly

### Newtonsoft.Json-for-Unity
https://github.com/jilleJr/Newtonsoft.Json-for-Unity