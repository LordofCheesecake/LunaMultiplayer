using System;
using System.Collections.Concurrent;

namespace Server.System
{
    /// <summary>
    /// Tracks vessel ids the server has explicitly killed so stale proto traffic for those ids can be rejected.
    /// Previously this dictionary was add-only and grew unbounded across a long-running server. It's now capped
    /// with a simple FIFO eviction: when the kill-list is larger than <see cref="MaxRemovedVesselEntries"/>, the
    /// oldest entry is evicted. That is acceptable because: after a few hundred thousand removes, any stale proto
    /// for the evicted oldest id almost certainly also stopped being sent.
    /// </summary>
    public static class VesselContext
    {
        /// <summary>
        /// Upper bound on the number of vessel ids we keep in the "killed" set. Tune if legitimate traffic is
        /// producing false acceptances for long-past removes.
        /// </summary>
        private const int MaxRemovedVesselEntries = 8192;

        private static readonly ConcurrentDictionary<Guid, byte> RemovedVesselsStorage = new ConcurrentDictionary<Guid, byte>();
        private static readonly ConcurrentQueue<Guid> InsertionOrder = new ConcurrentQueue<Guid>();

        public static ConcurrentDictionary<Guid, byte> RemovedVessels => RemovedVesselsStorage;

        /// <summary>
        /// Adds a vessel id to the kill list, evicting the oldest entry if we are over the capacity budget.
        /// </summary>
        public static void AddRemovedVessel(Guid vesselId)
        {
            if (RemovedVesselsStorage.TryAdd(vesselId, 0))
            {
                InsertionOrder.Enqueue(vesselId);

                // Lazy eviction; not exact because of concurrent Add's, but bounded enough to prevent growth.
                while (RemovedVesselsStorage.Count > MaxRemovedVesselEntries && InsertionOrder.TryDequeue(out var oldest))
                {
                    RemovedVesselsStorage.TryRemove(oldest, out _);
                }
            }
        }

        /// <summary>
        /// Explicit removal from the kill list. Use when a legitimate re-creation of the id occurs so future
        /// proto messages for the same id are accepted again.
        /// </summary>
        public static void ClearRemovedVessel(Guid vesselId)
        {
            RemovedVesselsStorage.TryRemove(vesselId, out _);
        }
    }
}
