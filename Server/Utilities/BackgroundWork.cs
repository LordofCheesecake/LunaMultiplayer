using Server.Log;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Server.Utilities
{
    /// <summary>
    /// Small helper around fire-and-forget <see cref="Task.Run"/> that attaches a fault handler so unhandled
    /// exceptions are logged (and don't become <c>TaskScheduler.UnobservedTaskException</c> events with process-
    /// policy-dependent behavior). Use this instead of raw <c>_ = Task.Run(...)</c>.
    /// The <c>description</c> parameter defaults to the caller's member name via <see cref="CallerMemberNameAttribute"/>
    /// so call sites can just write <c>BackgroundWork.Fire(() =&gt; ... );</c>.
    /// </summary>
    public static class BackgroundWork
    {
        public static void Fire(Action action, [CallerMemberName] string description = null)
        {
            if (action == null) return;

            _ = Task.Run(action)
                .ContinueWith(t => LunaLog.Error($"Background task '{description}' failed: {t.Exception?.GetBaseException()}"),
                              TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        public static void Fire(Func<Task> asyncFunc, [CallerMemberName] string description = null)
        {
            if (asyncFunc == null) return;

            _ = Task.Run(asyncFunc)
                .ContinueWith(t => LunaLog.Error($"Background task '{description}' failed: {t.Exception?.GetBaseException()}"),
                              TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
