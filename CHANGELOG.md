# Changelog
All notable changes to this project will be documented in this file.

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
