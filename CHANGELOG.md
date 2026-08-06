# Changelog
All notable changes to this project will be documented in this file.

## [2.2.0]
Allocation work driven by the 2026-08-05/08-06 Android deep-profile captures. Mostly internal, but three things are observable at the call site — read **Behavior change** before upgrading.

### Behavior change
- **`HttpResponse<T>.Body` is `null` for NDJSON stream messages.** The reader-emit path (see *Add*) never materialises the message as a string, so there is nothing to hand out. `Content` (the deserialized `T`) is unchanged, and this is the default for streams created by `ApiClientConnection.CreatePutStreamRequest`. Two paths keep the old behavior: `ApiClientOptions.VerboseLogging` (the per-message log needs the text, so `Body` is populated as before), and SSE (`ServerSentEventStreamMessageReader`), which is deliberately untouched because its consumers re-parse the raw body per message type. Consumers reading `Body` off an NDJSON stream must move to `Content`.
- **`new HttpResponse<T>(content, null, null, ...)` no longer compiles (CS0121).** The added `Dictionary<string, string>` header overload (see *Add*) makes bare `null` header arguments ambiguous with the `HttpResponseHeaders`/`HttpContentHeaders` overload. Fix at the call site with a cast: `(HttpResponseHeaders)null` / `(HttpContentHeaders)null`. Only affects code that passes untyped `null` — typed arguments bind as before.
- **`ApiClientOptions.StreamBufferSize` default raised 4096 → 16384 chars.** It is the char block the stream reader loop frames messages from; each iteration is one `StreamReader.ReadAsync`, which Mono wraps in a timeout (linked `CancellationTokenSource` + `Task.Delay` + `WhenAny`). Larger blocks cut that per-read machinery proportionally during message waves. Set it back explicitly if the old size mattered.
- **`CreatePutStreamRequest` gains a trailing `allowCompressedResponse` parameter (default `false`).** Passing `true` routes the stream through a third `HttpClient` whose handler decompresses — Mono derives `Accept-Encoding` from the *handler's* `AutomaticDecompression` at send time, which is why streams were excluded when gzip landed for regular requests (#11) and why this needs a separate client rather than a per-request header. NDJSON gzips ~8–10x on the wire. It is off by default because it is not a free win: a proxy that buffers gzip output holds messages back until the response ends (so SSE must never set it), and on Mono it *raises* GC pressure — `DeflateStreamNative.UnmanagedRead` pulls the base stream through its own `new byte[4096]`, and every `SslStream` read costs ~33 KB of garbage regardless of size, so a decompressed body pays that toll per 4 KB of ciphertext instead of per ~16 KB record. Enable per call site and measure both delivery shape and allocation.

### Add
- Stream reader-emit path: `StreamMessageReadContext` gains an optional `Func<FramedMessageTextReader, Task>` emit callback plus `SupportsReaderEmit`, and `NewlineDelimitedJsonStreamMessageReader` prefers it when present — a framed NDJSON line is handed to the transport as a reusable `TextReader` over the framing buffers (`CharSegmentTextReader` over the read chunk, `StringBuilderTextReader` over the reassembly builder) instead of being materialised as a string. `ApiClient.SendStreamRequest` deserializes that reader with a per-thread cached `JsonSerializer` (`CheckAdditionalContent` on, matching `JsonConvert.DeserializeObject`) through a `JsonTextReader` whose char buffers are recycled per thread (`JsonCharArrayPool`). NDJSON lines have reached 845 KB; on this path the per-message string, and the `StringBuilder.ToString` behind it, no longer exist.
- `HttpClientStreamRequest<T>.AllowCompressedResponse` (default `false`): opts a stream into the decompressing `HttpClient`. Only safe for bulk-transfer streams; see *Behavior change* for why SSE must not set it.
- `HttpResponse<T>` constructor taking already-flattened `Dictionary<string, string>` headers and content headers, so a stream can flatten its response headers once for its lifetime instead of once per message (24.6k allocations in the 2026-08-05 capture). The dictionaries are stored as given, not copied — a caller sharing one across responses must treat it as read-only from then on. Nothing in the library mutates them.
- `FramedMessageTextReader` (`Runtime/Streaming/`) and `JsonCharArrayPool` (`Runtime/Auxiliary/`).

### Change
- NDJSON framing no longer appends character by character: it scans newline to newline and bulk-appends the span, and a line that lies wholly inside one read chunk bypasses the `StringBuilder` entirely — no `ToString`, no `Clear`. Trimming is applied to the builder's bounds so one string is produced where `ToString().Trim()` produced two. An oversized builder is replaced with one sized to the message just emitted rather than cleared, because Mono's `StringBuilder` is copy-on-write: `ToString` marks the buffer shared, so the next mutation — including `Clear()` — reallocates the whole capacity. On the reader-emit path there is no `ToString`, so the builder is kept at peak capacity instead. The line builder now starts at 8 KB rather than doubling up from 16 on every stream. 8.4 MB of the 185 MB in the 2026-08-05 capture.
- `ServerSentEventStreamMessageReader` no longer materialises each read chunk as a string just to test `EndsWith("\n\n")`; it checks the char array and builds the string only when a message is actually complete.
- Header flattening is LINQ-free. `ToHeadersDictionary` (called twice per response constructor, plus once per stream message) uses a manual loop with a single-value fast path, so a header carrying one value — virtually all of them — returns that string instead of a joined copy; verified equivalent to `string.Join` for the 0/1/n/null-element cases. `GetHeader` walks the sequence once instead of twice (`Count()` then `ElementAt(0)`, each allocating an enumerator). The three duplicated request-side header getters route through the same helper. The runtime assembly no longer references `System.Linq`.
- Stream response headers are flattened once per stream instead of per message. Content headers stay per-message on purpose: the SSE reader rewrites `ContentLength` on the shared response message for each message it frames.
- `StreamReader` is constructed with an explicit 64 KB read buffer (it previously took the 1024-byte default). Every refill is one `SslStream.ReadAsync`, and Mono's `MobileAuthenticatedStream.StartOperation` calls `readBuffer.Reset()` twice per operation — once up front, once in its `finally` — where `Reset()` unconditionally does `Buffer = new byte[InitialSize]` (16500 for reads). So each TLS read costs ~33 KB of garbage no matter how many bytes it returns, which made this the single largest allocator in the app: 29.7 MB over 2440 reads (22% of all bytes) in the 2026-08-05 capture, 48 MB of 238 MB in the 2026-08-06 10:50 one. Explicitly *not* addressed here: the bucket tracks the READ-OPERATION COUNT (one op yields at most one record), not our read size — measured unchanged going from 1 KB to 16 KB refills. Cutting it needs fewer arrivals per byte: less wire volume, fewer requests, no 4 KB-at-a-time decompression on the socket, or a transport that is not Mono-managed TLS at all.
- The three request profiler markers no longer interpolate the URI into the sample name. That grew the profiler's marker registry without bound on device and formatted a string per attempt; endpoint breakdown comes from per-host counters in the consumer's middleware.
- `ChunkedByteArrayDownloadAsync` built its `MemoryStream` with capacity == `totalLength`, filled it exactly, then called `ToArray()` — a second full-size allocation that doubled peak memory for the transfer. It now hands over the internal buffer when the fit is exact and copies only when it is not.
- Byte-array drain (`DrainResponseToByteArrayResponseAsync`) allocation profile: the assembly `MemoryStream` is presized from `Content-Length`, the 64 KB read buffer is rented from `ArrayPool<byte>.Shared`, and when the body fills the presized capacity exactly the internal buffer is handed over as the response instead of a full-size `ToArray()` copy — the exact-fit pattern the chunked Range path already used. Behavioral surface is unchanged: a missing or wrong `Content-Length` only reproduces the previous grow-and-copy behavior. 9.9 MB across the 2026-08-06 capture (measured 9.9 → 3.1 MB on the 2026-08-06 10:50 capture, and the residual is payload plus the TLS layer underneath).
- Both assembly-buffer presizes are capped at 16 MB (`MaxPresizeBytes`): the drain's `Content-Length` and `ChunkedByteArrayDownloadAsync`'s `Content-Range` total, which was previously clamped only at `int.MaxValue`. Real bodies — map tiles, JSON payloads — sit orders of magnitude below the cap, so the presize and exact-fit handover are unchanged for them, but no single header can now force a huge up-front allocation on a phone. A body that genuinely exceeds the cap still completes; it grows the stream as before.

### Fix
- `StreamMessageReadContext.EmitMessageAsync(FramedMessageTextReader)` throws `InvalidOperationException` naming the invariant when the transport has no reader-emit callback, instead of a `NullReferenceException` from the null delegate. `SupportsReaderEmit` remains the check a reader is expected to make, and the `string` overload is always available as the fallback.
- Profiler `Begin`/`EndSample` pairs no longer span awaits. Five markers had an `await` between the pair, so any continuation crossing a frame on the main thread logged the Missing/Non-matching `EndSample` error pair and made the marker's timing meaningless. `"Api Client Execute Request [E]"`, `"Api Client Execute Request"` and `"Api Client Execute Stream Request"` wrapped whole HTTP round-trips (the stream one: the stream's entire lifetime) and are removed — wire timing is already measured by stopwatch and the synchronous parse work keeps its own markers. `"Api Client Body Read"` now reads the in-memory body synchronously inside the sample (the stream is a memory buffer; nothing blocks). The SSE reader's `"Api Client Stream Regex Extraction"` emits its parsing error after the sample closes instead of awaiting inside the `catch`.

### Note
- A failed deserialize on the reader-emit path still reports raw text: the framed reader re-materialises a bounded snapshot (first 4096 chars) for `ParsingErrorHttpResponse`, taken only after the throw.
- `ApiClient` now owns a third `HttpClient`/`HttpClientHandler` pair (the decompressing stream client), disposed with the others.

## [2.1.0]
### Add
- `RequestTimingSample.NetworkDuration`: pure wire round-trip time of the final `HttpClient.SendAsync` attempt, measured on the ThreadPool inside the send's `Task.Run`. Unlike `Duration` (which brackets the whole `SendHttp*` call and therefore includes Task scheduling, middleware, inter-retry Polly backoff, and — critically — the `SynchronizationContext` post-back to the caller), `NetworkDuration` excludes all caller-thread scheduling latency, so a stalled/janky caller thread no longer inflates it. Consumers driving a connection-quality/latency EWMA should prefer `NetworkDuration` over `Duration`. `TimeSpan.Zero` when no send attempt completed (e.g. aborted before the wire call). Additive: `Duration` is unchanged.

### Note
- `RequestTimingSample`'s constructor gains a `networkDuration` parameter (positioned right after `duration`). The struct is only constructed inside the library (`ApiClient.EmitTiming`); external code consumes the sample and is unaffected.

## [2.0.2]
### Changes:
- ApiClient no longer logs request errors (non-success 4xx/5xx status codes). Reporting request outcomes is now the responsibility of the consumer, which can inspect the returned `IHttpResponse` (`StatusCode`, `IsClientError`/`IsServerError`). No change to default behaviour: request-error logging was already gated behind `ApiClientOptions.VerboseLogging` (off by default). Other verbose diagnostics (download progress, stream messages, range-download warnings) are unchanged.

## [2.0.1]
### Changes:
- Added IStreamMessageReader + StreamMessageReadContext to decouple stream framing from ApiClient.SendStreamRequest.
- Implemented NDJSON framing (NewlineDelimitedJsonStreamMessageReader) with unit tests.
- Added CreatePutStreamRequest<T> that sends a JSON PUT and reads the response as NDJSON; HttpClientStreamRequest<T> now carries a MessageReader strategy.

## [2.0.0]
### Breaking
- **Library no longer forces caller continuations onto the Unity main thread.** `ReturnOnSyncContext` (the per-response `SynchronizationContext.Post` hop) and the `_syncCtx` field were removed. Continuations now resume on whatever thread the caller's `await` captures — exactly the standard .NET async/await contract. Code that awaits a send from a `MonoBehaviour` method (already on main) keeps working unchanged. Code that awaits from a pool/background context and then touches Unity objects must dispatch to main explicitly.
- **`OnStreamResponse` and `readDelta` callbacks now fire on the pool thread** that read the bytes — no library-side `Post` to main. Host UI bindings to stream responses must marshal to main themselves.
- **Byte-array `progressCallback` invocations are now (a) synchronous on the pool thread and (b) throttled.** Default throttle: at most one callback per 64 KB of progress OR per 100 ms (whichever crosses first); first and final callbacks always fire. For a 1 MB tile this collapses ~256 main-thread posts into ~16 pool-thread invokes. Tune via the new `ApiClientOptions.ProgressReportThresholdBytes` (default 65536) and `ApiClientOptions.ProgressReportThrottleMs` (default 100).
- **Default `ByteArrayBufferSize` raised from 4 KB → 64 KB.** Cuts `ReadAsync` iterations 16× per download; lower allocation pressure on Android. Override via `ApiClientOptions.ByteArrayBufferSize` if the old value mattered.
- **`Extensions.PostOnMainThread<T>` extension removed** (`Runtime/Auxiliary/Extensions.cs`). It had no users outside the library.

### Migration
Host code that previously relied on response/progress/stream callbacks landing on the Unity main thread should wrap with its own dispatcher (e.g. a `UnityMainThreadDispatcher.Enqueue` call) at the use site, OR await the send from a method that is already running on main. The library no longer makes this guarantee on the caller's behalf.

### Improvement
- `UrlCache.Process` now `await`s its continuation with `ConfigureAwait(false)` for consistency with the rest of the package's no-context-capture policy. No behaviour change in practice (the call already runs inside `Task.Run`).

## [1.4.1]
### Improvement
- Priority bulkhead slot and lane scope are now acquired per Polly retry attempt and released between attempts. Previously the handshake was held across the entire retry chain, so backoff sleeps on transient infra codes (408/500/502/504) and 401 retries kept the bulkhead slot occupied and the lane marked in-flight while no HTTP I/O was running — stalling cross-lane traffic and any same-lane queued requests. Chunked Range downloads still hold the slot continuously across all chunks within one attempt; release only happens between attempts. Note: a request that retries must re-queue on its lane's bulkhead per attempt (FIFO-ish), so fresh requests on the same lane can interleave between retries.
- New `PriorityRetryInteractionTests` fixture exercises the per-attempt handshake via a localhost `HttpListener`: slot released during backoff, slot held across Range chunks within an attempt, no slot leak on cancellation mid-backoff, and Polly context flow (auth-header swap) unaffected by the move.

## [1.4.0]
### Add
- Per-request timing hook. New `IApiClient.OnRequestCompleted` event fires once per `SendHttpRequest*` and `SendHttpHeadersRequest` call with a `RequestTimingSample` (duration, success/abort/timeout/network classification, cache hit flag, HTTP status, priority lane). Lets consumers drive a connection-quality classifier (EWMA etc.) for bandwidth-throttled networks where SSE heartbeats stay healthy but full HTTP calls stretch into hundreds of milliseconds. Byte-array and stream sends are intentionally not instrumented — their durations are bandwidth-/lifetime-bound and would corrupt RTT signals.
- Domain-neutral priority lanes. New `RequestPriorityCoordinator` (in `ApiClient.Runtime.Priority`) lets the caller define lanes (caller-named string ids) with per-lane concurrency caps, yield-to-other-lanes relationships, fairness ceilings, and an opt-in chunked-Range download path. The library coordinates without assuming any meaning for lane labels — gameplay/asset/telemetry/etc. are entirely a caller convention.
- `LaneConfig` describes one lane: `Id`, `MaxConcurrent`, `YieldsTo`, `FairnessMaxPause`, `ChunkedRangeDownloads`. Coordinator validates duplicates, unknown `YieldsTo` targets, and cycles at construction time.
- `ApiClientOptions.PriorityCoordinator` (default `null`) opts in. `ApiClientOptions.RangeDownload` configures the chunked path. New `ApiClientOptions.AutomaticDecompression` exposes the underlying handler's decompression policy (set to `None` on instances that service `ChunkedRangeDownloads = true` lanes — Range over a gzipped entity is undefined).
- Per-request priority tagging: every `IApiClientConnection.Create*` method gains an optional `string priorityLane = null` parameter. The id is stamped onto the request as `IHttpRequest.PriorityLane`; when non-null the executor acquires a slot, awaits yielded-to lanes idle, and registers the request as in-flight on its lane.
- `ApiClientConnection` accepts an optional `IReadOnlyDictionary<string, IApiClient> laneRouting` map. When the request's lane is keyed in the map, the request is dispatched through the mapped client; otherwise it falls back to the default. Pool isolation across lanes becomes a caller concern (compose with multiple `ApiClient` instances sharing one coordinator).
- Chunked HTTP `Range` download path with per-chunk retries, mid-transfer fallback to a full GET when the server stops honouring `Range`, and gate-between-chunks preemption so a higher-priority lane becoming busy yields radio bandwidth back. Synthesises a final `200 OK` with assembled `Content-Length` so the URL cache stores responses normally.
- `PriorityCoordinatorTests` covering construction validation, multi-lane chains, bulkhead, fairness ceiling, cancellation, and disposal semantics.

### Removed
- The earlier (unreleased) policy-leaky API: `ApiClientLane` enum, `RequestPriorityCoordinator.EnterGameplay`/`GameplayScope`/`GameplayInFlight`, `AcquireAssetSlotAsync`, `_assetHttpClient`, `ApiClientConnection.AssetAPIClient` and the two-instance gameplay/asset constructor, `PriorityCoordinatorOptions`, and the `RangeChunkedDownloadOptions.UseRangeRequests` flag.

## [1.3.4]
- When valid SSE message is received, the IApiClientMiddleware.ProcessResponse will not be invoked

## [1.3.3]
- Make IApiClient disposable and add Dispose() implementations to ApiClient and the example mock.
- Construct HttpClient with a configured HttpClientHandler enabling automatic gzip/deflate decompression.
- Remove the manual gzip stream wrapper (PrepareJsonStream) and read/deserialize directly from the provided streams.

## [1.3.1]
- Changed all catch (OperationCanceledException) blocks to catch (TaskCanceledException) throughout the file
- Removed CancellationToken parameter and cancellation registration logic from ReturnOnSyncContext method
- Added HandleTaskContinuation extension methods (generic and non-generic) to log task exceptions
- Applied HandleTaskContinuation calls to various async operations in SendHttpRequest<T, E> method only

## [1.3.0]
### Add
- Stream deserialization support for automatic parsing of streamed data
- Possibility to use multiple retry policies
- Header request support for retrieving server headers separately
- CountingStream utility for tracking received bytes
- Comprehensive test suite with ApiClientHelperTests and ExtensionsTests
### Improvement
- Better threading solution with separate threads for all requests
- Removed GraphQL support for a lighter package footprint
- Refactored ApiClient with improved code structure and maintainability
- Updated package dependencies

## [1.2.2]
### Add
- Added gathering stats for compressed and uncompressed received bytes in IApiClient

## [1.2.1]
### Add
- IHttpResponse will now expose HttpMethod along with Uri

## [1.2.0]
### Add
- Added support for gzip compression for non-stream requests

## [1.1.5]
### Fix
- Custom headers missing on re-create fix
### Improvement
- Changed IsSent assignment

## [1.1.4]
### Improvement
- Added more detailed logs for exception messages

## [1.1.3]
### Fix
- throw TaskCanceledException when cancellation token is canceled after data read has been started

## [1.1.2]
### Fix
- removed regex unescaping of SSE message body that led to parsing errors on unescaped JSON

## [1.1.1] - 2024-09-06
### Fix
- added more verbose logging and restored missing UpdateReadDeltaValueTask

## [1.1.0] - 2024-09-06
### Add
- Basic cache system
- ByteArray requests support
### Fix
- Changed what do we store in Response and Request objects and how we re-creating
requests - this is related to unexpected Timeouts

## [1.0.10] - 2024-07-09
### Improvement
- Removed unescape-ing from stream response processing

## [1.0.10] - 2024-07-09
### Add
- Dedicated task for stream read delta

## [1.0.9] - 2024-07-09
### Add
- Read delta for stream

## [1.0.8] - 2024-03-21
### Improvement
- Added `headers` argument in ApiClientConnection helper methods

## [1.0.7] - 2024-03-20
### Improvement
- Setting Http version by ApiClientOptions
- Refactor and improved naming

## [1.0.6] - 2024-02-02
### Improvement
- Removed obsolete UserFacingErrorMessage from ResponseWithContent

## [1.0.5] - 2024-01-30
### Added
- support for http 2.0
### Fix
- Incorrect propagation of internal server error

## [1.0.4] - 2023-11-15
### Improvement
- Combining stream messages when received in chunks

## [1.0.3] - 2023-06-15
### Improvement
- Stream cancelling in editor when loosing focus or while exiting play mode has been improved

## [1.0.0] - 2023-06-15
### Fix

### Added
