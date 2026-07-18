namespace TIMF.Abstractions
{
    /// <summary>
    /// A registered hotkey that participates in the vanilla PlayerInput pipeline
    /// (Settings → Controls, rebinding, save/load).
    /// </summary>
    public interface IKeybind
    {
        /// <summary>Stable unique id, e.g. "TIMF.BossCursor.Toggle".</summary>
        string Id { get; }

        /// <summary>Label shown in the vanilla keybinding UI.</summary>
        string DisplayName { get; }

        /// <summary>True while the bound key is held this frame.</summary>
        bool Current { get; }

        /// <summary>True on the frame the key was pressed.</summary>
        bool JustPressed { get; }

        /// <summary>True on the frame the key was released.</summary>
        bool JustReleased { get; }

        /// <summary>Human-readable current binding (e.g. "Insert"), or empty if unbound.</summary>
        string CurrentBindingDisplay { get; }
    }
}
