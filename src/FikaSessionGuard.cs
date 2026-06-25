using System;
using System.Reflection;
using HarmonyLib;

namespace FullLogger
{
    /// <summary>
    /// Avoid FikaGlobals.GetProfile / GetSession — they log errors when TarkovApplication is not ready.
    /// </summary>
    internal static class FikaSessionGuard
    {
        private static Type _tarkovApplicationType;

        internal static bool IsTarkovApplicationReady()
        {
            try
            {
                _tarkovApplicationType ??= AccessTools.TypeByName("EFT.TarkovApplication");
                if (_tarkovApplicationType == null)
                {
                    return false;
                }

                foreach (var method in _tarkovApplicationType.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    if (!string.Equals(method.Name, "Exist", StringComparison.Ordinal)
                        || method.GetParameters().Length != 1)
                    {
                        continue;
                    }

                    var args = new object[1];
                    if (method.Invoke(null, args) is bool exists && exists && args[0] != null)
                    {
                        return true;
                    }

                    return false;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        internal static object TryGetSessionProfile()
        {
            if (!IsTarkovApplicationReady())
            {
                return null;
            }

            try
            {
                _tarkovApplicationType ??= AccessTools.TypeByName("EFT.TarkovApplication");
                if (_tarkovApplicationType == null)
                {
                    return null;
                }

                object tarkovApp = null;
                foreach (var method in _tarkovApplicationType.GetMethods(BindingFlags.Static | BindingFlags.Public))
                {
                    if (!string.Equals(method.Name, "Exist", StringComparison.Ordinal)
                        || method.GetParameters().Length != 1)
                    {
                        continue;
                    }

                    var args = new object[1];
                    if (method.Invoke(null, args) is bool exists && exists)
                    {
                        tarkovApp = args[0];
                    }

                    break;
                }

                if (tarkovApp == null)
                {
                    return null;
                }

                var session = tarkovApp.GetType().GetProperty("Session")?.GetValue(tarkovApp);
                return session?.GetType().GetProperty("Profile")?.GetValue(session);
            }
            catch
            {
                return null;
            }
        }
    }
}
