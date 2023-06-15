namespace ApiClient.Runtime.HttpResponses
{
    public class ResponseInfo
    {
        public ResponseInfo(string id, IHttpResponse response)
        {
            Id = id;
            Response = response;
        }

        public string Id { get; }
        public IHttpResponse Response { get; }
    }
}