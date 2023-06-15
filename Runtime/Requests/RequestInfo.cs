namespace ApiClient.Runtime.Requests
{
    public class RequestInfo
    {
        public RequestInfo(string id, IHttpRequest request)
        {
            Id = id;
            Request = request;
        }

        public string Id { get; }
        public IHttpRequest Request { get; }
    }
}