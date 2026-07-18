using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;

namespace ModSettingsHub
{
    /// <summary>
    /// Central settings window: lists all loaded TIMF mods and opens a separate floating
    /// settings page for the selected mod (mods that implement <see cref="IModSettings"/>).
    /// </summary>
    [TimfMod(Id = "ModSettingsHub")]
    [TimfDependsOn("TIMF.UI", MinVersion = "1.0.0")]
    public sealed class ModSettingsHubMod : IMod
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

        public string Name => "Mod Settings";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            // TIMF.UI is a hard dependency, so this resolves here.
            if (!context.Services.TryGetService(out _ui) || _ui == null)
                context.Log.Error("IImmediateModeUi not available — TIMF.UI missing?");

            if (context.Services.TryGetService(out _keybinds) && _keybinds != null)
                _toggle = _keybinds.Register(ToggleId, context.L.Get("Keybind.Toggle", "Mod Settings Toggle"), Keys.F9);
            else
                context.Log.Error("IKeybindService unavailable — ModSettingsHub toggle will not work");

            // IModRegistry is registered AFTER all mods load; resolve lazily in PostDraw.
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
                if (_registry == null)
                    _ctx.Services.TryGetService(out _registry);

                HandleToggle();
                MaybeAnnounce();

                if (Main.dedServ || !_windowOpen)
                    return;

                if (_ui.Begin(_ctx.L.Get("Window.Title", "Mod Settings"), ref _windowOpen))
                    DrawHub();
                _ui.End();

                // Settings page is a separate floating window, independent of the list.
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
            _ui.Text(_ctx.L.Get("UI.SelectHint", "Select a mod to open its settings window."));
            _ui.Separator();

            IModInfo selected = null;
            if (_ui.BeginChild("modlist", 220f))
            {
                for (var i = 0; i < mods.Count; i++)
                {
                    var m = mods[i];
                    var isSel = m.Id == _selectedId;
                    if (isSel)
                        selected = m;

                    var label = m.Name + "  v" + m.Version + (m.HasSettings ? "  [cfg]" : "");
                    if (_ui.Selectable(label, isSel))
                    {
                        if (_selectedId != m.Id)
                        {
                            _selectedId = m.Id;
                            if (m.HasSettings)
                                _settingsOpen = true;
                        }
                        else if (m.HasSettings)
                        {
                            // Re-selecting the same mod re-opens its settings window.
                            _settingsOpen = true;
                        }

                        selected = m;
                    }
                }
            }
            _ui.EndChild();

            // Default selection: first mod that has a settings page, else first mod.
            if (selected == null)
            {
                foreach (var m in mods)
                {
                    if (m.HasSettings)
                    {
                        selected = m;
                        _selectedId = m.Id;
                        _settingsOpen = true;
                        break;
                    }
                }

                if (selected == null)
                {
                    selected = mods[0];
                    _selectedId = selected.Id;
                }
            }

            _ui.Spacing(8f);
            _ui.Separator();

            if (selected.HasSettings)
            {
                _settingsTitle = selected.Name;
                _ui.TextColored(_ctx.L.Format("UI.SettingsFor", selected.Name), new Color(255, 220, 150));
                if (!_settingsOpen && _ui.Button(_ctx.L.Get("UI.OpenSettings", "Open settings")))
                    _settingsOpen = true;
            }
            else
            {
                _ui.TextColored(_ctx.L.Get("UI.NoSettings", "This mod has no settings UI."), new Color(150, 150, 150));
            }
        }

        private void DrawSettingsWindow()
        {
            if (!_settingsOpen)
                return;

            var selected = FindSelected();
            if (selected == null || !selected.HasSettings)
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
            // Don't toggle while typing in TIMF text fields.
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
