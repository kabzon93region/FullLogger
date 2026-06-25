using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace FullLogger.Logging
{
    /// <summary>
    /// Dedupes BepInEx hook lines vs LogOutput tail and duplicate hook invocations.
    /// </summary>
    internal static class LogCaptureCoordinator
    {
        private static readonly ConcurrentDictionary<string, long> RecentKeys =
            new ConcurrentDictionary<string, long>(StringComparer.Ordinal);

        private static long _ops;

        private static readonly Regex BepInExFileLine = new Regex(
            @"^\[(Info|Warning|Error|Message|Debug|Fatal)\s*:([^\]]+)\]\s*(.*)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static bool ShouldWrite(string channel, string level, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            var key = BuildDedupeKey(level, message);
            var now = (long)Environment.TickCount;

            if (RecentKeys.TryGetValue(key, out var seenAt) && now - seenAt < 3000)
            {
                return false;
            }

            RecentKeys[key] = now;

            if (++_ops % 2048 == 0)
            {
                PruneStale(now);
            }

            return true;
        }

        internal static string NormalizeLogOutputLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return line;
            }

            var match = BepInExFileLine.Match(line.Trim());
            if (!match.Success)
            {
                return line;
            }

            var level = NormalizeLevel(match.Groups[1].Value);
            var source = match.Groups[2].Value.Trim();
            var text = match.Groups[3].Value;
            return $"[{source}] {text}";
        }

        internal static string FormatHookLine(string sourceName, string level, string text)
        {
            return $"[{sourceName}] {text}";
        }

        private static string BuildDedupeKey(string level, string message)
        {
            var normalized = message.Trim();
            var match = BepInExFileLine.Match(normalized);
            if (match.Success)
            {
                normalized = FormatHookLine(
                    match.Groups[2].Value.Trim(),
                    NormalizeLevel(match.Groups[1].Value),
                    match.Groups[3].Value);
            }

            return $"{NormalizeLevel(level)}|{normalized}";
        }

        private static string NormalizeLevel(string level)
        {
            if (string.IsNullOrEmpty(level))
            {
                return "INFO";
            }

            var text = level.Trim().ToUpperInvariant();
            if (text.StartsWith("WARN", StringComparison.Ordinal))
            {
                return "WARN";
            }

            if (text.StartsWith("ERR", StringComparison.Ordinal) || text.StartsWith("FATAL", StringComparison.Ordinal))
            {
                return "ERROR";
            }

            if (text.StartsWith("DBG", StringComparison.Ordinal))
            {
                return "DEBUG";
            }

            return text;
        }

        private static void PruneStale(long now)
        {
            foreach (var pair in RecentKeys)
            {
                if (now - pair.Value > 10000)
                {
                    RecentKeys.TryRemove(pair.Key, out _);
                }
            }
        }
    }
}
