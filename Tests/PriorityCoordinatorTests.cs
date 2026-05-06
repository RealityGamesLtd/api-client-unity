using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ApiClient.Runtime.Priority;
using NUnit.Framework;

namespace ApiClient.Tests
{
    /// <summary>
    /// In-memory tests for <see cref="RequestPriorityCoordinator"/> covering generic
    /// multi-lane semantics. The HTTP-touching paths (chunked Range downloads, 200
    /// fallback, mid-transfer 200 regression) are exercised on-device against the real
    /// backend — see the manual verification section of the priority-lane plan.
    /// </summary>
    public class PriorityCoordinatorTests
    {
        private static LaneConfig Lane(string id, int max = int.MaxValue, IReadOnlyCollection<string> yieldsTo = null, TimeSpan? fairness = null)
            => new LaneConfig(id)
            {
                MaxConcurrent = max,
                YieldsTo = yieldsTo ?? Array.Empty<string>(),
                FairnessMaxPause = fairness ?? TimeSpan.FromSeconds(8),
            };

        // ─────────────────────────── Construction validation ─────────────────────────

        [Test]
        public void Ctor_rejects_duplicate_lane_ids()
        {
            Assert.Throws<ArgumentException>(() =>
                new RequestPriorityCoordinator(new[] { Lane("a"), Lane("a") }));
        }

        [Test]
        public void Ctor_rejects_unknown_yields_to_target()
        {
            Assert.Throws<ArgumentException>(() =>
                new RequestPriorityCoordinator(new[] { Lane("a", yieldsTo: new[] { "ghost" }) }));
        }

        [Test]
        public void Ctor_rejects_self_yield()
        {
            Assert.Throws<ArgumentException>(() =>
                new RequestPriorityCoordinator(new[] { Lane("a", yieldsTo: new[] { "a" }) }));
        }

        [Test]
        public void Ctor_rejects_cycle()
        {
            // a -> b -> a
            Assert.Throws<ArgumentException>(() => new RequestPriorityCoordinator(new[]
            {
                Lane("a", yieldsTo: new[] { "b" }),
                Lane("b", yieldsTo: new[] { "a" }),
            }));
        }

        [Test]
        public void Ctor_accepts_acyclic_chain()
        {
            using var coord = new RequestPriorityCoordinator(new[]
            {
                Lane("a"),
                Lane("b", yieldsTo: new[] { "a" }),
                Lane("c", yieldsTo: new[] { "a", "b" }),
            });
            Assert.That(coord.GetLaneConfig("c").YieldsTo, Has.Count.EqualTo(2));
        }

        // ─────────────────────────── EnterLane / counter ─────────────────────────────

        [Test]
        public void EnterLane_unknown_id_throws()
        {
            using var coord = new RequestPriorityCoordinator(new[] { Lane("a") });
            Assert.Throws<KeyNotFoundException>(() => coord.EnterLane("ghost"));
        }

        [Test]
        public async Task EnterLane_increments_and_decrements_counter()
        {
            using var coord = new RequestPriorityCoordinator(new[] { Lane("a") });
            Assert.That(coord.InFlight("a"), Is.EqualTo(0));

            using (coord.EnterLane("a"))
            {
                Assert.That(coord.InFlight("a"), Is.EqualTo(1));
                using (coord.EnterLane("a"))
                {
                    Assert.That(coord.InFlight("a"), Is.EqualTo(2));
                }
                Assert.That(coord.InFlight("a"), Is.EqualTo(1));
            }
            Assert.That(coord.InFlight("a"), Is.EqualTo(0));
            await Task.Yield();
        }

        [Test]
        public void Default_LaneScope_is_noop()
        {
            using var coord = new RequestPriorityCoordinator(new[] { Lane("a") });
            using (default(LaneScope)) { /* dispose noop */ }
            Assert.That(coord.InFlight("a"), Is.EqualTo(0));
        }

        [Test]
        public void EnterLane_null_id_returns_default_scope()
        {
            using var coord = new RequestPriorityCoordinator(new[] { Lane("a") });
            using (coord.EnterLane(null))
            {
                Assert.That(coord.InFlight("a"), Is.EqualTo(0));
            }
        }

        // ─────────────────────────── WaitForYieldedLanesIdleAsync ────────────────────

        [Test]
        public async Task WaitForYieldedLanesIdleAsync_returns_immediately_when_targets_idle()
        {
            using var coord = new RequestPriorityCoordinator(new[]
            {
                Lane("a"),
                Lane("b", yieldsTo: new[] { "a" }),
            });

            var t = coord.WaitForYieldedLanesIdleAsync("b", CancellationToken.None);
            Assert.That(t.IsCompletedSuccessfully);
            await t;
        }

        [Test]
        public async Task WaitForYieldedLanesIdleAsync_no_yields_returns_immediately()
        {
            using var coord = new RequestPriorityCoordinator(new[] { Lane("a") });
            var t = coord.WaitForYieldedLanesIdleAsync("a", CancellationToken.None);
            Assert.That(t.IsCompletedSuccessfully);
            await t;
        }

        [Test]
        public async Task WaitForYieldedLanesIdleAsync_unblocks_when_target_lane_exits()
        {
            using var coord = new RequestPriorityCoordinator(new[]
            {
                Lane("a"),
                Lane("b", yieldsTo: new[] { "a" }),
            });

            var aScope = coord.EnterLane("a");
            var waitTask = coord.WaitForYieldedLanesIdleAsync("b", CancellationToken.None);

            Assert.That(waitTask.IsCompleted, Is.False, "should be pending while a is busy");

            aScope.Dispose();
            await waitTask;
            Assert.That(waitTask.IsCompletedSuccessfully);
        }

        [Test]
        public async Task WaitForYieldedLanesIdleAsync_returns_after_fairness_max_pause()
        {
            using var coord = new RequestPriorityCoordinator(new[]
            {
                Lane("a"),
                Lane("b", yieldsTo: new[] { "a" }, fairness: TimeSpan.FromMilliseconds(150)),
            });

            using var _ = coord.EnterLane("a");
            var waitTask = coord.WaitForYieldedLanesIdleAsync("b", CancellationToken.None);

            // Should not complete immediately.
            var early = await Task.WhenAny(waitTask, Task.Delay(50));
            Assert.That(early, Is.Not.SameAs(waitTask));

            // Should complete within a generous safety timeout via the fairness exit.
            var settled = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.That(settled, Is.SameAs(waitTask), "fairness wait did not elapse");
            await waitTask;
            Assert.That(coord.InFlight("a"), Is.EqualTo(1), "fairness exit must not decrement target counter");
        }

        [Test]
        public async Task WaitForYieldedLanesIdleAsync_throws_on_cancellation()
        {
            using var coord = new RequestPriorityCoordinator(new[]
            {
                Lane("a"),
                Lane("b", yieldsTo: new[] { "a" }),
            });
            using var _ = coord.EnterLane("a");
            using var cts = new CancellationTokenSource();

            var waitTask = coord.WaitForYieldedLanesIdleAsync("b", cts.Token);
            cts.Cancel();

            var threw = false;
            try { await waitTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { threw = true; }
            Assert.That(threw, Is.True);
        }

        // ─────────────────────────── AcquireSlotAsync ────────────────────────────────

        [Test]
        public async Task AcquireSlotAsync_caps_per_lane_concurrency()
        {
            using var coord = new RequestPriorityCoordinator(new[] { Lane("a", max: 2) });

            var s1 = await coord.AcquireSlotAsync("a", CancellationToken.None);
            var s2 = await coord.AcquireSlotAsync("a", CancellationToken.None);
            Assert.That(s1, Is.Not.Null);
            Assert.That(s2, Is.Not.Null);

            var s3Task = coord.AcquireSlotAsync("a", CancellationToken.None);
            var raced = await Task.WhenAny(s3Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.That(raced, Is.Not.SameAs(s3Task), "third acquire should be pending");

            s1.Dispose();
            var s3 = await s3Task;
            Assert.That(s3, Is.Not.Null);
            s2.Dispose();
            s3.Dispose();
        }

        [Test]
        public async Task AcquireSlotAsync_respects_cancellation()
        {
            using var coord = new RequestPriorityCoordinator(new[] { Lane("a", max: 1) });
            var first = await coord.AcquireSlotAsync("a", CancellationToken.None);

            using var cts = new CancellationTokenSource();
            var pending = coord.AcquireSlotAsync("a", cts.Token);
            Assert.That(pending.IsCompleted, Is.False);

            cts.Cancel();
            var threw = false;
            try { await pending.ConfigureAwait(false); }
            catch (OperationCanceledException) { threw = true; }
            Assert.That(threw, Is.True);

            first.Dispose();
        }

        [Test]
        public async Task AcquireSlotAsync_null_lane_returns_noop_handle()
        {
            using var coord = new RequestPriorityCoordinator(new[] { Lane("a", max: 1) });
            var h1 = await coord.AcquireSlotAsync(null, CancellationToken.None);
            var h2 = await coord.AcquireSlotAsync(null, CancellationToken.None);
            Assert.That(h1, Is.Not.Null);
            Assert.That(h2, Is.Not.Null);
            h1.Dispose();
            h2.Dispose();
        }

        // ─────────────────────────── Multi-lane chains ───────────────────────────────

        [Test]
        public async Task Three_lane_chain_priority_chain()
        {
            // a (highest) -> b yields to a -> c yields to a,b
            using var coord = new RequestPriorityCoordinator(new[]
            {
                Lane("a"),
                Lane("b", yieldsTo: new[] { "a" }),
                Lane("c", yieldsTo: new[] { "a", "b" }),
            });

            var aScope = coord.EnterLane("a");
            var bScope = coord.EnterLane("b");

            var cWait = coord.WaitForYieldedLanesIdleAsync("c", CancellationToken.None);
            Assert.That(cWait.IsCompleted, Is.False, "c should wait for both a and b to be idle");

            aScope.Dispose();
            // Still busy because b is in flight.
            Assert.That(cWait.IsCompleted, Is.False);

            bScope.Dispose();
            await cWait;
            Assert.That(cWait.IsCompletedSuccessfully);
        }

        // ─────────────────────────── Disposal ────────────────────────────────────────

        [Test]
        public async Task Dispose_unblocks_pending_waits()
        {
            var coord = new RequestPriorityCoordinator(new[]
            {
                Lane("a"),
                Lane("b", yieldsTo: new[] { "a" }),
            });
            using var _ = coord.EnterLane("a");

            var waitTask = coord.WaitForYieldedLanesIdleAsync("b", CancellationToken.None);
            Assert.That(waitTask.IsCompleted, Is.False);

            coord.Dispose();
            await waitTask;
            Assert.That(waitTask.IsCompletedSuccessfully);
        }

        [Test]
        public void Disposed_coordinator_throws_on_use()
        {
            var coord = new RequestPriorityCoordinator(new[] { Lane("a") });
            coord.Dispose();
            Assert.Throws<ObjectDisposedException>(() => coord.EnterLane("a"));
            Assert.Throws<ObjectDisposedException>(() => coord.InFlight("a"));
            Assert.Throws<ObjectDisposedException>(() => coord.GetLaneConfig("a"));
        }

        // ─────────────────────────── Concurrency stress ──────────────────────────────

        [Test]
        public async Task Concurrent_enter_exit_thread_safe()
        {
            using var coord = new RequestPriorityCoordinator(new[] { Lane("a") });
            const int n = 200;

            var tasks = new Task[n];
            for (var i = 0; i < n; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    using var _ = coord.EnterLane("a");
                    await Task.Yield();
                });
            }

            await Task.WhenAll(tasks);
            Assert.That(coord.InFlight("a"), Is.EqualTo(0));
        }
    }
}
