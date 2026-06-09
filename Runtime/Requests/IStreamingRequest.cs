namespace ApiClient.Runtime.Requests
{
    /// <summary>
    /// Marks a request whose response body is consumed progressively as a stream rather
    /// than buffered into a single response. Lets middleware and instrumentation treat all
    /// streaming requests uniformly, independent of payload type or wire framing.
    /// </summary>
    public interface IStreamingRequest : IHttpRequest
    {
    }
}
