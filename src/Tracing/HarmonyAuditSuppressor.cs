namespace FullLogger.Tracing
{
    /// <summary>
    /// Подавляет Harmony audit при массовой установке тысяч патчей (иначе зависание).
    /// </summary>
    internal static class HarmonyAuditSuppressor
    {
        internal static bool Suppress { get; private set; }

        internal static void EnterBulk() => Suppress = true;

        internal static void ExitBulk() => Suppress = false;
    }
}
