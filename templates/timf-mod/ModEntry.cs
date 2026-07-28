using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;

namespace MyMod
{
    /// <summary>
    /// MOD_DISPLAY_NAME — a minimal TIMF client mod.
    ///
    /// It implements <see cref="IClientMod"/> (this mod needs client features: a keybind and a
    /// per-frame <see cref="IMod.PostDraw"/> hook) and <see cref="IModSettings"/> (a page in the
    /// Mod Settings hub). The loader infers the side as <see cref="TimfSide.Client"/> from
    /// <see cref="IClientMod"/>; the explicit Side on the attribute just documents that intent.
    /// </summary>
    [TimfMod(Id = "MyMod", Side = TimfSide.Client)]
    public sealed class MyModMod : IClientMod, IModSettings
    {
        private const string ConfigName = "MyMod.json";
        private const string ToggleId = "MyMod.Toggle";

        private IModContext _ctx;
        private ModConfig _config;
        private IKeybindService _keybinds;
        private IKeybind _toggle;
        private bool _announcePending;

        public string Name => "MOD_DISPLAY_NAME";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;

            // Config is read/written only through the framework's confined storage.
            _config = ModConfig.LoadOrCreate(context.Storage, ConfigName);

            // Client is null on a dedicated server — always null-check before using it.
            _keybinds = context.Client != null ? context.Client.Keybinds : null;
            if (_keybinds != null)
            {
                _toggle = _keybinds.Register(
                    ToggleId,
                    context.L.Get("Keybind.Toggle", "MOD_DISPLAY_NAME Toggle"),
                    ParseKey(_config.ToggleKey, Keys.N));
            }
            else
            {
                context.Log.Error("IKeybindService unavailable — the toggle key will not work");
            }

            _announcePending = true;
            context.Log.Info("MOD_DISPLAY_NAME loaded (author: MOD_AUTHOR). Enabled=" + _config.Enabled);
        }

        public void Unload()
        {
            try { _keybinds?.Unregister(ToggleId); } catch { /* ignore */ }
            _keybinds = null;
            _toggle = null;
            _config = null;
            _ctx = null;
        }

        public void PostDraw(GameTime gameTime)
        {
            if (_ctx == null || Main.dedServ)
                return;

            try
            {
                HandleToggle();
                MaybeAnnounce();

                if (_config == null || !_config.Enabled || Main.gameMenu)
                    return;

                // TODO: your per-frame client drawing / logic goes here.
                // Main.spriteBatch is closed at this point; if you draw, wrap it in
                // Main.spriteBatch.Begin(...) / End() yourself.
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("MOD_DISPLAY_NAME PostDraw error", ex);
            }
        }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            // IMPORTANT: build widgets on 'ui' only — never call ui.Begin/End here.
            var L = _ctx.L;

            var dirty = ui.Checkbox(L.Get("Settings.Enabled", "Enabled"), ref _config.Enabled);

            var bind = _toggle != null && !string.IsNullOrEmpty(_toggle.CurrentBindingDisplay)
                ? _toggle.CurrentBindingDisplay
                : L.Get("Settings.Unbound", "(unbound)");
            ui.Text(L.Format("Settings.Toggle", bind));

            if (dirty)
                SaveConfig();
        }

        private void HandleToggle()
        {
            if (_toggle == null || !_toggle.JustPressed)
                return;
            // Ignore the key when the game window is not focused.
            if (Main.instance != null && !Main.instance.IsActive)
                return;

            _config.Enabled = !_config.Enabled;
            SaveConfig();

            var msg = _config.Enabled
                ? _ctx.L.Get("Chat.On", "MOD_DISPLAY_NAME: ON")
                : _ctx.L.Get("Chat.Off", "MOD_DISPLAY_NAME: OFF");
            try { Main.NewText(msg, 255, 255, 0); } catch { /* ignore */ }
        }

        private void MaybeAnnounce()
        {
            if (!_announcePending || Main.gameMenu)
                return;
            _announcePending = false;
            try
            {
                Main.NewText(
                    _ctx.L.Format("Chat.Ready", _toggle != null ? _toggle.CurrentBindingDisplay : "?"),
                    255, 200, 80);
            }
            catch { /* ignore */ }
        }

        private void SaveConfig()
        {
            try { _config.Save(_ctx.Storage, ConfigName); }
            catch (Exception ex) { _ctx.Log.Error("MOD_DISPLAY_NAME save config failed", ex); }
        }

        private static Keys ParseKey(string name, Keys fallback)
        {
            Keys k;
            return !string.IsNullOrWhiteSpace(name) && Enum.TryParse(name.Trim(), true, out k) ? k : fallback;
        }
    }
}
