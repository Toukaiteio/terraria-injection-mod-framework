namespace TIMF.Abstractions
{
    /// <summary>
    /// Optional settings page for the Mod Settings hub.
    /// Client-process only (UI). Authority-only mods that implement this still need a
    /// client host session (SP/Host) to open the hub — dedicated servers have no UI.
    ///
    /// IMPORTANT: build widgets on <paramref name="ui"/> only — do not call Begin/End.
    /// </summary>
    [TimfHook(TimfSide.Client)]
    public interface IModSettings
    {
        void BuildSettingsUI(IImmediateModeUi ui);
    }
}
