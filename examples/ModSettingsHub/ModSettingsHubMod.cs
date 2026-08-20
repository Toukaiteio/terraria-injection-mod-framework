using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;
using TIMF.Abstractions.Security;

namespace ModSettingsHub
{
    /// <summary>
    /// Compact mod table-of-contents: list + enable + open settings page.
    /// </summary>
    [TimfMod(Id = "ModSettingsHub", Side = TimfSide.Client, LoadBeforeWorld = true)]
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
        private ISecurityCenter _securityCenter;

        public string Name => "Mod Settings";
        public string Version => "1.4.0";

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

            context.Log.Info("ModSettingsHub loaded. Toggle=" + ToggleId + " default=F9");
        }

        public void Unload()
        {
            try { _keybinds?.Unregister(ToggleId); } catch { /* ignore */ }
            _keybinds = null;
            _toggle = null;
            _ui = null;
            _registry = null;
            _securityCenter = null;
            _ctx = null;
        }

        public void PostDraw(GameTime gameTime)
        {
            if (_ctx == null || _ui == null)
                return;

            try
            {
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
            _ctx.Services.TryGetService(out _securityCenter);
            DrawSecurityBanner();

            var mods = _registry != null ? _registry.Mods : null;
            if (mods == null || mods.Count == 0)
            {
                _ui.TextColored(_ctx.L.Get("UI.NoMods", "No mods."), new Color(200, 180, 120));
                return;
            }

            IModInfo selected = null;
            if (_ui.BeginChild("modlist", 280f))
            {
                // Two-stage lifecycle: startup (pre-world) mods first, world-staged mods below.
                selected = DrawModGroup(mods, true, selected);
                selected = DrawModGroup(mods, false, selected);
            }
            _ui.EndChild();

            if (selected == null)
            {
                foreach (var m in mods)
                {
                    if (CanOpenSettings(m))
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

            _ui.Spacing(6f);
            _ui.Separator();
            DrawSelectedDetails(selected);

            if (!string.IsNullOrEmpty(_statusLine))
            {
                _ui.Spacing(2f);
                _ui.TextColored(_statusLine, new Color(200, 220, 160));
            }
        }

        private IModInfo DrawModGroup(IReadOnlyList<IModInfo> mods, bool preWorld, IModInfo selected)
        {
            var headerDrawn = false;
            for (var i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                if (m.LoadsBeforeWorld != preWorld)
                    continue;

                if (!headerDrawn)
                {
                    headerDrawn = true;
                    _ui.TextColored(preWorld
                            ? _ctx.L.Get("UI.GroupPreWorld", "Startup mods (loaded before worlds)")
                            : _ctx.L.Get("UI.GroupWorld", "World mods (loaded in-world)"),
                        new Color(140, 175, 215));
                }

                var isSel = m.Id == _selectedId;
                if (isSel)
                    selected = m;

                if (_ui.Selectable(FormatListLabel(m), isSel))
                {
                    if (_selectedId != m.Id)
                    {
                        _selectedId = m.Id;
                        _statusLine = null;
                        if (CanOpenSettings(m))
                            _settingsOpen = true;
                    }
                    else if (CanOpenSettings(m))
                    {
                        _settingsOpen = true;
                    }

                    selected = m;
                }
            }

            return selected;
        }

        private void DrawSecurityBanner()
        {
            if (_securityCenter == null)
                return;

            var pending = _securityCenter.PendingRequestCount;
            var blocked = _securityCenter.BlockedModCount;
            var text = pending > 0
                ? _ctx.L.Format("Security.Pending", pending)
                : blocked > 0
                    ? _ctx.L.Format("Security.Blocked", blocked)
                : _ctx.L.Get("Security.Warning",
                    "Security note: mod DLLs are trusted code; direct system calls are not sandboxed.");
            _ui.TextColored(text, pending > 0 || blocked > 0
                ? new Color(255, 130, 100)
                : new Color(230, 180, 100));
            if (_ui.Button(_ctx.L.Get("Security.Open", "Security & Permissions")))
                _securityCenter.Show();
            _ui.Spacing(6f);
            _ui.Separator();
        }

        private void DrawSelectedDetails(IModInfo selected)
        {
            _ui.Text(selected.Name + "  v" + selected.Version);

            var enabled = selected.IsEnabled;
            var session = selected as IModSessionState;
            var canToggle = session != null
                ? session.CanChangeEnabled
                : !IsProtected(selected.Id);
            if (canToggle)
            {
                if (_ui.Checkbox(_ctx.L.Get("UI.Enabled", "Enabled"), ref enabled))
                {
                    string msg = null;
                    if (_registry != null && _registry.TrySetEnabled(selected.Id, enabled, out msg))
                    {
                        _statusLine = msg;
                        _ctx.Services.TryGetService(out _registry);
                        selected = FindSelected() ?? selected;
                    }
                    else
                    {
                        _statusLine = msg ?? "Failed.";
                    }
                }
            }
            else
            {
                // In-world the mod enable switch is menu-only; surface the mod's own feature
                // switch instead (config-backed flip — no load/unload, no stutter).
                var featureToggle = selected.FeatureToggle;
                if (featureToggle != null)
                {
                    var featureOn = featureToggle.FeatureEnabled;
                    if (_ui.Checkbox(_ctx.L.Get("UI.FeatureEnabled", "Feature enabled"), ref featureOn))
                    {
                        try
                        {
                            featureToggle.FeatureEnabled = featureOn;
                            _statusLine = null;
                        }
                        catch (Exception ex)
                        {
                            _ctx.Log.Error("Feature toggle failed for " + selected.Id, ex);
                            _statusLine = "Feature toggle failed (see log).";
                        }
                    }
                }
                else if (selected.IsLoaded && HasSettingsCapability(selected))
                {
                    _ui.TextColored(_ctx.L.Get("UI.NoFeatureToggle",
                            "No single feature switch — use the mod's settings window."),
                        new Color(180, 170, 130));
                }

                var state = enabled
                    ? _ctx.L.Get("UI.EnabledLockedOn", "Enabled: ON (locked)")
                    : _ctx.L.Get("UI.EnabledLockedOff", "Enabled: OFF (locked)");
                _ui.TextColored(state, new Color(150, 150, 150));
                var lockReason = session?.InteractionLockReason;
                if (session != null && !session.IsSessionAllowed)
                    lockReason = _ctx.L.Get("UI.ServerDisabled",
                        "Not enabled by the current server; controls are unavailable.");
                else if (!IsProtected(selected.Id))
                    lockReason = _ctx.L.Get("UI.ModToggleMenuOnly",
                        "Mod enablement is menu-only; in-world use the feature switch above.");
                if (!string.IsNullOrEmpty(lockReason))
                    _ui.TextColored(lockReason,
                        session == null || session.IsSessionAllowed
                            ? new Color(180, 170, 130)
                            : new Color(255, 150, 100));
            }

            if (CanOpenSettings(selected))
            {
                if (!_settingsOpen && _ui.Button(_ctx.L.Get("UI.OpenSettings", "Settings")))
                    _settingsOpen = true;
            }
            else if (HasSettingsCapability(selected))
            {
                var reason = session?.InteractionLockReason;
                if (session != null && !session.IsSessionAllowed)
                    reason = _ctx.L.Get("UI.ServerDisabled",
                        "Not enabled by the current server; controls are unavailable.");
                if (string.IsNullOrEmpty(reason))
                    reason = _ctx.L.Get("UI.EnableForSettings", "Enable to open settings.");
                _ui.TextColored(reason, new Color(150, 150, 150));
            }
        }

        private string FormatListLabel(IModInfo m)
        {
            // Side tags: whether join clients need TIMF handshake.
            // [C]/[P] = no handshake (vanilla clients OK) · [S]/[B] = clients need TIMF
            var side = SideTag(m);
            var en = m.IsEnabled ? "" : _ctx.L.Get("UI.TagOff", " [OFF]");
            var notLoaded = m.IsEnabled && !m.IsLoaded && !m.LoadsBeforeWorld
                ? _ctx.L.Get("UI.TagNotLoaded", " [NOT LOADED]")
                : "";
            var session = m as IModSessionState;
            var unavailable = session == null || session.IsSessionAllowed
                ? "" : _ctx.L.Get("UI.TagServerOff", " [SERVER OFF]");
            var srv = m.ServerLogicActive ? _ctx.L.Get("UI.TagActive", " *") : "";
            return side + " " + m.Name + en + notLoaded + unavailable + srv;
        }

        /// <summary>
        /// Derives the tag from the two orthogonal axes rather than switching on the side value.
        /// Capability decides C vs S/B; the handshake question is purely <see cref="TimfNetProfile"/>.
        /// </summary>
        private string SideTag(IModInfo m)
        {
            if (!TimfSides.IsAuthorityCapable(m.Side))
                return _ctx.L.Get("UI.TagClient", "[C]");

            // Vanilla-safe authority: host stays joinable by pure vanilla clients.
            if (TimfNetProfiles.IsVanillaHostCompatible(m.NetProfile))
                return _ctx.L.Get("UI.TagPlugin", "[P]");

            return TimfSides.IsClientCapable(m.Side)
                ? _ctx.L.Get("UI.TagBoth", "[B]")
                : _ctx.L.Get("UI.TagServer", "[S]");
        }

        private void DrawSettingsWindow()
        {
            if (!_settingsOpen)
                return;

            var selected = FindSelected();
            if (selected == null || !CanOpenSettings(selected))
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
                    _ui.TextColored("Settings error (see log)", new Color(255, 120, 120));
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

        private static bool CanOpenSettings(IModInfo mod)
        {
            if (mod == null) return false;
            var session = mod as IModSessionState;
            return session != null
                ? session.CanOpenSettings
                : mod.HasSettings && mod.IsLoaded;
        }

        private static bool HasSettingsCapability(IModInfo mod)
        {
            if (mod == null) return false;
            var session = mod as IModSessionState;
            return session != null ? session.HasSettingsCapability : mod.HasSettings;
        }

        private static bool IsProtected(string id)
        {
            return string.Equals(id, "TIMF.UI", StringComparison.OrdinalIgnoreCase);
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
                var key = _toggle != null && !string.IsNullOrEmpty(_toggle.CurrentBindingDisplay)
                    ? _toggle.CurrentBindingDisplay
                    : "F9";
                Main.NewText(_ctx.L.Format("Chat.Ready", key), 180, 200, 255);
            }
            catch { /* ignore */ }
        }
    }
}
