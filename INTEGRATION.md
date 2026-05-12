# ApiClient 1.4.0 — Integration Guide

How to wire the priority lane feature into a consuming Unity project.

## 1. Bump package

`Packages/manifest.json`:

```json
"com.realitygames.apiclient": "1.4.0"
```

Or via OpenUPM CLI: `openupm add com.realitygames.apiclient@1.4.0`.

## 2. Decide your lanes

Pick caller-named lane ids that match your game's traffic types. Example for a mobile game:

| Lane id | Purpose | YieldsTo | MaxConcurrent | ChunkedRange |
|---|---|---|---|---|
| `"gameplay"` | Login, moves, inventory — latency-critical | — | unbounded | false |
| `"ui"` | Background UI hydration, prefetch | `gameplay` | 4 | false |
| `"asset"` | Bundle / sprite / blob downloads | `gameplay`, `ui` | 1 | true |
| `"telemetry"` | Analytics, crash logs | `gameplay`, `ui`, `asset` | 2 | false |

Higher-priority lanes have nothing in `YieldsTo`. Lower-priority lanes list every higher one. The coordinator validates the graph at construction (rejects cycles, unknown ids, duplicates).

Keep ids in **one place** so call sites don't typo:

```csharp
public static class ApiLane
{
    public const string Gameplay  = "gameplay";
    public const string Ui        = "ui";
    public const string Asset     = "asset";
    public const string Telemetry = "telemetry";
}
```

## 3. Pick topology

### Topology A — single client (simplest)

All lanes share one `HttpClient` connection pool. Coordinator serializes; bandwidth contention solved at app level via the gate.

```csharp
var coord = new RequestPriorityCoordinator(new[]
{
    new LaneConfig(ApiLane.Gameplay),
    new LaneConfig(ApiLane.Ui)        { MaxConcurrent = 4, YieldsTo = new[] { ApiLane.Gameplay } },
    new LaneConfig(ApiLane.Asset)     { MaxConcurrent = 1, YieldsTo = new[] { ApiLane.Gameplay, ApiLane.Ui }, ChunkedRangeDownloads = true },
    new LaneConfig(ApiLane.Telemetry) { MaxConcurrent = 2, YieldsTo = new[] { ApiLane.Gameplay, ApiLane.Ui, ApiLane.Asset } },
});

var options = new ApiClientOptions
{
    Timeout = TimeSpan.FromSeconds(15),
    PriorityCoordinator = coord,
    RetryPolicies = BuildRetryPolicy(),
    // NOTE: ChunkedRangeDownloads requires AutomaticDecompression = None.
    // With single-client topology your gameplay/UI calls also lose gzip — usually a
    // dealbreaker. Use Topology B if you want gameplay calls compressed AND chunked
    // Range downloads on assets.
    AutomaticDecompression = DecompressionMethods.None,
};

var apiClient = new ApiClient(options);
var conn = new ApiClientConnection(options, apiClient);
```

### Topology B — split clients (recommended for chunked Range + gzip together)

One `ApiClient` per pool you want isolated. Same coordinator shared. Caller composes via `laneRouting`.

```csharp
var coord = new RequestPriorityCoordinator(new[]
{
    new LaneConfig(ApiLane.Gameplay),
    new LaneConfig(ApiLane.Ui)    { MaxConcurrent = 4, YieldsTo = new[] { ApiLane.Gameplay } },
    new LaneConfig(ApiLane.Asset) { MaxConcurrent = 1, YieldsTo = new[] { ApiLane.Gameplay, ApiLane.Ui }, ChunkedRangeDownloads = true },
});

var gameplayOptions = new ApiClientOptions
{
    Timeout = TimeSpan.FromSeconds(10),
    PriorityCoordinator = coord,
    RetryPolicies = BuildRetryPolicy(),
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,  // gzip on
};
var gameplayClient = new ApiClient(gameplayOptions);

var assetClient = new ApiClient(new ApiClientOptions
{
    Timeout = TimeSpan.FromMinutes(2),
    PriorityCoordinator = coord,
    RetryPolicies = BuildAssetRetryPolicy(),  // keep retries modest — chunked path retries internally
    AutomaticDecompression = DecompressionMethods.None,  // required for Range
    RangeDownload = new RangeChunkedDownloadOptions
    {
        ChunkSizeBytes = 256 * 1024,
        MaxChunkRetries = 3,
    },
});

// Connection is built with the gameplay client as the default; asset lane is routed
// to the dedicated asset client so it gets its own connection pool.
var conn = new ApiClientConnection(
    gameplayOptions,
    defaultApiClient: gameplayClient,
    laneRouting: new Dictionary<string, IApiClient>
    {
        [ApiLane.Gameplay]  = gameplayClient,
        [ApiLane.Ui]        = gameplayClient,
        [ApiLane.Asset]     = assetClient,
        [ApiLane.Telemetry] = gameplayClient,
    });
```

## 4. Tag calls

Add `priorityLane:` arg per `Create*`. Existing un-tagged calls keep legacy behaviour (no coordinator interaction):

```csharp
// Gameplay — fast, gzipped
var login = conn.CreatePost<LoginResp, LoginErr>(
    "/auth/login", body, ct,
    priorityLane: ApiLane.Gameplay);

// UI hydration — yields to gameplay
var leaderboard = conn.CreateGet<LeaderboardResp, ApiErr>(
    "/leaderboard", ct,
    priorityLane: ApiLane.Ui);

// Asset — yields to gameplay+ui, chunked Range
var sprite = conn.CreateGetByteArrayRequest(
    cdnUrl, ct,
    priorityLane: ApiLane.Asset);

// Telemetry — yields to everything
var crash = conn.CreatePost(
    "/crash", payload, ct,
    priorityLane: ApiLane.Telemetry);

// Untagged — back-compat, ignores coordinator entirely
var pingNow = conn.CreateGet("/ping", ct);
```

## 5. Migration from earlier (unreleased) code

If your project was using the prior `gameplay`/`asset` API (1.4.0-pre on `main`), sweep these renames:

| Old | New |
|---|---|
| `coordinator.EnterGameplay()` | `coordinator.EnterLane("gameplay")` |
| `coordinator.GameplayInFlight` | `coordinator.InFlight("gameplay")` |
| `coordinator.AcquireAssetSlotAsync(ct)` | `coordinator.AcquireSlotAsync("asset", ct)` |
| `coordinator.WaitForGameplayIdleAsync(ct, fairness)` | `coordinator.WaitForYieldedLanesIdleAsync("asset", ct)` |
| `GameplayScope` | `LaneScope` |
| `ApiClientLane.Mixed/Gameplay/Asset` | deleted — use `laneRouting` map instead |
| `ApiClientConnection(opts, gameplay, asset)` two-instance ctor | `new ApiClientConnection(opts, gameplayClient, laneRouting: {...})` |
| `conn.AssetAPIClient` | look up via `laneRouting` |
| `RangeChunkedDownloadOptions.UseRangeRequests = true` | `LaneConfig.ChunkedRangeDownloads = true` per lane |
| `ApiClientOptions.Lane = ...` | deleted |

## 6. Helpers worth adding (in your game code)

Reduce per-call boilerplate via extension methods:

```csharp
public static class ApiClientConnectionExtensions
{
    public static HttpClientRequest<T, E> CreateGameplayPost<T, E>(
        this IApiClientConnection conn, string url, string body, CancellationToken ct)
        => conn.CreatePost<T, E>(url, body, ct, priorityLane: ApiLane.Gameplay);

    public static HttpClientByteArrayRequest CreateAssetGet(
        this IApiClientConnection conn, string url, CancellationToken ct, CachePolicy cache = null)
        => conn.CreateGetByteArrayRequest(url, ct, cachePolicy: cache, priorityLane: ApiLane.Asset);

    // …etc
}
```

## 7. Lifetime

Coordinator owns `SemaphoreSlim` per lane. Dispose when shutting down to release waiters:

```csharp
void OnApplicationQuit()
{
    conn = null;
    gameplayClient?.Dispose();
    assetClient?.Dispose();
    coord?.Dispose();   // unblocks any pending WaitForYieldedLanesIdleAsync callers
}
```

Don't dispose the coordinator while requests are in flight — pending `AcquireSlotAsync` will throw `ObjectDisposedException` once awaited.

## 8. Caveats / gotchas

- **`ChunkedRangeDownloads = true` requires the underlying handler's `AutomaticDecompression = None`** on whatever `IApiClient` services that lane. Range over a gzipped entity has undefined byte offsets. Topology B handles this by giving the asset lane its own `ApiClient`.
- **Outer Polly retry on byte-array path** should be ≤1 when chunked — chunked retries per chunk internally; outer retry restarts the whole transfer from byte 0.
- **Stringly-typed lane ids.** Always use the `ApiLane` constants. Coordinator throws `KeyNotFoundException` on unknown id at request time.
- **SSE streams** are **not coordinated**. `priorityLane` on `CreateGetStreamRequest` is **routing-only** — it picks which `IApiClient` services the stream (via the `laneRouting` map) and stamps the request for observability, but `SendStreamRequest` never acquires a bulkhead slot, registers as in-flight, or yields. Stream lifetimes are open-ended; holding a slot or in-flight count for the stream's duration would deadlock other lanes.
- **`PriorityCoordinator` shutdown order:** dispose coordinator AFTER all `ApiClient`s.
- **Cancellation:** `priorityLane`-tagged calls hold their bulkhead slot for the full retry duration. A long-blocked retry pile-up can starve a low-`MaxConcurrent` lane until the calling `CancellationToken` fires.

## 9. Connection-quality signal (optional)

`IApiClient.OnRequestCompleted` fires once per `SendHttp*` / `SendHttpHeadersRequest` call with a `RequestTimingSample`. Feed the duration into an EWMA on the consumer side to detect bandwidth-throttled networks where SSE heartbeats stay healthy but full REST calls stretch.

```csharp
private Action<RequestTimingSample> _rttSink;

void Subscribe(IApiClient client)
{
    _rttSink = sample =>
    {
        // Skip aborts/timeouts/network errors (durations are meaningless) and
        // cache hits (return near-instantly, would drag the EWMA low).
        if (!sample.IsSuccess || sample.IsFromCache) return;

        // Optional: only feed the indicator from the gameplay lane.
        if (sample.PriorityLane != ApiLane.Gameplay) return;

        ConnectionQuality.OnRestRttSample(sample.Duration);
    };
    client.OnRequestCompleted += _rttSink;
}

void Unsubscribe(IApiClient client) => client.OnRequestCompleted -= _rttSink;
```

Notes:
- `Duration` is **user-perceived call time** — includes Polly retry backoff, middleware, and the sync-context post. Not pure network RTT, but it IS what the player feels.
- Subscribers receive the event from whatever thread completed the request — typically Unity's main thread (sync context post), but on exception paths from a worker. Marshal yourself if your handler does Unity API calls.
- Byte-array and stream sends do NOT emit. Bandwidth-bound durations would skew the signal.
- In a multi-client topology (post priority-lane refactor), subscribe to **each** `IApiClient` you care about — typically only the gameplay one.

## 10. Verify on device

1. Throttle with iOS Network Link Conditioner to "3G" (DL 780kbps / UL 330kbps / 100ms RTT).
2. Trigger a heavy asset download (e.g. 10–50 MB blob via `priorityLane: "asset"`).
3. Mid-download, fire a gameplay call (`priorityLane: "gameplay"`).
4. Measure gameplay round-trip vs. baseline (no coordinator). Expect ≥3× reduction in gameplay latency on 3G.
5. Watch the asset progress callback — chunks should pause while gameplay is in flight, resume after.
