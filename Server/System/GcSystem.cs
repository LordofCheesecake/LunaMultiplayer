using Server.Context;
using Server.Log;
using Server.Settings.Structures;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Server.System
{
    /// <summary>
    /// Previously called <see cref="GC.Collect()"/> on a fixed interval to paper over memory leaks. That caused
    /// tail-latency spikes (full blocking collections on the server process, disrupting every connected client).
    ///
    /// The underlying leak sources were addressed separately:
    /// - central wrapper recycling in <see cref="Server.Server.MessageReceiver"/> and per-client send paths,
    /// - <see cref="VesselContext"/> kill-list capped with FIFO eviction,
    /// - <see cref="Vessel.VesselDataUpdater.ForgetVessel"/> now also clears per-message-type throttle dicts.
    ///
    /// This loop is kept (gated by <see cref="IntervalSettings.SettingsStore.GcMinutesInterval"/>) but now only
    /// issues a periodic <see cref="GC.Collect(int, GCCollectionMode, bool, bool)"/> in an OPT-IN mode with
    /// the optimized/non-blocking path, intended as a diagnostic lever rather than a default behavior.
    /// </summary>
    public class GcSystem
    {
        public static async Task PerformGarbageCollectionAsync(CancellationToken token)
        {
            // Opt-in: only run if operator explicitly sets a non-zero interval. Default is 0 -> this task exits.
            var intervalMinutes = IntervalSettings.SettingsStore.GcMinutesInterval;
            if (intervalMinutes <= 0)
            {
                LunaLog.NetworkDebug("GcSystem disabled (GcMinutesInterval=0); the runtime's GC is left to run on its own");
                return;
            }

            while (ServerContext.ServerRunning && !token.IsCancellationRequested)
            {
                // Optimized + non-blocking: instruct the runtime to collect but don't force a full blocking
                // pause like the previous `GC.Collect()` did. If a real leak resurfaces, the operator can
                // still toggle this on, but at lower latency cost than before.
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false, compacting: false);

                try
                {
                    await Task.Delay((int)TimeSpan.FromMinutes(intervalMinutes).TotalMilliseconds, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
