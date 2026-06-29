using System.Reflection;
using EFT.HealthSystem;

namespace FullLogger.Monitoring
{
    internal static class MetabolismReflection
    {
        private static FieldInfo _metabolismDisabledField;

        internal static FieldInfo MetabolismDisabledField =>
            _metabolismDisabledField ?? (_metabolismDisabledField = ResolveMetabolismDisabledField());

        private static FieldInfo ResolveMetabolismDisabledField()
        {
            var type = typeof(ActiveHealthController);
            while (type != null)
            {
                var field = type.GetField("Boolean_0", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }
    }
}
