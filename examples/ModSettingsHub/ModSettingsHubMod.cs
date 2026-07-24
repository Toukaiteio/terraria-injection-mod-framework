using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;

namespace ModSettingsHub
{
    /// <summary>
    /// Central settings window: lists all TIMF mods (with side / enable state) and opens a
    /// floating settings page for the selected mod (mods that implement <see cref="IModSettings"/>).
    /// </summary>
    [TimfMod(Id = "ModSettingsHub", Side = TimfSide.Client)]
    [TimfDependsOn("TIMF.UI", MinVersion = "1.0.0")]
    public sealed class ModSettingsHubMod : IClientMod
    {
        private const string ToggleId = "ModSettingsHub.Toggle";
        private IModContext _ctx;
        private IImmediateModeUi _ui;
        private IModRegistry _registry;
        private IKeybind _toggle;
        private IKeybindService _keybinds;
        private bool _windowOpen = true;
        private string _selectedId;
        private bool _settingsOpen;
        private string _settingsTitle = "Mod Settings";
        private bool _announcePending = true;
        private string _statusLine;

        public string Name => "Mod Settings";
        public string Version => "1.1.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            _ui = context.Client != null ? context.Client.Ui : null;
            if (_ui == null)
                context.Log.Error("IClientServices.Ui unavailable — TIMF.UI missing?");

            _keybinds = context.Client != null ? context.Client.Keybinds : null;
            if (_keybinds != null)
                _toggle = _keybinds.Register(ToggleId, context.L.Get("Keybind.Toggle", "Mod Settings Toggle"), Keys.F9);
            else
                context.Log.Error("IKeybindService unavailable — ModSettingsHub toggle will not work");

            context.Log.Info("ModSettingsHub loaded. Toggle keybind=" + ToggleId + " default=F9");
        }

        public void Unload()
        {
            try { _keybinds?.Unregister(ToggleId); } catch { /* ignore */ }
            _keybinds = null;
            _toggle = null;
            _ui = null;
            _registry = null;
            _ctx = null;
        }

        public void PostDraw(GameTime gameTime)
        {
            if (_ctx == null || _ui == null)
                return;

            try
            {
                // Always re-resolve registry — Core may rebuild it after enable/disable.
                _ctx.Services.TryGetService(out _registry);

                HandleToggle();
                MaybeAnnounce();

                if (Main.dedServ || !_windowOpen)
                    return;

                if (_ui.Begin(_ctx.L.Get("Window.Title", "Mod Settings"), ref _windowOpen))
                    DrawHub();
                _ui.End();

                DrawSettingsWindow();
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("ModSettingsHub PostDraw error", ex);
            }
        }

        private void DrawHub()
        {
            var mods = _registry != null ? _registry.Mods : null;
            if (mods == null || mods.Count == 0)
            {
                _ui.TextColored(_ctx.L.Get("UI.NoMods", "No mods registered yet."), new Color(200, 180, 120));
                return;
            }

            _ui.TextColored(_ctx.L.Format("UI.LoadedMods", mods.Count), new Color(160, 200, 255));
            _ui.Text(_ctx.L.Get("UI.SelectHint", "Select a mod. Toggle Enabled to load/unload. Server mods are marked."));
            _ui.Separator();

            IModInfo selected = null;
            if (_ui.BeginChild("modlist", 240f))
            {
                for (var i = 0; i < mods.Count; i++)
                {
                    var m = mods[i];
                    var isSel = m.Id == _selectedId;
                    if (isSel)
                        selected = m;

                    if (_ui.Selectable(FormatListLabel(m), isSel))
                    {
                        if (_selectedId != m.Id)
                        {
                            _selectedId = m.Id;
                            _statusLine = null;
                            if (m.HasSettings && m.IsLoaded)
                                _settingsOpen = true;
                        }
                        else if (m.HasSettings && m.IsLoaded)
                        {
                            _settingsOpen = true;
                        }

                        selected = m;
                    }
                }
            }
            _ui.EndChild();

            if (selected == null)
            {
                foreach (var m in mods)
                {
                    if (m.HasSettings && m.IsLoaded)
                    {
                        selected = m;
                        _selectedId = m.Id;
                        break;
                    }
                }

                if (selected == null && mods.Count > 0)
                {
                    selected = mods[0];
                    _selectedId = selected.Id;
                }
            }

            if (selected == null)
                return;

            _ui.Spacing(8f);
            _ui.Separator();

            DrawSelectedDetails(selected);

            if (!string.IsNullOrEmpty(_statusLine))
            {
                _ui.Spacing(4f);
                _ui.TextColored(_statusLine, new Color(200, 220, 160));
            }
        }

        private void DrawSelectedDetails(IModInfo selected)
        {
            _ui.TextColored(selected.Name + "  v" + selected.Version, new Color(255, 230, 160));
            _ui.TextColored(FormatSideLine(selected), SideColor(selected.Side));

            if (selected.Side == TimfSide.Server || selected.Side == TimfSide.Both)
            {
                _ui.TextColored(
                    _ctx.L.Get("UI.ServerWarn",
                        "Server-side: inactive on pure vanilla joins; hosting may kick vanilla clients if RequiredOnJoin."),
                    new Color(255, 160, 80));
            }

            var enabled = selected.IsEnabled;
            var canToggle = !IsProtected(selected.Id);
            if (canToggle)
            {
                if (_ui.Checkbox(_ctx.L.Get("UI.Enabled", "Enabled"), ref enabled))
                {
                    string msg = null;
                    if (_registry != null && _registry.TrySetEnabled(selected.Id, enabled, out msg))
                    {
                        _statusLine = msg;
                        // Registry was rebuilt — refresh selection snapshot next frame.
                        _ctx.Services.TryGetService(out _registry);
                        selected = FindSelected() ?? selected;
                    }
                    else
                    {
                        _statusLine = msg ?? "Failed to change enable state.";
                    }
                }
            }
            else
            {
                _ui.TextColored(_ctx.L.Get("UI.Protected", "Framework library — always enabled."), new Color(150, 150, 150));
            }

            _ui.TextColored(
                selected.IsLoaded
                    ? _ctx.L.Get("UI.StateLoaded", "State: loaded")
                    : _ctx.L.Get("UI.StateNotLoaded", "State: not loaded"),
                selected.IsLoaded ? new Color(140, 220, 140) : new Color(160, 160, 160));

            if (selected.Side == TimfSide.Server || selected.Side == TimfSide.Both)
            {
                _ui.TextColored(
                    selected.ServerLogicActive
                        ? _ctx.L.Get("UI.ServerActive", "Server logic: ACTIVE this session")
                        : _ctx.L.Get("UI.ServerInactive", "Server logic: inactive this session"),
                    selected.ServerLogicActive ? new Color(255, 140, 80) : new Color(150, 150, 150));
            }

            _ui.Spacing(4f);
            if (selected.HasSettings && selected.IsLoaded)
            {
                _settingsTitle = selected.Name;
                _ui.TextColored(_ctx.L.Format("UI.SettingsFor", selected.Name), new Color(255, 220, 150));
                if (!_settingsOpen && _ui.Button(_ctx.L.Get("UI.OpenSettings", "Open settings")))
                    _settingsOpen = true;
            }
            else if (!selected.IsLoaded)
            {
                _ui.TextColored(_ctx.L.Get("UI.EnableForSettings", "Enable and load the mod to open settings."), new Color(150, 150, 150));
            }
            else
            {
                _ui.TextColored(_ctx.L.Get("UI.NoSettings", "This mod has no settings UI."), new Color(150, 150, 150));
            }
        }

        private static bool IsProtected(string id)
        {
            return string.Equals(id, "TIMF.UI", StringComparison.OrdinalIgnoreCase);
        }

        private string FormatListLabel(IModInfo m)
        {
            var side = SideTag(m.Side);
            var en = m.IsEnabled ? "" : _ctx.L.Get("UI.TagOff", " [OFF]");
            var cfg = m.HasSettings && m.IsLoaded ? " [cfg]" : "";
            var srv = m.ServerLogicActive ? _ctx.L.Get("UI.TagSrvOn", " [SRV]") : "";
            return side + " " + m.Name + "  v" + m.Version + en + srv + cfg;
        }

        private static string SideTag(TimfSide side)
        {
            switch (side)
            {
                case TimfSide.Server: return "[S]";
                case TimfSide.Both: return "[B]";
                default: return "[C]";
            }
        }

        private string FormatSideLine(IModInfo m)
        {
            switch (m.Side)
            {
                case TimfSide.Server:
                    return _ctx.L.Get("UI.SideServer", "Side: Server (authoritative / host path)");
                case TimfSide.Both:
                    return _ctx.L.Get("UI.SideBoth", "Side: Both (client + server path)");
                default:
                    return _ctx.L.Get("UI.SideClient", "Side: Client");
            }
        }

        private static Color SideColor(TimfSide side)
        {
            switch (side)
            {
                case TimfSide.Server: return new Color(255, 140, 100);
                case TimfSide.Both: return new Color(220, 180, 255);
                default: return new Color(160, 200, 255);
            }
        }

        private void DrawSettingsWindow()
        {
            if (!_settingsOpen)
                return;

            var selected = FindSelected();
            if (selected == null || !selected.HasSettings || !selected.IsLoaded)
            {
                _settingsOpen = false;
                return;
            }

            _settingsTitle = selected.Name;
            if (_ui.Begin(_settingsTitle, ref _settingsOpen))
            {
                try
                {
                    selected.Settings.BuildSettingsUI(_ui);
                }
                catch (Exception ex)
                {
                    _ui.TextColored("Settings page error (see log)", new Color(255, 120, 120));
                    _ctx.Log.Error("BuildSettingsUI threw for " + selected.Id, ex);
                }
            }
            _ui.End();
        }

        private IModInfo FindSelected()
        {
            var mods = _registry != null ? _registry.Mods : null;
            if (mods == null || string.IsNullOrEmpty(_selectedId))
                return null;

            for (var i = 0; i < mods.Count; i++)
            {
                if (mods[i].Id == _selectedId)
                    return mods[i];
            }

            return null;
        }

        private void HandleToggle()
        {
            if (_toggle == null || !_toggle.JustPressed)
                return;
            if (!IsGameFocused())
                return;
            if (_ui != null && _ui.WantCaptureKeyboard)
                return;

            _windowOpen = !_windowOpen;
            var bind = !string.IsNullOrEmpty(_toggle.CurrentBindingDisplay) ? _toggle.CurrentBindingDisplay : "Toggle";
            try
            {
                Main.NewText(
                    _windowOpen ? _ctx.L.Format("Chat.Open", bind) : _ctx.L.Format("Chat.Closed", bind),
                    180, 200, 255);
            }
            catch { /* ignore */ }
        }

        private bool IsGameFocused()
        {
            try
            {
                if (_ui != null)
                    return _ui.IsGameFocused;
            }
            catch { /* ignore */ }

            try
            {
                return Main.instance == null || Main.instance.IsActive;
            }
            catch
            {
                return true;
            }
        }

        private void MaybeAnnounce()
        {
            if (!_announcePending || Main.gameMenu || Main.dedServ)
                return;
            _announcePending = false;
            try
            {
                Main.NewText(_ctx.L.Format("Chat.Ready", _toggle != null && !string.IsNullOrEmpty(_toggle.CurrentBindingDisplay) ? _toggle.CurrentBindingDisplay : "Toggle"), 180, 200, 255);
            }
            catch { /* ignore */ }
        }
    }
}
