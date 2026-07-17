using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;

namespace ModSettingsHub
{
    /// <summary>
    /// Central settings window: lists all loaded TIMF mods and shows the settings page
    /// for the selected mod (mods that implement <see cref="IModSettings"/>).
    /// Toggle with F9.
    /// </summary>
    [TimfMod(Id = "ModSettingsHub")]
    [TimfDependsOn("TIMF.UI", MinVersion = "1.0.0")]
    public sealed class ModSettingsHubMod : IMod
    {
        private IModContext _ctx;
        private IImmediateModeUi _ui;
        private IModRegistry _registry;
        private bool _windowOpen = true;
        private string _selectedId;
        private Keys _toggleKey = Keys.F9;
        private KeyboardState _prevKb;
        private bool _announcePending = true;

        public string Name => "Mod Settings";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            // TIMF.UI is a hard dependency, so this resolves here.
            if (!context.Services.TryGetService(out _ui) || _ui == null)
                context.Log.Error("IImmediateModeUi not available — TIMF.UI missing?");

            // IModRegistry is registered AFTER all mods load; resolve lazily in PostDraw.
            _prevKb = Keyboard.GetState();
            context.Log.Info("ModSettingsHub loaded. Toggle window: F9");
        }

        public void Unload()
        {
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

                if (_ui.Begin("Mod Settings", ref _windowOpen))
                    DrawHub();
                _ui.End();
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
                _ui.TextColored("No mods registered yet.", new Color(200, 180, 120));
                return;
            }

            _ui.TextColored("Loaded mods (" + mods.Count + ")", new Color(160, 200, 255));
            _ui.Separator();

            // --- Mod list ---
            IModInfo selected = null;
            for (var i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                var isSel = m.Id == _selectedId;
                if (isSel)
                    selected = m;

                var label = m.Name + "  v" + m.Version + (m.HasSettings ? "  ⚙" : "");
                if (_ui.Selectable(label, isSel))
                {
                    _selectedId = m.Id;
                    selected = m;
                }
            }

            // Default selection: first mod that has a settings page, else first mod.
            if (selected == null)
            {
                foreach (var m in mods)
                {
                    if (m.HasSettings)
                    {
                        selected = m;
                        _selectedId = m.Id;
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

            // --- Settings page for selected mod ---
            _ui.TextColored(selected.Name + " — settings", new Color(255, 220, 150));
            _ui.Spacing(4f);

            if (selected.HasSettings)
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
            else
            {
                _ui.TextColored("This mod has no settings page.", new Color(150, 150, 150));
                _ui.Text("Implement IModSettings on your IMod class to add one.");
            }
        }

        private void HandleToggle()
        {
            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(_toggleKey) && _prevKb.IsKeyUp(_toggleKey))
            {
                _windowOpen = !_windowOpen;
                try
                {
                    Main.NewText(
                        _windowOpen ? "Mod Settings: open (F9)" : "Mod Settings: closed (F9)",
                        180, 200, 255);
                }
                catch { /* ignore */ }
            }

            _prevKb = kb;
        }

        private void MaybeAnnounce()
        {
            if (!_announcePending || Main.gameMenu || Main.dedServ)
                return;
            _announcePending = false;
            try
            {
                Main.NewText("Press F9 for the Mod Settings window", 180, 200, 255);
            }
            catch { /* ignore */ }
        }
    }
}
