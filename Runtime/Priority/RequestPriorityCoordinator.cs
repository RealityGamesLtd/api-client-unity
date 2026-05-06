using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ApiClient.Runtime.Priority
{
    /// <summary>
    /// Domain-neutral priority coordinator. Caller registers lanes (caller-named) at
    /// construction and tags each request with a lane id; the coordinator provides per-lane
    /// concurrency caps, in-flight counters, and a yield-to-other-lanes wait primitive.
    ///
    /// The library does not assume any meaning for lane ids — labels like "gameplay" /
    /// "asset" / "telemetry" are entirely a caller convention. Inject one shared instance
    /// into every <see cref="ApiClientOptions.PriorityCoordinator"/> so lane activity in
    /// one <see cref="ApiClient"/> coordinates with another.
    /// </summary>
    /// <remarks>
    /// Three primitives:
    /// <list type="bullet">
    /// <item><see cref="EnterLane"/> — increment a lane's in-flight counter (decrement on
    /// scope dispose). Other lanes that yield to this one will pause while the counter is
    /// non-zero.</item>
    /// <item><see cref="WaitForYieldedLanesIdleAsync"/> — wait until every lane in the
    /// caller's <see cref="LaneConfig.YieldsTo"/> set is idle (or the lane's
    /// <see cref="LaneConfig.FairnessMaxPause"/> elapses).</item>
    /// <item><see cref="AcquireSlotAsync"/> — bulkhead. Caps concurrent requests on a
    /// lane regardless of any yield relationships.</item>
    /// </list>
    /// Validation at construction rejects duplicate lane ids, unknown
    /// <see cref="LaneConfig.YieldsTo"/> targets, and cycles in the yield graph.
    /// </remarks>
    public sealed class RequestPriorityCoordinator : IDisposable
    {
        private readonly Dictionary<string, LaneState> _lanes;
        private int _disposed;

        public RequestPriorityCoordinator(IEnumerable<LaneConfig> lanes)
        {
            if (lanes == null) throw new ArgumentNullException(nameof(lanes));

            _lanes = new Dictionary<string, LaneState>(StringComparer.Ordinal);
            foreach (var cfg in lanes)
            {
                if (cfg == null)
                    throw new ArgumentException("Lane configs must not be null.", nameof(lanes));
                if (_lanes.ContainsKey(cfg.Id))
                    throw new ArgumentException($"Duplicate lane id '{cfg.Id}'.", nameof(lanes));

                _lanes.Add(cfg.Id, new LaneState(cfg));
            }

            ValidateGraph();
        }

        /// <summary>
        /// Returns the registered <see cref="LaneConfig"/> for <paramref name="laneId"/>,
        /// or throws <see cref="KeyNotFoundException"/> when the id is unknown.
        /// </summary>
        public LaneConfig GetLaneConfig(string laneId)
        {
            ThrowIfDisposed();
            return Lookup(laneId).Config;
        }

        /// <summary>
        /// Number of in-flight requests on <paramref name="laneId"/>. <c>null</c> id is
        /// a no-op and returns 0; throws on unknown non-null id.
        /// </summary>
        public int InFlight(string laneId)
        {
            ThrowIfDisposed();
            if (laneId == null) return 0;
            return Volatile.Read(ref Lookup(laneId).Count);
        }

        /// <summary>
        /// Increment the in-flight counter for <paramref name="laneId"/>. Dispose the
        /// returned scope to decrement. <c>null</c> id returns the no-op default scope
        /// so callers can write
        /// <c>using var s = coord?.EnterLane(req.PriorityLane) ?? default;</c>.
        /// </summary>
        public LaneScope EnterLane(string laneId)
        {
            ThrowIfDisposed();
            if (laneId == null) return default;

            var state = Lookup(laneId);

            // Lock guards counter+TCS swap atomically. Without it: thread A decrements
            // 1->0 and reads IdleTcs to complete it; thread B enters, sees count==1
            // again and swaps a fresh TCS; A then completes the now-stale TCS.
            lock (state.Gate)
            {
                state.Count++;
                if (state.Count == 1)
                {
                    state.IdleTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            return new LaneScope(state);
        }

        /// <summary>
        /// Awaits idleness on every lane in <paramref name="laneId"/>'s
        /// <see cref="LaneConfig.YieldsTo"/> set, with the lane's
        /// <see cref="LaneConfig.FairnessMaxPause"/> as a ceiling. Cancellable.
        /// Fast-path: returns <see cref="Task.CompletedTask"/> when no yielded-to lane
        /// is currently busy. <c>null</c> id is a no-op.
        /// </summary>
        public Task WaitForYieldedLanesIdleAsync(string laneId, CancellationToken ct)
        {
            ThrowIfDisposed();
            if (laneId == null) return Task.CompletedTask;

            var self = Lookup(laneId);
            if (self.YieldedTo.Length == 0) return Task.CompletedTask;

            // Snapshot the busy yielded-to lanes' idle TCS tasks.
            List<Task> busyTasks = null;
            foreach (var target in self.YieldedTo)
            {
                Task tcsTask;
                lock (target.Gate)
                {
                    if (target.Count == 0) continue;
                    tcsTask = target.IdleTcs.Task;
                }
                (busyTasks ??= new List<Task>(self.YieldedTo.Length)).Add(tcsTask);
            }

            if (busyTasks == null) return Task.CompletedTask;

            return WaitForBusyTcsOrFairnessAsync(busyTasks, self.Config.FairnessMaxPause, ct);
        }

        private static async Task WaitForBusyTcsOrFairnessAsync(List<Task> busyTcsTasks, TimeSpan fairnessMaxPause, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var idleAll = Task.WhenAll(busyTcsTasks);
            var fairness = Task.Delay(fairnessMaxPause, ct);
            await Task.WhenAny(idleAll, fairness).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Acquire one bulkhead slot for <paramref name="laneId"/>. Dispose the returned
        /// handle to release. Honours <paramref name="ct"/>. <c>null</c> id returns a
        /// no-op handle.
        /// </summary>
        public async Task<IDisposable> AcquireSlotAsync(string laneId, CancellationToken ct)
        {
            ThrowIfDisposed();
            if (laneId == null) return NoopDisposable.Instance;

            var state = Lookup(laneId);
            await state.Bulkhead.WaitAsync(ct).ConfigureAwait(false);
            return new SemaphoreReleaser(state.Bulkhead);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            // Complete every lane's idle TCS so pending WaitForYieldedLanesIdleAsync
            // callers don't hang at shutdown.
            foreach (var state in _lanes.Values)
            {
                TaskCompletionSource<bool> tcs;
                lock (state.Gate)
                {
                    tcs = state.IdleTcs;
                }
                tcs.TrySetResult(true);
                state.Bulkhead.Dispose();
            }
        }

        private LaneState Lookup(string laneId)
        {
            if (laneId == null) throw new ArgumentNullException(nameof(laneId));
            if (!_lanes.TryGetValue(laneId, out var state))
                throw new KeyNotFoundException($"Lane '{laneId}' is not registered with this coordinator.");
            return state;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(RequestPriorityCoordinator));
        }

        private void ValidateGraph()
        {
            // Resolve YieldsTo string ids to LaneState references; detect missing targets.
            foreach (var state in _lanes.Values)
            {
                var ids = state.Config.YieldsTo;
                if (ids == null || ids.Count == 0)
                {
                    state.YieldedTo = Array.Empty<LaneState>();
                    continue;
                }

                var resolved = new LaneState[ids.Count];
                var i = 0;
                foreach (var id in ids)
                {
                    if (!_lanes.TryGetValue(id, out var target))
                        throw new ArgumentException(
                            $"Lane '{state.Config.Id}' yields to unknown lane '{id}'.");
                    if (ReferenceEquals(target, state))
                        throw new ArgumentException(
                            $"Lane '{state.Config.Id}' yields to itself.");
                    resolved[i++] = target;
                }
                state.YieldedTo = resolved;
            }

            // Cycle detection via DFS: 0 = unvisited, 1 = on current stack, 2 = done.
            var color = new Dictionary<LaneState, int>(_lanes.Count);
            foreach (var state in _lanes.Values) color[state] = 0;
            foreach (var state in _lanes.Values)
            {
                if (color[state] == 0) DfsCheck(state, color);
            }
        }

        private static void DfsCheck(LaneState node, Dictionary<LaneState, int> color)
        {
            color[node] = 1;
            foreach (var next in node.YieldedTo)
            {
                var c = color[next];
                if (c == 1)
                    throw new ArgumentException(
                        $"Cycle detected in lane yield graph (involving '{next.Config.Id}').");
                if (c == 0) DfsCheck(next, color);
            }
            color[node] = 2;
        }

        // Internal — owned state used by LaneScope.Dispose to decrement without taking
        // a reference to the coordinator on the scope (keeps the scope a single-field
        // readonly struct).
        internal sealed class LaneState
        {
            public LaneConfig Config { get; }
            public SemaphoreSlim Bulkhead { get; }
            public object Gate { get; } = new object();
            public TaskCompletionSource<bool> IdleTcs;
            public int Count;
            public LaneState[] YieldedTo;

            public LaneState(LaneConfig config)
            {
                Config = config;
                var max = Math.Max(1, config.MaxConcurrent);
                Bulkhead = new SemaphoreSlim(max, max);
                IdleTcs = CompletedTcs();
                YieldedTo = Array.Empty<LaneState>();
            }

            // Decrement counter; complete idle TCS on 1->0. Called by LaneScope.Dispose.
            public void Exit()
            {
                TaskCompletionSource<bool> toComplete = null;
                lock (Gate)
                {
                    if (Count == 0) return;
                    Count--;
                    if (Count == 0) toComplete = IdleTcs;
                }
                toComplete?.TrySetResult(true);
            }

            private static TaskCompletionSource<bool> CompletedTcs()
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                tcs.TrySetResult(true);
                return tcs;
            }
        }

        private sealed class SemaphoreReleaser : IDisposable
        {
            private SemaphoreSlim _semaphore;
            public SemaphoreReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

            public void Dispose()
            {
                var s = Interlocked.Exchange(ref _semaphore, null);
                s?.Release();
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Allocation-free disposable returned by <see cref="RequestPriorityCoordinator.EnterLane"/>.
    /// Default value is a no-op so callers can null-coalesce against a missing
    /// coordinator or a null lane id.
    /// </summary>
    public readonly struct LaneScope : IDisposable
    {
        private readonly RequestPriorityCoordinator.LaneState _state;

        internal LaneScope(RequestPriorityCoordinator.LaneState state)
        {
            _state = state;
        }

        public void Dispose() => _state?.Exit();
    }
}
