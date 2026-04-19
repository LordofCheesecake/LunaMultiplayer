using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Utilities
{
    /// <summary>
    /// Coalesces repeated callbacks keyed by a string tag. Useful for "write this scenario to disk, but at most
    /// every N ms" patterns. When <see cref="Trigger"/> is called:
    /// - first call for a key fires the action after <paramref name="delayMs"/>;
    /// - subsequent calls inside the window re-arm the timer with the latest action so only the last state wins.
    ///
    /// Exceptions are logged via <see cref="BackgroundWork"/>'s log path. A single internal dictionary tracks
    /// per-key state so each debounced channel is independent.
    /// </summary>
    public static class Debouncer
    {
        private sealed class Entry
        {
            public CancellationTokenSource Cts;
            public Action LatestAction;
        }

        private static readonly ConcurrentDictionary<string, Entry> Entries = new ConcurrentDictionary<string, Entry>();

        public static void Trigger(string key, int delayMs, Action action)
        {
            if (string.IsNullOrEmpty(key) || action == null) return;

            var entry = Entries.GetOrAdd(key, _ => new Entry());

            // Atomically swap in a fresh CTS + latest action; cancel the previous scheduled run.
            CancellationTokenSource previousCts;
            CancellationTokenSource newCts;
            lock (entry)
            {
                previousCts = entry.Cts;
                newCts = entry.Cts = new CancellationTokenSource();
                entry.LatestAction = action;
            }

            previousCts?.Cancel();

            // Schedule the run. When it fires, it executes whatever action was most recently set by Trigger.
            BackgroundWork.Fire(async () =>
            {
                try
                {
                    await Task.Delay(delayMs, newCts.Token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                Action toRun;
                lock (entry)
                {
                    toRun = entry.LatestAction;
                    entry.LatestAction = null;
                }

                toRun?.Invoke();
            });
        }
    }
}
