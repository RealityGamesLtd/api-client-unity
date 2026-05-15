# Changelog
All notable changes to this project will be documented in this file.

## [Unreleased]
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
