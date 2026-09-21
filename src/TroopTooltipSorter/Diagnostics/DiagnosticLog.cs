using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace TroopTooltipSorter.Diagnostics
{
    /// <summary>
    /// Opt-in tracing, for working out which vanilla code path actually builds a given tooltip.
    ///
    /// Switched on by dropping a file named "diagnostics.enabled" in the module root. With that
    /// file absent this costs one bool check per call and writes nothing, so it is safe to leave
    /// compiled in.
    ///
    /// Tooltips re-render constantly, so entries are written once per distinct signature rather
    /// than once per call - otherwise a few seconds of hovering would bury the interesting lines.
    /// </summary>
    internal static class DiagnosticLog
    {
        private const int MaxEntries = 500;

        private static readonly object Gate = new object();
        private static readonly HashSet<string> SeenSignatures = new HashSet<string>(StringComparer.Ordinal);

        private static bool _enabled;
        private static string _logPath;
        private static int _entries;

        internal static bool Enabled => _enabled;

        internal static void Initialize()
        {
            try
            {
                string moduleRoot = ResolveModuleRoot();
                if (moduleRoot == null || !File.Exists(Path.Combine(moduleRoot, "diagnostics.enabled")))
                {
                    return;
                }

                _logPath = Path.Combine(moduleRoot, "diagnostics.log");
                File.WriteAllText(
                    _logPath,
                    $"# {SubModule.ModuleName} trace - {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
                _enabled = true;

                // A hard crash otherwise leaves nothing behind but a dump we cannot read.
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            }
            catch
            {
                // Tracing must never be the reason the mod fails to load.
                _enabled = false;
            }
        }

        internal static void Write(string body)
        {
            if (!_enabled)
            {
                return;
            }

            lock (Gate)
            {
                AppendLocked(body);
            }
        }

        /// <summary>Writes an entry the first time a given signature is seen.</summary>
        internal static void WriteOnce(string signature, Func<string> body)
        {
            if (!_enabled)
            {
                return;
            }

            lock (Gate)
            {
                if (_entries >= MaxEntries || !SeenSignatures.Add(signature))
                {
                    return;
                }

                _entries++;
                AppendLocked(body());
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Write($"[fatal] unhandled exception (terminating={e.IsTerminating}){Environment.NewLine}{e.ExceptionObject}");
        }

        private static string ResolveModuleRoot()
        {
            // <module>/bin/Win64_Shipping_Client/TroopTooltipSorter.dll -> <module>
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(assemblyPath))
            {
                return null;
            }

            return Directory.GetParent(assemblyPath)?.Parent?.Parent?.FullName;
        }

        private static void AppendLocked(string body)
        {
            try
            {
                File.AppendAllText(_logPath, body + Environment.NewLine);
            }
            catch
            {
                // Ignore - a locked or full disk is not worth breaking a tooltip over.
            }
        }
    }
}
