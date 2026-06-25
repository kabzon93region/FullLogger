using System;
using System.Collections;
using System.Reflection;
using System.Text;

namespace FullLogger.Logging
{
    internal static class TraceFormatter
    {
        private const int MaxArgLength = 4096;
        private const int MaxCollectionItems = 32;

        [ThreadStatic]
        private static bool _formatting;

        internal static bool IsFormatting => _formatting;

        internal static string FormatMethod(MethodBase method, object instance, object[] args, long elapsedMs, Exception ex)
        {
            if (_formatting)
            {
                return SafeMethodLabel(method);
            }

            _formatting = true;
            try
            {
            var sb = new StringBuilder(512);
            sb.Append(method.DeclaringType?.FullName ?? "?");
            sb.Append('.');
            sb.Append(method.Name);
            sb.Append('(');
            sb.Append(FormatArgs(method, args));
            sb.Append(')');

            if (instance != null && !method.IsStatic)
            {
                sb.Append(" @inst=");
                sb.Append(Truncate(SafeObjectLabel(instance)));
            }

            if (elapsedMs >= 0)
            {
                sb.Append(" elapsedMs=");
                sb.Append(elapsedMs);
            }

            if (ex != null)
            {
                sb.Append(" EX=");
                sb.Append(ex.GetType().Name);
                sb.Append(": ");
                sb.Append(Truncate(ex.Message));
            }

            return sb.ToString();
            }
            finally
            {
                _formatting = false;
            }
        }

        private static string SafeMethodLabel(MethodBase method)
        {
            if (method == null)
            {
                return "?";
            }

            return (method.DeclaringType?.FullName ?? "?") + "." + method.Name;
        }

        private static string SafeObjectLabel(object value)
        {
            if (value == null)
            {
                return "null";
            }

            try
            {
                var type = value.GetType();
                return type.FullName + "#" + value.GetHashCode().ToString();
            }
            catch
            {
                return value.GetType().Name;
            }
        }

        private static string FormatArgs(MethodBase method, object[] args)
        {
            if (args == null || args.Length == 0)
            {
                return string.Empty;
            }

            var parameters = method.GetParameters();
            var sb = new StringBuilder();
            for (var i = 0; i < args.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                var name = i < parameters.Length ? parameters[i].Name : $"arg{i}";
                sb.Append(name);
                sb.Append('=');
                sb.Append(FormatValue(args[i]));
            }

            return sb.ToString();
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is string s)
            {
                return Truncate($"\"{s}\"");
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                var sb = new StringBuilder("[");
                var count = 0;
                foreach (var item in enumerable)
                {
                    if (count >= MaxCollectionItems)
                    {
                        sb.Append("…");
                        break;
                    }

                    if (count > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(item == null ? "null" : SafeObjectLabel(item));
                    count++;
                }

                sb.Append(']');
                return Truncate(sb.ToString());
            }

            return Truncate(SafeObjectLabel(value));
        }

        internal static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            if (text.Length <= MaxArgLength)
            {
                return text;
            }

            return text.Substring(0, MaxArgLength) + "…";
        }
    }
}
