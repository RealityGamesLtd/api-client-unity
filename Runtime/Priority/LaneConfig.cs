using System;
using System.Collections.Generic;

namespace ApiClient.Runtime.Priority
{
    /// <summary>
    /// Caller-defined description of one priority lane: its identifier, concurrency cap,
    /// which other lanes pause it, fairness ceiling, and whether its byte-array transfers
    /// run as preempt-friendly chunked Range downloads.
    ///
    /// Lanes are registered up-front via <see cref="RequestPriorityCoordinator"/>'s
    /// constructor; identifiers are opaque strings owned by the caller.
    /// </summary>
    public sealed class LaneConfig
    {
        public LaneConfig(string id)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Lane id must not be null or empty.", nameof(id));
            Id = id;
        }

        /// <summary>Caller-defined lane identifier. Compared by ordinal string equality.</summary>
        public string Id { get; }

        /// <summary>
        /// Maximum concurrent requests admitted to this lane via
        /// <see cref="RequestPriorityCoordinator.AcquireSlotAsync"/>. Default unbounded.
        /// </summary>
        public int MaxConcurrent { get; set; } = int.MaxValue;

        /// <summary>
        /// Lane identifiers that, while in flight, should pause this lane. A request on
        /// this lane awaits all <see cref="YieldsTo"/> lanes to be idle (or
        /// <see cref="FairnessMaxPause"/> to elapse) before proceeding.
        /// </summary>
        public IReadOnlyCollection<string> YieldsTo { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Cap on how long this lane stays paused while higher-priority lanes are busy.
        /// Prevents starvation under sustained higher-priority traffic.
        /// </summary>
        public TimeSpan FairnessMaxPause { get; set; } = TimeSpan.FromSeconds(8);

        /// <summary>
        /// When true, <see cref="ApiClient.SendByteArrayRequest"/> on this lane runs as
        /// chunked HTTP Range downloads so a higher-priority lane becoming busy preempts
        /// the transfer between chunks. When false the legacy single-GET drain runs and
        /// is gated only between buffer reads (TCP back-pressure).
        /// </summary>
        public bool ChunkedRangeDownloads { get; set; } = false;
    }
}
