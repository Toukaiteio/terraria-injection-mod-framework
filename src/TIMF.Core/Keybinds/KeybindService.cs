using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework.Input;
using Terraria;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using TIMF.Abstractions;

namespace TIMF.Core.Keybinds
{
    /// <summary>
    /// Registers mod hotkeys into the vanilla PlayerInput pipeline:
    /// KnownTriggers + profile KeyStatus + TriggersSet.KeyStatus + Controls UI group.
    /// </summary>
    internal sealed class KeybindService : IKeybindService
    {
        private readonly ILogger _log;
        private readonly Dictionary<string, KeybindEntry> _entries =
            new Dictionary<string, KeybindEntry>(StringComparer.Ordinal);

        // Snapshot used by UIManageControls postfix (order of registration).
        private readonly List<string> _orderedIds = new List<string>();

        private bool _loggedInjectFail;

        public KeybindService(ILogger log)
        {
            _log = log;
        }

        internal IReadOnlyList<string> OrderedIds => _orderedIds;

        internal sealed class ModGroup
        {
            public string ModId;
            public string Title;
            public List<string> Ids = new List<string>();
        }

        /// <summary>
        /// Group registered keybinds by mod id (prefix before first '.').
        /// Title prefers the first entry's display name's leading words, else the mod id.
        /// </summary>
        internal List<ModGroup> GetGroupsByMod()
        {
            var order = new List<string>();
            var map = new Dictionary<string, ModGroup>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < _orderedIds.Count; i++)
            {
                var id = _orderedIds[i];
                KeybindEntry e;
                if (!_entries.TryGetValue(id, out e))
                    continue;

                var modId = ExtractModId(id);
                ModGroup g;
                if (!map.TryGetValue(modId, out g))
                {
                    g = new ModGroup
                    {
                        ModId = modId,
                        Title = PrettyModTitle(modId, e.DisplayName),
                    };
                    map[modId] = g;
                    order.Add(modId);
                }

                g.Ids.Add(id);
            }

            var result = new List<ModGroup>(order.Count);
            for (var i = 0; i < order.Count; i++)
                result.Add(map[order[i]]);
            return result;
        }

        private static string ExtractModId(string keybindId)
        {
            if (string.IsNullOrEmpty(keybindId))
                return "TIMF";
            var dot = keybindId.IndexOf('.');
            if (dot <= 0)
                return keybindId;
            return keybindId.Substring(0, dot);
        }

        private static string PrettyModTitle(string modId, string firstDisplayName)
        {
            // Prefer a clean mod-facing title. Display names look like "Boss Cursor Toggle" —
            // strip a trailing " Toggle" if present; else use the mod id as-is.
            if (!string.IsNullOrEmpty(firstDisplayName))
            {
                var t = firstDisplayName.Trim();
                if (t.EndsWith(" Toggle", StringComparison.OrdinalIgnoreCase) && t.Length > 7)
                    return t.Substring(0, t.Length - 7).Trim();
                // If display is just the action, fall through to mod id.
            }

            return string.IsNullOrEmpty(modId) ? "TIMF Mod" : modId;
        }

        internal bool TryGetDisplayName(string id, out string displayName)
        {
            KeybindEntry e;
            if (_entries.TryGetValue(id, out e))
            {
                displayName = e.DisplayName;
                return true;
            }

            displayName = null;
            return false;
        }

        public IKeybind Register(string id, string displayName, Keys defaultKey)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Keybind id is required", "id");
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = id;

            KeybindEntry existing;
            if (_entries.TryGetValue(id, out existing))
            {
                // Keep the first registration; still ensure native inject is present.
                InjectNative(id, existing.DefaultKeyName);
                return existing;
            }

            var keyName = defaultKey == Keys.None ? "" : defaultKey.ToString();
            var entry = new KeybindEntry(this, id, displayName, keyName);
            _entries[id] = entry;
            _orderedIds.Add(id);

            InjectNative(id, keyName);
            _log.Info("Keybind registered: " + id + " default=" + (string.IsNullOrEmpty(keyName) ? "(none)" : keyName) +
                      " label=\"" + displayName + "\"");
            return entry;
        }

        public void Unregister(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;

            if (!_entries.Remove(id))
                return;

            _orderedIds.Remove(id);
            // Leave KnownTriggers / profile entries in place so saved bindings stay valid
            // if the mod reloads mid-session; they simply become unused.
            _log.Info("Keybind unregistered: " + id);
        }

        public IKeybind Get(string id)
        {
            KeybindEntry e;
            return _entries.TryGetValue(id, out e) ? e : null;
        }

        public bool TryGet(string id, out IKeybind keybind)
        {
            KeybindEntry e;
            if (_entries.TryGetValue(id, out e))
            {
                keybind = e;
                return true;
            }

            keybind = null;
            return false;
        }

        /// <summary>
        /// Re-apply every registered keybind into the live PlayerInput state.
        /// Call after <c>PlayerInput.Initialize</c> (or profile reload) so late-loaded
        /// profiles still contain our triggers.
        /// </summary>
        internal void ReinjectAll()
        {
            foreach (var kv in _entries)
                InjectNative(kv.Key, kv.Value.DefaultKeyName);
        }

        internal void LogInfo(string msg)
        {
            try { _log.Info(msg); }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Ensure the trigger exists in KnownTriggers and all live KeyStatus dictionaries.
        /// Safe to call repeatedly.
        /// </summary>
        private void InjectNative(string id, string defaultKeyName)
        {
            try
            {
                // PlayerInput static ctor always fills KnownTriggers; if it's null we are too early.
                if (PlayerInput.KnownTriggers == null)
                    return;

                // 1) KnownTriggers — drives SetupKeys for new profiles / re-init.
                if (!PlayerInput.KnownTriggers.Contains(id))
                    PlayerInput.KnownTriggers.Add(id);

                // Prefer full-width rows in Controls UI (like Inventory / map keys).
                try
                {
                    var fullLine = typeof(UIManageControls)
                        .GetField("_BindingsFullLine", BindingFlags.NonPublic | BindingFlags.Static);
                    var list = fullLine != null ? fullLine.GetValue(null) as List<string> : null;
                    if (list != null && !list.Contains(id))
                        list.Add(id);
                }
                catch
                {
                    // non-fatal
                }

                // 2) Live TriggersSet dictionaries (Current / Old / JustPressed / JustReleased).
                // Triggers may be uninitialised until PlayerInput.Initialize runs.
                if (PlayerInput.Triggers != null)
                {
                    EnsureTriggersDict(PlayerInput.Triggers.Current, id);
                    EnsureTriggersDict(PlayerInput.Triggers.Old, id);
                    EnsureTriggersDict(PlayerInput.Triggers.JustPressed, id);
                    EnsureTriggersDict(PlayerInput.Triggers.JustReleased, id);
                }

                // 3) All profiles (current + originals): every InputMode KeyStatus list.
                InjectProfiles(PlayerInput.Profiles, id, defaultKeyName);
                InjectProfiles(PlayerInput.OriginalProfiles, id, defaultKeyName);
            }
            catch (Exception ex)
            {
                if (!_loggedInjectFail)
                {
                    _loggedInjectFail = true;
                    _log.Error("Keybind InjectNative failed for " + id, ex);
                }
            }
        }

        private static void EnsureTriggersDict(TriggersSet set, string id)
        {
            if (set == null || set.KeyStatus == null)
                return;
            if (!set.KeyStatus.ContainsKey(id))
                set.KeyStatus[id] = false;
        }

        private static void InjectProfiles(
            Dictionary<string, PlayerInputProfile> profiles,
            string id,
            string defaultKeyName)
        {
            if (profiles == null)
                return;

            foreach (var kv in profiles)
            {
                var profile = kv.Value;
                if (profile == null || profile.InputModes == null)
                    continue;

                foreach (var modeKv in profile.InputModes)
                {
                    var cfg = modeKv.Value;
                    if (cfg == null || cfg.KeyStatus == null)
                        continue;

                    if (!cfg.KeyStatus.ContainsKey(id))
                    {
                        var list = new List<string>();
                        // Only seed keyboard modes with a default physical key.
                        // Gamepad modes start unbound so users can assign if desired.
                        if ((modeKv.Key == InputMode.Keyboard || modeKv.Key == InputMode.KeyboardUI)
                            && !string.IsNullOrEmpty(defaultKeyName))
                        {
                            list.Add(defaultKeyName);
                        }

                        cfg.KeyStatus[id] = list;
                    }
                }
            }
        }

        internal static bool ReadStatus(string id, bool justPressed, bool justReleased)
        {
            try
            {
                TriggersSet set;
                if (justPressed)
                    set = PlayerInput.Triggers.JustPressed;
                else if (justReleased)
                    set = PlayerInput.Triggers.JustReleased;
                else
                    set = PlayerInput.Triggers.Current;

                bool value;
                if (set != null && set.KeyStatus != null && set.KeyStatus.TryGetValue(id, out value))
                    return value;
            }
            catch
            {
                // ignore
            }

            return false;
        }

        internal static string ReadBindingDisplay(string id)
        {
            try
            {
                var profile = PlayerInput.CurrentProfile;
                if (profile == null || profile.InputModes == null)
                    return "";

                KeyConfiguration cfg;
                if (!profile.InputModes.TryGetValue(InputMode.Keyboard, out cfg) || cfg == null)
                    return "";

                List<string> list;
                if (!cfg.KeyStatus.TryGetValue(id, out list) || list == null || list.Count == 0)
                    return "";

                // Prefer first non-empty binding; vanilla stores XNA Keys names ("Insert", "F9").
                for (var i = 0; i < list.Count; i++)
                {
                    if (!string.IsNullOrEmpty(list[i]))
                        return list[i];
                }
            }
            catch
            {
                // ignore
            }

            return "";
        }

        private sealed class KeybindEntry : IKeybind
        {
            private readonly KeybindService _owner;

            public KeybindEntry(KeybindService owner, string id, string displayName, string defaultKeyName)
            {
                _owner = owner;
                Id = id;
                DisplayName = displayName;
                DefaultKeyName = defaultKeyName ?? "";
            }

            public string Id { get; }
            public string DisplayName { get; }
            public string DefaultKeyName { get; }

            public bool Current
            {
                get { return ReadStatus(Id, false, false); }
            }

            public bool JustPressed
            {
                get { return ReadStatus(Id, true, false); }
            }

            public bool JustReleased
            {
                get { return ReadStatus(Id, false, true); }
            }

            public string CurrentBindingDisplay
            {
                get { return ReadBindingDisplay(Id); }
            }
        }
    }
}
