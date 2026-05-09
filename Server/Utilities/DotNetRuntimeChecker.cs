using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Server.Utilities
{
    /// <summary>
    /// Verifies that the server is running on the expected .NET runtime version.
    /// </summary>
    /// <remarks>
    /// This check only runs after the native apphost has already succeeded in loading a
    /// runtime. If no compatible .NET is installed at all, apphost prints its own error
    /// and exits before any managed code (including this class) gets a chance to run.
    /// </remarks>
    internal static class DotNetRuntimeChecker
    {
        /// <summary>
        /// Major version of the .NET runtime the server is built against (see TargetFramework in Server.csproj).
        /// </summary>
        private const int RequiredMajorVersion = 10;

        /// <summary>
        /// Friendly name of the required runtime, shown to the user if the check fails.
        /// </summary>
        private const string RequiredRuntimeName = ".NET 10 Runtime";

        /// <summary>
        /// Official Microsoft download page for the required runtime.
        /// </summary>
        private const string RuntimeDownloadUrl = "https://dotnet.microsoft.com/en-us/download/dotnet/10.0";

        /// <summary>
        /// Name of the shared framework the base .NET runtime ships under.
        /// </summary>
        private const string BaseRuntimeMoniker = "Microsoft.NETCore.App";

        /// <summary>
        /// Ensures the currently executing .NET runtime matches the required major version.
        /// If it does not, a clear message is written to the console and the process blocks
        /// on a key press (so the window doesn't vanish when the user double-clicked the exe)
        /// before exiting.
        /// </summary>
        public static void EnsureCorrectRuntimeOrExit()
        {
            var currentVersion = Environment.Version;
            if (currentVersion.Major == RequiredMajorVersion)
                return;

            WriteIncorrectRuntimeMessage(currentVersion);
            BlockUntilKeyPress();
            Environment.Exit(1);
        }

        private static void WriteIncorrectRuntimeMessage(Version currentVersion)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("========================================================================");
            Console.Error.WriteLine(" ERROR: Incorrect .NET runtime detected.");
            Console.Error.WriteLine("------------------------------------------------------------------------");
            Console.Error.WriteLine($" LunaServer requires the {RequiredRuntimeName} to run.");
            Console.Error.WriteLine($" Detected runtime version: {currentVersion}");
            Console.Error.WriteLine($" Process architecture:     {RuntimeInformation.ProcessArchitecture}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(" Please download and install the correct runtime from:");
            Console.Error.WriteLine($"   {RuntimeDownloadUrl}");
            Console.Error.WriteLine();
            Console.Error.WriteLine(" On that page, pick the \"Runtime\" (or \"ASP.NET Core Runtime\") download");
            Console.Error.WriteLine(" that matches your operating system and architecture, then re-run the");
            Console.Error.WriteLine(" server.");

            WriteInstalledRuntimeDiagnostics();

            Console.Error.WriteLine("========================================================================");
            Console.Error.WriteLine();
        }

        private static void WriteInstalledRuntimeDiagnostics()
        {
            if (!TryListInstalledRuntimes(out var installedRuntimes))
                return;

            Console.Error.WriteLine();
            Console.Error.WriteLine(" Installed .NET runtimes detected on this machine:");

            if (installedRuntimes.Count == 0)
            {
                Console.Error.WriteLine("   (none reported by 'dotnet --list-runtimes')");
                return;
            }

            foreach (var runtime in installedRuntimes)
                Console.Error.WriteLine($"   {runtime}");

            var hasAnyRequired = false;
            var hasBaseRequired = false;
            foreach (var runtime in installedRuntimes)
            {
                if (!LooksLikeMajor(runtime, RequiredMajorVersion))
                    continue;

                hasAnyRequired = true;
                if (runtime.StartsWith(BaseRuntimeMoniker + " ", StringComparison.OrdinalIgnoreCase))
                    hasBaseRequired = true;
            }

            Console.Error.WriteLine();
            if (hasAnyRequired && !hasBaseRequired)
            {
                Console.Error.WriteLine($" NOTE: A .NET {RequiredMajorVersion} install is present, but the base '{BaseRuntimeMoniker}'");
                Console.Error.WriteLine(" runtime that LunaServer needs is missing. This typically happens when");
                Console.Error.WriteLine(" only the SDK listing or a specialized runtime (Desktop / ASP.NET Core) got picked up.");
                Console.Error.WriteLine($" Install the plain \".NET Runtime\" {RequiredMajorVersion}.x from the page above.");
            }
            else if (!hasAnyRequired)
            {
                Console.Error.WriteLine($" NOTE: No .NET {RequiredMajorVersion}.x runtime was found. The versions listed above are");
                Console.Error.WriteLine($" not compatible with this server - install .NET {RequiredMajorVersion} as described.");
            }
        }

        private static bool LooksLikeMajor(string runtimeLine, int requiredMajor)
        {
            var firstSpace = runtimeLine.IndexOf(' ');
            if (firstSpace < 0 || firstSpace + 1 >= runtimeLine.Length)
                return false;

            var afterMoniker = runtimeLine.Substring(firstSpace + 1);
            var versionEnd = afterMoniker.IndexOf(' ');
            var versionToken = versionEnd > 0 ? afterMoniker.Substring(0, versionEnd) : afterMoniker;
            var dot = versionToken.IndexOf('.');
            if (dot <= 0) return false;

            return int.TryParse(versionToken.Substring(0, dot), out var major) && major == requiredMajor;
        }

        private static bool TryListInstalledRuntimes(out List<string> runtimes)
        {
            runtimes = new List<string>();
            try
            {
                var psi = new ProcessStartInfo("dotnet", "--list-runtimes")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return false;

                var stdout = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(5000))
                {
                    try { proc.Kill(); } catch { /* best-effort */ }
                    return false;
                }

                foreach (var rawLine in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var line = rawLine.Trim();
                    var bracket = line.IndexOf('[');
                    if (bracket > 0) line = line.Substring(0, bracket).TrimEnd();
                    if (line.Length > 0) runtimes.Add(line);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void BlockUntilKeyPress()
        {
            Console.Error.WriteLine("Press any key to exit...");

            if (TryBlockOnReadKey())
                return;

            if (TryBlockOnReadLine())
                return;

            try { Thread.Sleep(TimeSpan.FromMinutes(5)); } catch { /* shutdown requested */ }
        }

        private static bool TryBlockOnReadKey()
        {
            try
            {
                if (Console.IsInputRedirected)
                    return false;
                Console.ReadKey(true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryBlockOnReadLine()
        {
            try
            {
                Console.In.ReadLine();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
