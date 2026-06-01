using System.Threading.Tasks;

namespace ApiClient.Runtime.Streaming
{
    /// <summary>
    /// Strategy for framing a streamed HTTP response body into discrete JSON messages.
    /// Implementations own the read loop and report each framed message, framing-level
    /// parsing errors and read progress through the supplied <see cref="StreamMessageReadContext"/>.
    /// Framing (for example Server-Sent Events versus newline-delimited JSON) is the only
    /// concern of an implementation; deserialization and response dispatch are handled by
    /// the transport via the context callbacks.
    /// </summary>
    public interface IStreamMessageReader
    {
        Task ReadAsync(StreamMessageReadContext context);
    }
}
