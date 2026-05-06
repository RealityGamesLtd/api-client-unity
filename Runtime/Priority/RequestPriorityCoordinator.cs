using System;
using System.Threading;
using System.Threading.Tasks;

namespace ApiClient.Runtime.Priority
{
    /// <summary>
    /// Process-wide priority coordinator: tracks gameplay HTTP traffic in flight, gates
    /// asset workers on it, and bulkheads concurrent asset transfers. Inject one shared
    /// instance into every <see cref="ApiClientOptions.PriorityCoordinator"/> so gameplay
    /// activity in one <see cref="ApiClient"/> throttles asset transfers in another.
    /// </summary>
    /// <remarks>
    /// Three responsibilities:
    /// <list type="bullet">
    /// <item>Counter — <see cref="EnterGameplay"/> increments on entry, decrements on
    /// dispose. Counter is gameplay-only by design (excludes byte-array assets and SSE
    /// streams), so asset workers see a true idle signal.</item>
    /// <item>Idle gate — <see cref="WaitForGameplayIdleAsync"/> awaits a transition to
    /// counter == 0, with an optional fairness ceiling so assets never starve.</item>
    /// <item>Asset bulkhead — <see cref="AcquireAssetSlotAsync"/> caps concurrent asset
    /// transfers via a <see cref="SemaphoreSlim"/>. The bulkhead is asset-scoped (hence
    /// the naming) and does not gate gameplay calls.</item>
    /// </list>
    /// </remarks>
    public sealed class RequestPriorityCoordinator : IDisposable
    {
        private readonly PriorityCoordinatorOptions _options;
        private readonly SemaphoreSlim _assetSemaphore;
        private readonly object _gate = new();
        private TaskCompletionSource<bool> _idleTcs;
        private int _gameplayCount;
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
        /// Number of gameplay requests currently in flight. Excludes asset and stream
        /// traffic. Read-only snapshot.
        /// </summary>
        public int GameplayInFlight => Volatile.Read(ref _gameplayCount);

        /// <summary>
        /// Increment the gameplay-in-flight counter. Dispose the returned scope to
        /// decrement. The struct is allocation-free; <c>default(GameplayScope)</c> is a
        /// no-op so callers can write
        /// <c>using var s = coord?.EnterGameplay() ?? default;</c>.
        /// </summary>
        public GameplayScope EnterGameplay()
        {
            ThrowIfDisposed();
            if (!_options.Enabled) return default;

            // Lock guards counter+TCS swap atomically. Without it: thread A decrements
            // 1->0 and reads _idleTcs to complete it; thread B enters, sees count==1
            // again and swaps a fresh TCS into _idleTcs; A then completes the now-stale
            // TCS instead of leaving the new one pending. Race.
            lock (_gate)
            {
                _gameplayCount++;
                if (_gameplayCount == 1)
                {
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
        /// <paramref name="maxWait"/> elapses (fairness ceiling — assets never starve),
        /// or when <paramref name="ct"/> is cancelled (in which case the task is cancelled).
        /// Fast-path: returns <see cref="Task.CompletedTask"/> immediately when idle.
        /// </summary>
        public async Task WaitForGameplayIdleAsync(CancellationToken ct, TimeSpan? maxWait = null)
        {
            ThrowIfDisposed();
            if (!_options.Enabled) return;

            TaskCompletionSource<bool> idleTcs;
            lock (_gate)
            {
                if (_gameplayCount == 0) return;
                idleTcs = _idleTcs;
            }

            ct.ThrowIfCancellationRequested();

            // Race idle vs (timeout OR cancellation). Task.Delay handles both: a non-null
            // maxWait makes the delay the fairness exit; an Infinite delay only completes
            // when ct cancels. Either way, ct cancellation surfaces via the post-await
            // ThrowIfCancellationRequested.
            var delay = maxWait.HasValue
                ? Task.Delay(maxWait.Value, ct)
                : Task.Delay(Timeout.Infinite, ct);

            await Task.WhenAny(idleTcs.Task, delay).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Acquire one asset transfer slot from the bulkhead. Dispose the returned handle
        /// to release. Honours <paramref name="ct"/>.
        /// </summary>
        public async Task<IDisposable> AcquireAssetSlotAsync(CancellationToken ct)
        {
            ThrowIfDisposed();
            if (!_options.Enabled) return NoopDisposable.Instance;

            await _assetSemaphore.WaitAsync(ct).ConfigureAwait(false);
            return new SemaphoreReleaser(_assetSemaphore);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            // Cancel the idle TCS so any pending WaitForGameplayIdleAsync callers stop
            // hanging at shutdown. WhenAny against a canceled task returns instantly;
            // the post-await ThrowIfCancellationRequested only fires if the caller's own
            // CT was canceled, so a clean dispose lets waiters return normally.
            TaskCompletionSource<bool> waiters;
            lock (_gate)
            {
                waiters = _idleTcs;
                _idleTcs = CompletedTcs();
            }
            waiters?.TrySetResult(true);

            _assetSemaphore.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(RequestPriorityCoordinator));
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
