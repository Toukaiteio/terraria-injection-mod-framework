namespace TIMF.Abstractions
{
    /// <summary>
    /// Optional session-level state for an <see cref="IModInfo"/>. Kept separate from the
    /// original interface so adding session policy does not break existing API consumers.
    /// Core-provided registry entries implement both interfaces.
    /// </summary>
    public interface IModSessionState
    {
        /// <summary>
        /// True when the current world/server permits this mod to execute. Independent from
        /// the persisted <see cref="IModInfo.IsEnabled"/> user preference.
        /// </summary>
        bool IsSessionAllowed { get; }

        /// <summary>Whether the registry enable switch may be changed right now.</summary>
        bool CanChangeEnabled { get; }

        /// <summary>Human-readable reason for a session/menu lock, or null when unrestricted.</summary>
        string InteractionLockReason { get; }

        /// <summary>True when the discovered entry type provides an IModSettings page.</summary>
        bool HasSettingsCapability { get; }

        /// <summary>True only when opening and operating the settings page is currently safe.</summary>
        bool CanOpenSettings { get; }
    }
}
