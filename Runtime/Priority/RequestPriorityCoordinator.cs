using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApiClient.Runtime.Priority
{
    /// <summary>
    /// Coordinates priority between gameplay HTTP traffic and bulk asset downloads.
    /// One instance is intended to be shared across every <see cref="ApiClient"/> in the
    /// process so that gameplay activity in one instance throttles asset transfers in
    /// another. Inject via <see cref="ApiClientOptions.PriorityCoordinator"/>.
    /// </summary>
    /// <remarks>
    /// Gameplay tracking: <see cref="EnterGameplay"/> returns a struct disposable that
    /// increments an internal counter. When the counter is non-zero, asset workers gating
    /// on <see cref="WaitForGameplayIdleAsync"/> stay paused.
    ///
    /// Asset bulkhead: <see cref="AcquireAssetSlotAsync"/> uses a <see cref="SemaphoreSlim"/>
    /// to cap concurrent asset transfers regardless of the gameplay counter.
    ///
    /// Fairness: <see cref="WaitForGameplayIdleAsync"/> accepts a max-pause timeout so
    /// asset workers cannot starve indefinitely under sustained gameplay traffic.
    /// </remarks>
    public sealed class RequestPriorityCoordinator : IDisposable
    {
        private readonly PriorityCoordinatorOptions _options;
        private readonly SemaphoreSlim _assetSemaphore;
        private readonly object _gate = new();
        private TaskCompletionSource<bool> _idleTcs;
        private long _gameplayCount;
        private int _disposed;

        public RequestPriorityCoordinator(PriorityCoordinatorOptions options = null)
        {
            _options = options ?? new PriorityCoordinatorOptions();
            var max = Math.Max(1, _options.MaxConcurrentAssetTransfers);
            _assetSemaphore = new SemaphoreSlim(max, max);
            _idleTcs = CompletedTcs();
        }

        public PriorityCoordinatorOptions Options => _options;

        /// <summary>
        /// Number of gameplay requests currently in flight. Read-only snapshot.
        /// </summary>
        public long GameplayInFlight => Interlocked.Read(ref _gameplayCount);

        /// <summary>
        /// Increment the gameplay-in-flight counter. Dispose the returned scope to
        /// decrement. The struct is allocation-free; default(GameplayScope) is a no-op
        /// so callers can write <c>using var s = coord?.EnterGameplay() ?? default;</c>.
        /// </summary>
        public GameplayScope EnterGameplay()
        {
            if (!_options.Enabled) return default;

            lock (_gate)
            {
                _gameplayCount++;
                if (_gameplayCount == 1)
                {
                    // transition idle -> busy: install fresh non-completed TCS
                    _idleTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            return new GameplayScope(this);
        }

        internal void ExitGameplay()
        {
            TaskCompletionSource<bool> toComplete = null;
            lock (_gate)
            {
                if (_gameplayCount == 0) return; // defensive — paired with EnterGameplay
                _gameplayCount--;
                if (_gameplayCount == 0)
                {
                    toComplete = _idleTcs;
                }
            }
            toComplete?.TrySetResult(true);
        }

        /// <summary>
        /// Returns a task that completes when no gameplay request is in flight, or when
        /// <paramref name="maxWait"/> elapses (fairness ceiling so assets never starve),
        /// or when <paramref name="ct"/> is cancelled (in which case the task is cancelled).
        /// Fast-path: returns <see cref="Task.CompletedTask"/> immediately when idle.
        /// </summary>
        public async Task WaitForGameplayIdleAsync(CancellationToken ct, TimeSpan? maxWait = null)
        {
            if (!_options.Enabled) return;

            TaskCompletionSource<bool> idleTcs;
            lock (_gate)
            {
                if (_gameplayCount == 0) return;
                idleTcs = _idleTcs;
            }

            ct.ThrowIfCancellationRequested();

            var idleTask = idleTcs.Task;

            if (maxWait.HasValue)
            {
                // Race idle vs timeout vs cancellation. Timeout is non-throwing (fairness exit).
                var delayTask = Task.Delay(maxWait.Value, ct);
                await Task.WhenAny(idleTask, delayTask).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
            }
            else
            {
                // Race idle vs cancellation only.
                if (!ct.CanBeCanceled)
                {
                    await idleTask.ConfigureAwait(false);
                    return;
                }

                var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(s => ((TaskCompletionSource<bool>)s).TrySetCanceled(), cancelTcs))
                {
                    await Task.WhenAny(idleTask, cancelTcs.Task).ConfigureAwait(false);
                }
                ct.ThrowIfCancellationRequested();
            }
        }

        /// <summary>
        /// Acquire one asset transfer slot from the bulkhead. Dispose the returned handle
        /// to release. Honours <paramref name="ct"/>.
        /// </summary>
        public async Task<IDisposable> AcquireAssetSlotAsync(CancellationToken ct)
        {
            if (!_options.Enabled) return NoopDisposable.Instance;
            await _assetSemaphore.WaitAsync(ct).ConfigureAwait(false);
            return new SemaphoreReleaser(_assetSemaphore);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _assetSemaphore.Dispose();
        }

        private static TaskCompletionSource<bool> CompletedTcs()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs.TrySetResult(true);
            return tcs;
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
    /// Allocation-free disposable returned by <see cref="RequestPriorityCoordinator.EnterGameplay"/>.
    /// Default value is a no-op so callers can null-coalesce against a missing coordinator.
    /// </summary>
    public readonly struct GameplayScope : IDisposable
    {
        private readonly RequestPriorityCoordinator _coordinator;

        internal GameplayScope(RequestPriorityCoordinator coordinator)
        {
            _coordinator = coordinator;
        }

        public void Dispose()
        {
            _coordinator?.ExitGameplay();
        }
    }
}
