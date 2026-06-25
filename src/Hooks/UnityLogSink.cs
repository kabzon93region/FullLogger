using System;
using FullLogger;
using FullLogger.Logging;
using UnityEngine;

namespace FullLogger.Hooks
{
    internal static class UnityLogSink
    {
        private static bool _installed;

        internal static void Install()
        {
            if (_installed)
            {
                return;
            }

            Application.logMessageReceivedThreaded += OnLogMessage;
            _installed = true;
            SessionBootstrap.Write(LogCategories.Unity, "INFO", "Unity log sink installed");
        }

        internal static void Uninstall()
        {
            if (!_installed)
            {
                return;
            }

            Application.logMessageReceivedThreaded -= OnLogMessage;
            _installed = false;
        }

        private static void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            var level = MapLevel(type);
            var message = condition;
            if (type == LogType.Exception || type == LogType.Error)
            {
                if (!string.IsNullOrEmpty(stackTrace))
                {
                    message = condition + Environment.NewLine + stackTrace;
                }
            }

            SessionBootstrap.Write(LogCategories.Unity, level, message);
        }

        private static string MapLevel(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                    return "ERROR";
                case LogType.Assert:
                    return "ASSERT";
                case LogType.Warning:
                    return "WARN";
                default:
                    return "INFO";
            }
        }
    }
}
