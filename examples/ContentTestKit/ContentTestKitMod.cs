using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using TIMF.Abstractions;
using TIMF.Content;

namespace ContentTestKit
{
    /// <summary>
    /// Manual test harness for the TIMF content subsystem.
    ///
    /// Most of what the content layer does — reflectively widening ItemID.Count, growing 300+
    /// vanilla arrays, building Asset&lt;Texture2D&gt; through non-public members — cannot be
    /// verified outside a running game. This mod surfaces that state in-game so a human can
    /// check it, and hands out the test items so their behaviour can be exercised directly.
    ///
    /// Net=Required because custom content ids are meaningless to a peer without this mod.
    /// </summary>
    [TimfMod(Id = "ContentTestKit", Net = TimfNetProfile.Required)]
    public sealed class ContentTestKitMod : IContentMod, IModSettings
    {
        private IModContext _ctx;
        private IContentLookup _content;
        private string _status = "";
        private readonly List<string> _probeResults = new List<string>();

        public string Name => "Content Test Kit";
        public string Version => "1.0.0";

        public void AddContent(IContentRegistry registry)
        {
            registry.AddItem<TestSword>();
            registry.AddItem<TestMaterial>();
            registry.AddItem<TestPotion>();
            registry.AddItem<TestAccessory>();
            registry.AddItem<TestPlaceable>();
            registry.AddItem<TestTorchItem>();
            registry.AddItem<TestWallItem>();
            registry.AddItem<TestWorkbenchItem>();
            registry.AddItem<TestChestItem>();
            registry.AddTile<TestTorchTile>();
            registry.AddTile<TestSpecialTorchTile>();
            registry.AddTile<TestWorkbenchTile>();
            registry.AddTile<TestChestTile>();
            registry.AddWall<TestWall>();
        }

        public void Load(IModContext context)
        {
            _ctx = context;
            context.Log.Info("ContentTestKit loaded — open Mod Settings (F9) for the content report");
        }

        public void Unload() { _ctx = null; }

        public void PostDraw(GameTime gameTime) { }

        public void BuildSettingsUI(IImmediateModeUi ui)
        {
            // IContentLookup is registered after all mods load, so resolve it lazily.
            if (_content == null)
                _ctx.Services.TryGetService(out _content);

            if (_content == null)
            {
                ui.TextColored("IContentLookup unavailable — content subsystem did not start.",
                    new Color(255, 120, 120));
                return;
            }

            ui.TextColored("=== 内容子系统状态 ===", new Color(255, 220, 150));
            foreach (var line in _content.Report())
            {
                var bad = line.Contains("NOT expanded") || line.Contains(": 0 loaded");
                ui.TextColored(line, bad ? new Color(255, 120, 120) : new Color(190, 220, 190));
            }

            ui.Separator();
            ui.TextColored("=== 已注册物品 ===", new Color(255, 220, 150));
            foreach (var item in _content.RegisteredItems)
            {
                ui.Text(item.ContentKey + "   id=" + item.Type);
                ui.SameLine();
                if (ui.Button("给我##" + item.Type))
                    _status = GiveItem(item.Type, item.DisplayName);
            }

            ui.Separator();
            ui.TextColored("=== 已注册图块 ===", new Color(255, 220, 150));
            foreach (var tile in _content.RegisteredTiles)
                ui.Text(tile.ContentKey + "   id=" + tile.Type);

            ui.Separator();
            ui.TextColored("=== 已注册墙壁 ===", new Color(255, 220, 150));
            foreach (var wall in _content.RegisteredWalls)
                ui.Text(wall.ContentKey + "   id=" + wall.Type);

            ui.Separator();
            if (ui.Button("全部给我一份"))
            {
                var n = 0;
                foreach (var item in _content.RegisteredItems)
                    if (GiveItem(item.Type, item.DisplayName).StartsWith("已放入"))
                        n++;
                _status = "已放入 " + n + " 件物品到背包";
            }

            ui.SameLine();
            if (ui.Button("探测扩容数组是否可寻址"))
                RunArrayProbe();

            if (_probeResults.Count > 0)
            {
                ui.Separator();
                ui.TextColored("=== 数组扩容探测 ===", new Color(255, 220, 150));
                foreach (var r in _probeResults)
                    ui.TextColored(r, r.StartsWith("OK") ? new Color(150, 230, 150) : new Color(255, 120, 120));
            }

            if (!string.IsNullOrEmpty(_status))
            {
                ui.Separator();
                ui.TextColored(_status, new Color(200, 220, 160));
            }
        }

        /// <summary>
        /// Builds the item straight into the inventory rather than dropping it, so the path
        /// under test is Item.SetDefaults on a modded id — exactly the patch we need to verify.
        /// </summary>
        private static string GiveItem(int type, string label)
        {
            try
            {
                var p = Main.LocalPlayer;
                if (p == null)
                    return "没有本地玩家 —— 请先进入世界";

                for (var i = 0; i < 50; i++)
                {
                    var slot = p.inventory[i];
                    if (slot != null && slot.type != 0 && slot.stack > 0)
                        continue;

                    var item = new Item();
                    item.SetDefaults(type);
                    item.stack = 1;
                    p.inventory[i] = item;

                    return item.type == type
                        ? "已放入 " + label + " (id=" + type + ", 槽位 " + i + ")"
                        : "异常：SetDefaults(" + type + ") 后 item.type 变成了 " + item.type;
                }

                return "背包已满";
            }
            catch (Exception ex)
            {
                return "失败：" + ex.GetType().Name + " " + ex.Message;
            }
        }

        /// <summary>
        /// Reads and writes a few id-indexed vanilla arrays at a modded index. If expansion
        /// missed an array this throws IndexOutOfRange here — in a controlled place — instead
        /// of somewhere deep in a draw or AI call later.
        /// </summary>
        private void RunArrayProbe()
        {
            _probeResults.Clear();
            if (_content.RegisteredItems.Count == 0)
            {
                _probeResults.Add("FAIL 没有已注册物品，无从探测");
                return;
            }

            var id = _content.RegisteredItems[0].Type;
            Probe("ItemID.Sets.ItemNoGravity", () =>
            {
                var v = Terraria.ID.ItemID.Sets.ItemNoGravity[id];
                Terraria.ID.ItemID.Sets.ItemNoGravity[id] = v;
            });
            Probe("ItemID.Sets.IsAMaterial", () =>
            {
                var v = Terraria.ID.ItemID.Sets.IsAMaterial[id];
                Terraria.ID.ItemID.Sets.IsAMaterial[id] = v;
            });
            // Reached reflectively: Terraria loads ReLogic from an embedded resource, so a
            // compile-time reference to any ReLogic.dll on disk is a different assembly
            // identity and every access to this field throws MissingFieldException.
            Probe("TextureAssets.Item", () =>
            {
                var field = typeof(Main).Assembly
                    .GetType("Terraria.GameContent.TextureAssets")
                    ?.GetField("Item", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (field == null) throw new InvalidOperationException("找不到 TextureAssets.Item 字段");

                var arr = field.GetValue(null) as Array;
                if (arr == null) throw new InvalidOperationException("TextureAssets.Item 为 null");
                if (id >= arr.Length)
                    throw new InvalidOperationException("数组长度 " + arr.Length + " 容不下 id " + id);

                var asset = arr.GetValue(id);
                if (asset == null) throw new InvalidOperationException("贴图槽位为 null（会导致物品栏绘制中断）");

                var loaded = asset.GetType().GetProperty("IsLoaded")?.GetValue(asset);
                if (loaded is bool b && !b) throw new InvalidOperationException("Asset 未标记为已加载");
            });
            Probe("Lang.GetItemNameValue", () =>
            {
                var s = Lang.GetItemNameValue(id);
                if (string.IsNullOrEmpty(s)) throw new InvalidOperationException("名称为空");
            });
            Probe("ArmorSetBonuses.SetsContaining", () =>
            {
                var type = typeof(Main).Assembly.GetType("Terraria.DataStructures.ArmorSetBonuses");
                var field = type?.GetField("SetsContaining",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static);
                var outer = field?.GetValue(null) as Array;
                if (outer == null) throw new InvalidOperationException("SetsContaining 为 null");
                var row = outer.GetValue(id) as Array;
                if (row == null) throw new InvalidOperationException("模组物品对应行是 null");
                if (row.Length != 0)
                    throw new InvalidOperationException("新物品被错误分配了 " + row.Length + " 个护甲套装条目");
            });

            if (_content.RegisteredTiles.Count > 0)
            {
                var tileId = _content.RegisteredTiles[0].Type;
                Probe("TileID / Main tile arrays", () =>
                {
                    var solid = Main.tileSolid[tileId];
                    Main.tileSolid[tileId] = solid;
                    var lighted = Main.tileLighted[tileId];
                    Main.tileLighted[tileId] = lighted;
                    var merge = Main.tileMerge[tileId][tileId];
                    Main.tileMerge[tileId][tileId] = merge;
                });
                Probe("Player.adjTile", () =>
                {
                    var player = Main.LocalPlayer;
                    if (player == null) throw new InvalidOperationException("没有本地玩家");
                    var adjacent = player.adjTile[tileId];
                    player.adjTile[tileId] = adjacent;
                });
                Probe("TextureAssets.Tile", () =>
                {
                    var field = typeof(Main).Assembly
                        .GetType("Terraria.GameContent.TextureAssets")
                        ?.GetField("Tile", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    var arr = field?.GetValue(null) as Array;
                    if (arr == null) throw new InvalidOperationException("TextureAssets.Tile 为 null");
                    if (tileId >= arr.Length) throw new InvalidOperationException("图块贴图数组未扩容");
                    if (arr.GetValue(tileId) == null) throw new InvalidOperationException("图块贴图槽位为 null");
                });
            }

            if (_content.RegisteredWalls.Count > 0)
            {
                var wallId = _content.RegisteredWalls[0].Type;
                Probe("WallID / Main wall arrays", () =>
                {
                    var house = Main.wallHouse[wallId];
                    Main.wallHouse[wallId] = house;
                    var dungeon = Main.wallDungeon[wallId];
                    Main.wallDungeon[wallId] = dungeon;
                });
                Probe("TextureAssets.Wall", () =>
                {
                    var field = typeof(Main).Assembly
                        .GetType("Terraria.GameContent.TextureAssets")
                        ?.GetField("Wall", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    var arr = field?.GetValue(null) as Array;
                    if (arr == null || wallId >= arr.Length) throw new InvalidOperationException("墙壁贴图数组未扩容");
                    if (arr.GetValue(wallId) == null) throw new InvalidOperationException("墙壁贴图槽位为 null");
                });
            }
        }

        private void Probe(string label, Action action)
        {
            try
            {
                action();
                _probeResults.Add("OK   " + label);
            }
            catch (Exception ex)
            {
                _probeResults.Add("FAIL " + label + " — " + ex.GetType().Name + ": " + ex.Message);
                _ctx?.Log.Error("Content probe failed: " + label, ex);
            }
        }
    }
}
