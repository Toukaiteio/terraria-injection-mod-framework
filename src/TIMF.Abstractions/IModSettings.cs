namespace TIMF.Abstractions
{
    /// <summary>
    /// Implement this on your <see cref="IMod"/> class to contribute a settings page.
    /// The settings hub calls <see cref="BuildSettingsUI"/> each frame while your page is open.
    ///
    /// IMPORTANT: build widgets directly on the provided <paramref name="ui"/> — do NOT call
    /// <c>ui.Begin</c>/<c>ui.End</c>; your widgets are appended into the hub's already-open window.
    /// </summary>
    public interface IModSettings
    {
        void BuildSettingsUI(IImmediateModeUi ui);
    }
}
