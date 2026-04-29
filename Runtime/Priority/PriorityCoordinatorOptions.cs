using System;

namespace ApiClient.Runtime.Priority
{
    /// <summary>
    /// Configuration for <see cref="RequestPriorityCoordinator"/>.
    /// </summary>
    public class PriorityCoordinatorOptions
    {
        /// <summary>
        /// Maximum number of asset (byte-array) downloads allowed to run concurrently.
        /// Acts as a bulkhead so a swarm of asset transfers cannot exhaust radio bandwidth
        /// or connection pool slots. Default 1.
        /// </summary>
        public int MaxConcurrentAssetTransfers { get; set; } = 1;

        /// <summary>
        /// Fairness ceiling: how long an asset worker may stay paused while gameplay
        /// requests are in flight before it is allowed to resume regardless. Prevents
        /// starvation when a chatty game keeps the gameplay counter perpetually non-zero.
        /// Default 8 seconds.
        /// </summary>
        public TimeSpan AssetFairnessMaxPause { get; set; } = TimeSpan.FromSeconds(8);

        /// <summary>
        /// Master switch for the coordinator. When false the coordinator behaves as a
        /// pass-through (no counters, no gating, no bulkhead) so consumers can flip the
        /// feature off at runtime without rebuilding the <see cref="ApiClient"/>.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}
