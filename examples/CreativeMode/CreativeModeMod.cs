using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Terraria;
using TIMF.Abstractions;
using TIMF.Abstractions.Security;

namespace CreativeMode
{
    /// <summary>
    /// Debug / creative helper: browse the full vanilla item list, search by name / id / pinyin,
    /// choose a quantity, and give the selected item (or coins) to the local player.
    /// </summary>
    [TimfMod(Id = "CreativeMode", Side = TimfSide.Client)]
    [TimfDependsOn("TIMF.UI", MinVersion = "1.0.0")]
    public sealed class CreativeModeMod : IClientMod, IModSettings
    {
        // Classic coin item types.
        private const int CopperCoin = 71;
        private const int SilverCoin = 72;
        private const int GoldCoin = 73;
        private const int PlatinumCoin = 74;
        private const string ToggleId = "CreativeMode.Toggle";

        private IModContext _ctx;
        private IImmediateModeUi _ui;
        private ItemDatabase _db;
        private IKeybind _toggle;
        private IKeybindService _keybinds;

        private bool _windowOpen = true;
        private bool _announcePending = true;

        private string _search = "";
        private string _lastSearch = null;
        private readonly List<ItemEntry> _results = new List<ItemEntry>();
        private int _selectedType = -1;
        private float _amount = 1f;

        public string Name => "Creative Mode";
        public string Version => "1.0.0";

        public void Load(IModContext context)
        {
            _ctx = context;
            _db = new ItemDatabase(context.Log, context.Services.GetService<ITerrariaReflection>());

            _ui = context.Client != null ? context.Client.Ui : null;
            if (_ui == null)
                context.Log.Error("IClientServices.Ui unavailable — TIMF.UI missing?");

            _keybinds = context.Client != null ? context.Client.Keybinds : null;
            if (_keybinds != null)
                _toggle = _keybinds.Register(ToggleId, context.L.Get("Keybind.Toggle", "Creative Mode Toggle"), Keys.F6);
            else
                context.Log.Error("IKeybindService unavailable — CreativeMode toggle will not work");

            context.Log.Info("CreativeMode loaded. Toggle keybind=" + ToggleId + " default=F6");
        }

        public void Unload()
        {
            try { _keybinds?.Unregister(ToggleId); } catch { /* ignore */ }
            _keybinds = null;
            _toggle = null;
            _ui = null;
            _db = null;
            _ctx = null;
        }

        public void PostDraw(GameTime gameTime)
        {
            if (_ctx == null || _ui == null)
                return;

            try
            {
                HandleToggle();
                MaybeAnnounce();

                if (Main.gameMenu || Main.dedServ || !_windowOpen)
                    return;

                _db.EnsureBuilt();
                RefreshIfNeeded();

                if (_ui.Begin("Creative Mode — Item Browser", ref _windowOpen))
                    DrawBrowser();
                _ui.End();
            }
            catch (Exception ex)
            {
                _ctx.Log.Error("CreativeMode PostDraw error", ex);
            }
        }

        private void DrawBrowser()
        {
            if (!_db.IsBuilt || _db.All.Count == 0)
            {
                _ui.TextColored(_ctx.L.Get("UI.DbNotReady", "Item database not ready (enter a world first)."), new Color(200, 180, 120));
                return;
            }

            // --- Search row ---
            _ui.InputText("Search (name / id / pinyin)", ref _search, 48);
            _ui.SameLine();
            if (_ui.Button("Clear"))
                _search = "";

            _ui.TextColored(_ctx.L.Format("UI.Matches", _results.Count), new Color(160, 200, 255));
            _ui.Separator();

            // --- Quick coins ---
            _ui.TextColored(_ctx.L.Get("UI.Coins", "Coins (stack 9999):"), new Color(255, 220, 120));
            if (_ui.Button("Copper"))
                GiveCoin(CopperCoin);
            _ui.SameLine();
            if (_ui.Button("Silver"))
                GiveCoin(SilverCoin);
            _ui.SameLine();
            if (_ui.Button("Gold"))
                GiveCoin(GoldCoin);
            _ui.SameLine();
            if (_ui.Button("Platinum"))
                GiveCoin(PlatinumCoin);

            _ui.Separator();

            // --- Scrollable item list ---
            if (_ui.BeginChild("itemlist", 240f))
            {
                if (_results.Count == 0)
                {
                    _ui.TextColored(_ctx.L.Get("UI.NoMatch", "No items match your search."), new Color(150, 150, 150));
                }
                else
                {
                    for (var i = 0; i < _results.Count; i++)
                    {
                        var e = _results[i];
                        var isSel = e.Type == _selectedType;
                        var label = "#" + e.Type + "  " + e.Name;
                        if (_ui.Selectable(label, isSel))
                            _selectedType = e.Type;
                    }
                }
            }
            _ui.EndChild();

            _ui.Separator();

            // --- Give panel ---
            if (_selectedType > 0)
            {
                var name = FindName(_selectedType);
                _ui.TextColored("Selected: #" + _selectedType + "  " + name, new Color(255, 220, 150));
                _ui.InputFloat("Amount", ref _amount, 1f);
                if (_amount < 1f) _amount = 1f;
                if (_amount > 99999f) _amount = 99999f;

                if (_ui.Button("Give x" + (int)_amount))
                    GiveSelected();
                _ui.SameLine();
                if (_ui.Button("Give 1"))
                {
                    var keep = _amount;
                    _amount = 1;
                    GiveSelected();
                    _amount = keep;
                }
                _ui.SameLine();
                if (_ui.Button("Stack (9999)"))
                {
                    var keep = _amount;
                    _amount = 9999;
                    GiveSelected();
                    _amount = keep;
                }
            }
            else
            {
                _ui.TextColored(_ctx.L.Get("UI.SelectHint", "Select an item from the list above."), new Color(150, 150, 150));
            }
        }

        private void GiveCoin(int coinType)
        {
            var ok = _db.Give(coinType, 9999);
            var name = FindName(coinType);
            try
            {
                if (ok)
                    Main.NewText("Gave 9999x " + name, 255, 220, 100);
                else
                    Main.NewText("Failed to give " + name, 255, 120, 120);
            }
            catch { /* ignore */ }
        }

        private void GiveSelected()
        {
            if (_selectedType <= 0)
                return;

            var amount = (int)_amount;
            var ok = _db.Give(_selectedType, amount);
            var name = FindName(_selectedType);
            try
            {
                if (ok)
                    Main.NewText("Gave " + amount + "x " + name, 120, 220, 160);
                else
                    Main.NewText("Failed to give " + name + " (see log)", 255, 120, 120);
            }
            catch { /* ignore */ }
        }

        private string FindName(int type)
        {
            var all = _db.All;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].Type == type)
                    return all[i].Name;
            }
            // Fallback names for coins if DB skipped them.
            switch (type)
            {
                case CopperCoin: return "Copper Coin";
                case SilverCoin: return "Silver Coin";
                case GoldCoin: return "Gold Coin";
                case PlatinumCoin: return "Platinum Coin";
                default: return "item " + type;
            }
        }

        private void RefreshIfNeeded()
        {
            if (_lastSearch == _search)
                return;
            _lastSearch = _search;
            _db.Search(_search, _results);
        }

        private void HandleToggle()
        {
            if (_toggle == null || !_toggle.JustPressed)
                return;
            // Don't toggle while typing in TIMF text fields.
            var typing = _ui != null && _ui.WantCaptureKeyboard;
            if (!IsGameFocused() || typing)
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

        public void BuildSettingsUI(IImmediateModeUi ui)
        {

            ui.Text(_ctx.L.Format("Settings.Toggle", _toggle != null && !string.IsNullOrEmpty(_toggle.CurrentBindingDisplay) ? _toggle.CurrentBindingDisplay : _ctx.L.Get("Settings.Unbound", "(unbound)")));
            ui.Spacing();
            if (ui.Button(_windowOpen ? "Close browser" : "Open browser"))
                _windowOpen = !_windowOpen;

            if (_db != null && _db.IsBuilt)
                ui.TextColored(_ctx.L.Format("Settings.Indexed", _db.All.Count), new Color(160, 200, 255));
            else
                ui.TextColored(_ctx.L.Get("UI.DbNotReady", "Item database not ready (enter a world first)."), new Color(200, 180, 120));
        }
    }
}
