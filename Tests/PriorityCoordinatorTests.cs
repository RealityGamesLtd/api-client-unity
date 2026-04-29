using System;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime.Priority;
using NUnit.Framework;

namespace ApiClient.Tests
{
    /// <summary>
    /// In-memory tests for <see cref="RequestPriorityCoordinator"/>. The HTTP-touching
    /// paths (chunked Range downloads, 200-fallback drain, mid-transfer 200 regression)
    /// are exercised on-device against the real backend — see the manual verification
    /// section of the priority-lane plan.
    /// </summary>
    public class PriorityCoordinatorTests
    {
        [Test]
        public async Task GameplayScope_increments_and_decrements_counter()
        {
            using var coord = new RequestPriorityCoordinator();
            Assert.That(coord.GameplayInFlight, Is.EqualTo(0));

            using (coord.EnterGameplay())
            {
                Assert.That(coord.GameplayInFlight, Is.EqualTo(1));
                using (coord.EnterGameplay())
                {
                    Assert.That(coord.GameplayInFlight, Is.EqualTo(2));
                }
                Assert.That(coord.GameplayInFlight, Is.EqualTo(1));
            }

            Assert.That(coord.GameplayInFlight, Is.EqualTo(0));
            await Task.Yield();
        }

        [Test]
        public void Default_GameplayScope_is_noop()
        {
            using var coord = new RequestPriorityCoordinator();
            // default(GameplayScope) should not affect the counter when disposed.
            using (default(GameplayScope))
            {
                Assert.That(coord.GameplayInFlight, Is.EqualTo(0));
            }
            Assert.That(coord.GameplayInFlight, Is.EqualTo(0));
        }

        [Test]
        public async Task WaitForGameplayIdleAsync_returns_immediately_when_idle()
        {
            using var coord = new RequestPriorityCoordinator();
            var t = coord.WaitForGameplayIdleAsync(CancellationToken.None);
            Assert.That(t.IsCompletedSuccessfully);
            await t;
        }

        [Test]
        public async Task WaitForGameplayIdleAsync_completes_when_last_gameplay_exits()
        {
            using var coord = new RequestPriorityCoordinator();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var scope = coord.EnterGameplay();
            var waitTask = coord.WaitForGameplayIdleAsync(cts.Token);

            Assert.That(waitTask.IsCompleted, Is.False, "should still be waiting while gameplay in flight");

            scope.Dispose();
            await waitTask;
            Assert.That(waitTask.IsCompletedSuccessfully);
        }

        [Test]
        public async Task WaitForGameplayIdleAsync_returns_after_max_wait_even_under_load()
        {
            using var coord = new RequestPriorityCoordinator(new PriorityCoordinatorOptions
            {
                AssetFairnessMaxPause = TimeSpan.FromMilliseconds(150),
            });
            using var _ = coord.EnterGameplay();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            await coord.WaitForGameplayIdleAsync(CancellationToken.None, TimeSpan.FromMilliseconds(150));
            sw.Stop();

            Assert.That(coord.GameplayInFlight, Is.EqualTo(1), "fairness exit should not decrement counter");
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(140));
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000));
        }

        [Test]
        public async Task WaitForGameplayIdleAsync_throws_on_cancellation()
        {
            using var coord = new RequestPriorityCoordinator();
            using var _ = coord.EnterGameplay();
            using var cts = new CancellationTokenSource();

            var waitTask = coord.WaitForGameplayIdleAsync(cts.Token);
            cts.Cancel();

            // Avoid Assert.ThrowsAsync — under Unity's main-thread SynchronizationContext
            // it blocks the thread while the awaited continuation tries to post back, which
            // deadlocks. Use try/catch with ConfigureAwait(false) instead.
            var threw = false;
            try
            {
                await waitTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }
            Assert.That(threw, Is.True, "expected OperationCanceledException");
        }

        [Test]
        public async Task AcquireAssetSlotAsync_caps_concurrency()
        {
            using var coord = new RequestPriorityCoordinator(new PriorityCoordinatorOptions
            {
                MaxConcurrentAssetTransfers = 2,
            });

            var slot1 = await coord.AcquireAssetSlotAsync(CancellationToken.None);
            var slot2 = await coord.AcquireAssetSlotAsync(CancellationToken.None);

            // Third acquire must block.
            var slot3Task = coord.AcquireAssetSlotAsync(CancellationToken.None);
            await Task.Delay(50);
            Assert.That(slot3Task.IsCompleted, Is.False);

            slot1.Dispose();
            var slot3 = await slot3Task;
            slot2.Dispose();
            slot3.Dispose();
        }

        [Test]
        public async Task AcquireAssetSlotAsync_respects_cancellation()
        {
            using var coord = new RequestPriorityCoordinator(new PriorityCoordinatorOptions
            {
                MaxConcurrentAssetTransfers = 1,
            });
            var first = await coord.AcquireAssetSlotAsync(CancellationToken.None);

            using var cts = new CancellationTokenSource();
            var pending = coord.AcquireAssetSlotAsync(cts.Token);

            Assert.That(pending.IsCompleted, Is.False, "should be pending while semaphore is exhausted");

            cts.Cancel();

            // Avoid Assert.ThrowsAsync — under Unity's main-thread SynchronizationContext
            // it deadlocks (blocks the thread while the continuation tries to post back).
            var threw = false;
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }
            Assert.That(threw, Is.True, "expected OperationCanceledException");

            first.Dispose();
        }

        [Test]
        public async Task Disabled_coordinator_is_passthrough()
        {
            using var coord = new RequestPriorityCoordinator(new PriorityCoordinatorOptions
            {
                Enabled = false,
                MaxConcurrentAssetTransfers = 1,
            });

            using var _gameplay = coord.EnterGameplay();
            // GameplayScope should not have incremented the counter when disabled.
            Assert.That(coord.GameplayInFlight, Is.EqualTo(0));

            // Wait should be a no-op.
            await coord.WaitForGameplayIdleAsync(CancellationToken.None);

            // Acquire still serialises but unboundedly returns the noop disposable.
            var slot1 = await coord.AcquireAssetSlotAsync(CancellationToken.None);
            var slot2Task = coord.AcquireAssetSlotAsync(CancellationToken.None);
            Assert.That(slot2Task.IsCompletedSuccessfully, "disabled coordinator should not bulkhead");
            (await slot2Task).Dispose();
            slot1.Dispose();
        }

        [Test]
        public async Task Concurrent_gameplay_scopes_are_thread_safe()
        {
            using var coord = new RequestPriorityCoordinator();
            const int n = 200;

            var tasks = new Task[n];
            for (var i = 0; i < n; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    using var _ = coord.EnterGameplay();
                    await Task.Yield();
                });
            }

            await Task.WhenAll(tasks);
            Assert.That(coord.GameplayInFlight, Is.EqualTo(0));
        }
    }
}
