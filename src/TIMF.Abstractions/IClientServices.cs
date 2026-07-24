namespace TIMF.Abstractions
{
    /// <summary>
    /// Client-process services. Null on dedicated server.
    /// Prefer this over digging through <see cref="IModContext.Services"/> for UI/hooks.
    /// </summary>
    public interface IClientServices
    {
        /// <summary>Immediate-mode UI (from TIMF.UI library mod). May be null if TIMF.UI is missing.</summary>
        IImmediateModeUi Ui { get; }

        /// <summary>Keybind registry (vanilla Controls integration).</summary>
        IKeybindService Keybinds { get; }

        /// <summary>Local-player ItemCheck prefix hooks.</summary>
        IPlayerUpdateHookRegistry PlayerUpdate { get; }

        /// <summary>Fullscreen / minimap overlay hooks.</summary>
        IMapOverlayHookRegistry MapOverlay { get; }

        /// <summary>Info accessory (GPS-style) text hooks.</summary>
        IInfoAccessoryHookRegistry InfoAccessories { get; }
    }
}
