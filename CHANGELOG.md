# Changelog
All notable changes to this project will be documented in this file.

## [1.4.0]
### Add
- Gameplay vs asset priority lane. New `RequestPriorityCoordinator` (in `ApiClient.Runtime.Priority`) gives gameplay HTTP traffic priority over bulk byte-array (asset) downloads on bandwidth-constrained networks (notably 3G).
- New `ApiClientOptions.PriorityCoordinator`, `ApiClientOptions.RangeDownload` and `ApiClientOptions.Lane` options. Default off — `PriorityCoordinator = null` keeps the legacy single-pool behaviour and is fully back-compat.
- When the coordinator is configured, `SendByteArrayRequest` runs through a dedicated `_assetHttpClient` (separate connection pool, `AutomaticDecompression = None`) and uses HTTP `Range` chunked downloads. Asset workers gate between chunks on `WaitForGameplayIdleAsync` so a long asset transfer pauses while gameplay requests are in flight.
- Per-chunk retry inside the chunked path so a transient packet loss mid-transfer doesn't throw away the bytes already received. Outer Polly retry still applies on exhaustion.
- Graceful fallback: if the server returns `200` to the Range probe (Range not honoured) or returns `200` mid-transfer, the download falls back to a single-GET drain (with optional gate-between-buffer-reads).
- `ApiClientConnection` two-instance constructor: separate gameplay and asset `IApiClient` instances sharing one `RequestPriorityCoordinator`.
- `ApiClientLane` enum (`Mixed | Gameplay | Asset`) for advanced topologies.
- `PriorityCoordinatorTests` covering coordinator semantics, fairness ceiling and bulkhead.

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
