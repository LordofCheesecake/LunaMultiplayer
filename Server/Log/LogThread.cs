using LmpCommon.Time;
using Server.Context;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Log
{
    public class LogThread
    {
        private const int PollIntervalMs = 250;
        private const long LogExpireCheckIntervalMs = 600_000;
        private const long DayCheckIntervalMs = 60_000;

        private static long _lastLogExpiredCheck;
        private static long _lastDayCheck;

        public static async Task RunLogThreadAsync(CancellationToken token)
        {
            while (ServerContext.ServerRunning && !token.IsCancellationRequested)
            {
                //Run the log expire function every 10 minutes
                if (ServerContext.ServerClock.ElapsedMilliseconds - _lastLogExpiredCheck > LogExpireCheckIntervalMs)
                {
                    _lastLogExpiredCheck = ServerContext.ServerClock.ElapsedMilliseconds;
                    LogExpire.ExpireLogs();
                }

                // Check if the day has changed, every minute
                if (ServerContext.ServerClock.ElapsedMilliseconds - _lastDayCheck > DayCheckIntervalMs)
                {
                    _lastDayCheck = ServerContext.ServerClock.ElapsedMilliseconds;
                    if (ServerContext.Day != LunaNetworkTime.Now.Day)
                    {
                        LunaLog.LogFilename = Path.Combine(LunaLog.LogFolder, $"lmpserver {LunaNetworkTime.Now:yyyy-MM-dd HH-mm-ss}.log");
                        LunaLog.Info($"Continued from logfile {LunaNetworkTime.Now:yyyy-MM-dd HH-mm-ss}.log");
                        ServerContext.Day = LunaNetworkTime.Now.Day;
                    }
                }

                try
                {
                    await Task.Delay(PollIntervalMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
