using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FullLogger.Logging
{
    internal sealed class ModLogStats
    {
        internal int Errors;
        internal int Warnings;
        internal string LastError;
        internal string LastWarning;
    }

    /// <summary>
    /// Aggregates WARN/ERROR counts and writes session_summary.txt on shutdown.
    /// </summary>
    internal static class SessionSummaryWriter
    {
        private static readonly ConcurrentDictionary<string, ModLogStats> ByMod =
            new ConcurrentDictionary<string, ModLogStats>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex HookMod = new Regex(
            @"\[([^\]]+)\]\s*(.+)$",
            RegexOptions.Compiled);

        private static int _totalErrors;
        private static int _totalWarnings;
        private static readonly ConcurrentDictionary<string, int> CategoryLineCounts =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static DateTime? _sessionStarted;
        private static string _sessionDir;

        internal static void BindSession(string sessionDir)
        {
            _sessionDir = sessionDir;
            _sessionStarted = DateTime.Now;
        }

        internal static void Record(string category, string level, string message)
        {
            if (!string.IsNullOrEmpty(category))
            {
                CategoryLineCounts.AddOrUpdate(category, 1, (_, count) => count + 1);
            }

            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            var upper = (level ?? string.Empty).ToUpperInvariant();
            var isError = upper.Contains("ERR") || upper.Contains("FATAL") || upper == "EXCEPTION";
            var isWarn = upper.Contains("WARN");
            if (!isError && !isWarn)
            {
                return;
            }

            var mod = ExtractModName(category, message);
            var stats = ByMod.GetOrAdd(mod, _ => new ModLogStats());
            if (isError)
            {
                System.Threading.Interlocked.Increment(ref _totalErrors);
                stats.Errors++;
                stats.LastError = Truncate(message, 240);
            }
            else
            {
                System.Threading.Interlocked.Increment(ref _totalWarnings);
                stats.Warnings++;
                stats.LastWarning = Truncate(message, 240);
            }
        }

        internal static void WriteSummaryFile()
        {
            if (string.IsNullOrEmpty(_sessionDir))
            {
                return;
            }

            try
            {
                var path = Path.Combine(_sessionDir, "session_summary.txt");
                var sb = new StringBuilder();
                var ended = DateTime.Now;
                sb.AppendLine("Full Logger session summary");
                sb.AppendLine($"Started: {_sessionStarted:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Ended:   {ended:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Duration: {(ended - (_sessionStarted ?? ended)).TotalMinutes:F1} min");
                sb.AppendLine($"Total ERROR: {_totalErrors}");
                sb.AppendLine($"Total WARN:  {_totalWarnings}");
                sb.AppendLine();
                sb.AppendLine("Lines by category (incl. INFO):");
                foreach (var pair in CategoryLineCounts.OrderByDescending(p => p.Value))
                {
                    sb.AppendLine($"  {pair.Key}: {pair.Value}");
                }
                sb.AppendLine();
                sb.AppendLine("By mod (ERROR / WARN):");

                foreach (var pair in ByMod.OrderByDescending(p => p.Value.Errors).ThenByDescending(p => p.Value.Warnings))
                {
                    var s = pair.Value;
                    if (s.Errors == 0 && s.Warnings == 0)
                    {
                        continue;
                    }

                    sb.AppendLine($"  {pair.Key}: ERROR={s.Errors} WARN={s.Warnings}");
                    if (!string.IsNullOrEmpty(s.LastError))
                    {
                        sb.AppendLine($"    last ERROR: {s.LastError}");
                    }

                    if (!string.IsNullOrEmpty(s.LastWarning))
                    {
                        sb.AppendLine($"    last WARN:  {s.LastWarning}");
                    }
                }

                sb.AppendLine();
                sb.AppendLine("Search tips:");
                sb.AppendLine("  python tools/logs/analyze_logs.py --source client2_fulllogger --filter LIV_FIKA");
                sb.AppendLine("  python tools/logs/analyze_logs.py --path \"<session>/part_00001.log\" --filter ERROR");

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

                WriteLatestPointer();
            }
            catch (Exception ex)
            {
                try
                {
                    var crash = Path.Combine(_sessionDir, "session_summary_error.txt");
                    File.WriteAllText(crash, ex.ToString(), Encoding.UTF8);
                }
                catch
                {
                    // ignore
                }
            }
        }

        private static void WriteLatestPointer()
        {
            if (string.IsNullOrEmpty(_sessionDir))
            {
                return;
            }

            var sessionsRoot = Directory.GetParent(_sessionDir)?.FullName;
            if (string.IsNullOrEmpty(sessionsRoot))
            {
                return;
            }

            var folderName = Path.GetFileName(_sessionDir);
            var pointerPath = Path.Combine(sessionsRoot, "latest_session.txt");
            var text = new StringBuilder();
            text.AppendLine(folderName);
            text.AppendLine(_sessionDir);
            text.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            File.WriteAllText(pointerPath, text.ToString(), new UTF8Encoding(false));
        }

        private static string ExtractModName(string category, string message)
        {
            var match = HookMod.Match(message.Trim());
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            if (!string.IsNullOrEmpty(category) &&
                !string.Equals(category, LogCategories.BepInEx, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(category, LogCategories.BepInExFile, StringComparison.OrdinalIgnoreCase))
            {
                return category;
            }

            return "unknown";
        }

        private static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
            {
                return text;
            }

            return text.Substring(0, max - 3) + "...";
        }
    }
}
