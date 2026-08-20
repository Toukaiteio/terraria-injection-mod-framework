using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using TIMF.Abstractions;

namespace TIMF.Bridge
{
    /// <summary>
    /// A keybind bound to a fixed default key, evaluated by polling the keyboard each frame.
    ///
    /// v1 does not integrate with tModLoader's Settings → Controls rebinding UI (that requires
    /// registering during the mod load window and differs per tML version); instead the bridge
    /// polls the default key directly. <see cref="CurrentBindingDisplay"/> reports that key name.
    /// The host suppresses input while chat / sign / chest text entry or the TIMF UI has keyboard
    /// focus, so toggles do not fire while typing.
    /// </summary>
    internal sealed class BridgeKeybind : IKeybind
    {
        private bool _current;
        private bool _previous;

        public BridgeKeybind(string id, string displayName, Keys key)
        {
            Id = id;
            DisplayName = displayName;
            Key = key;
        }

        public Keys Key { get; }
        public string Id { get; }
        public string DisplayName { get; }

        public bool Current => _current;
        public bool JustPressed => _current && !_previous;
        public bool JustReleased => !_current && _previous;
        public string CurrentBindingDisplay => Key == Keys.None ? string.Empty : Key.ToString();

        /// <summary>Advance the one-frame edge state from this frame's raw key-down reading.</summary>
        public void Update(bool down)
        {
            _previous = _current;
            _current = down;
        }
    }

    internal sealed class BridgeKeybindService : IKeybindService
    {
        private readonly Dictionary<string, BridgeKeybind> _binds =
            new Dictionary<string, BridgeKeybind>(StringComparer.OrdinalIgnoreCase);

        public IKeybind Register(string id, string displayName, Keys defaultKey)
        {
            if (string.IsNullOrEmpty(id))
                return new BridgeKeybind(id, displayName, defaultKey);
            BridgeKeybind existing;
            if (_binds.TryGetValue(id, out existing))
                return existing;
            var kb = new BridgeKeybind(id, displayName, defaultKey);
            _binds[id] = kb;
            return kb;
        }

        public void Unregister(string id)
        {
            if (!string.IsNullOrEmpty(id))
                _binds.Remove(id);
        }

        public IKeybind Get(string id)
        {
            BridgeKeybind kb;
            return !string.IsNullOrEmpty(id) && _binds.TryGetValue(id, out kb) ? kb : null;
        }

        public bool TryGet(string id, out IKeybind keybind)
        {
            keybind = Get(id);
            return keybind != null;
        }

        /// <summary>
        /// Called once per frame by the host. <paramref name="inputAllowed"/> is false while the
        /// game is capturing text (chat / sign / chest) or the TIMF UI wants the keyboard, so held
        /// keys read as up and no toggle edge fires during typing.
        /// </summary>
        public void Poll(bool inputAllowed)
        {
            KeyboardState ks;
            try { ks = Keyboard.GetState(); }
            catch { return; }

            foreach (var kb in _binds.Values)
            {
                var down = inputAllowed && kb.Key != Keys.None && ks.IsKeyDown(kb.Key);
                kb.Update(down);
            }
        }
    }
}
