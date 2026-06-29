using System;

namespace FullLogger.Logging
{
    internal enum LogOutputCaptureMode
    {
        Off = 0,
        ErrorsOnly = 1,
        ErrorsAndKeywords = 2,
        All = 3
    }

    internal static class LogOutputCaptureFilter
    {
        private static readonly string[] KeywordMarkers =
        {
            "METABOLISM",
            "POPULATION",
            "EXTRACT",
            "F8",
            "RaidAdmin",
            "CoopHandler",
            "METABOLISM_STUCK",
            "RAID_START",
            "RAID_END",
            "RAID_SNAPSHOT",
            "F8_EXTRACT",
            "desync",
            "Error",
            "Warning",
            "ERROR",
            "WARN",
            "Exception",
            "FATAL"
        };

        internal static bool ShouldCapture(string line, LogOutputCaptureMode mode)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            switch (mode)
            {
                case LogOutputCaptureMode.Off:
                    return false;
                case LogOutputCaptureMode.All:
                    return true;
                case LogOutputCaptureMode.ErrorsOnly:
                    return ContainsErrorMarker(line);
                case LogOutputCaptureMode.ErrorsAndKeywords:
                default:
                    return ContainsErrorMarker(line) || ContainsKeyword(line);
            }
        }

        private static bool ContainsErrorMarker(string line)
        {
            return line.IndexOf("[Error", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("[Warning", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("FATAL", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsKeyword(string line)
        {
            foreach (var marker in KeywordMarkers)
            {
                if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
