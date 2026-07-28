using System;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using HarmonyLib;
using Terraria;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    internal static class NpcContentPatches
    {
        private static ContentManager _content;
        private static ILogger _log;
        private static TextureAssetSlots _npcTextureSlots;

        internal static void Bind(ContentManager content, ILogger log)
        {
            _content = content; _log = log; _npcTextureSlots = null;
        }

        internal static void Install(Harmony harmony, ILogger log)
        {
            try
            {
                var setDefaults = AccessTools.Method(typeof(NPC), nameof(NPC.SetDefaults),
                    new[] { typeof(int), typeof(NPCSpawnParams) });
                var ai = AccessTools.Method(typeof(NPC), nameof(NPC.AI), Type.EmptyTypes);
                var frame = AccessTools.Method(typeof(NPC), nameof(NPC.FindFrame), Type.EmptyTypes);
                var chat = AccessTools.Method(typeof(NPC), nameof(NPC.GetChat), Type.EmptyTypes);
                if (setDefaults != null) harmony.Patch(setDefaults, prefix: new HarmonyMethod(typeof(NpcContentPatches), nameof(BeforeSetDefaults)));
                if (ai != null) harmony.Patch(ai, prefix: new HarmonyMethod(typeof(NpcContentPatches), nameof(BeforeAI)));
                if (frame != null) harmony.Patch(frame,
                    prefix: new HarmonyMethod(typeof(NpcContentPatches), nameof(BeforeFindFrame)),
                    postfix: new HarmonyMethod(typeof(NpcContentPatches), nameof(AfterFindFrame)));

                // Vanilla DrawNPCs skips any NPC whose type >= NPCID.Count, and that comparison was
                // compiled into Terraria.dll against the vanilla count — so an expanded custom id
                // (type >= vanilla count) never reaches DrawNPCDirect and is never drawn even though
                // it updates fine. Draw framework NPCs ourselves from a DrawNPCs postfix (always runs,
                // same open SpriteBatch), and suppress vanilla's body draw in DrawNPCDirect so an id
                // that does reach it on some build is not drawn twice.
                var drawDirect = AccessTools.Method(typeof(Main), nameof(Main.DrawNPCDirect),
                    new[] { typeof(SpriteBatch), typeof(NPC), typeof(bool), typeof(Vector2) });
                if (drawDirect != null)
                    harmony.Patch(drawDirect, prefix: new HarmonyMethod(typeof(NpcContentPatches), nameof(BeforeDrawNPCDirect)));
                else
                    log.Error("Content: Main.DrawNPCDirect not found — custom NPC sprites may not render");
                var drawNpcs = AccessTools.Method(typeof(Main), "DrawNPCs", new[] { typeof(bool) });
                if (drawNpcs != null)
                    harmony.Patch(drawNpcs, postfix: new HarmonyMethod(typeof(NpcContentPatches), nameof(AfterDrawNPCs)));
                else
                    log.Error("Content: Main.DrawNPCs not found — custom NPC sprites will not render");
                if (chat != null) harmony.Patch(chat, prefix: new HarmonyMethod(typeof(NpcContentPatches), nameof(BeforeGetChat)));
                var getName = AccessTools.Method(typeof(Lang), "GetNPCNameValue", new[] { typeof(int) });
                if (getName != null) harmony.Patch(getName, prefix: new HarmonyMethod(typeof(NpcContentPatches), nameof(BeforeGetName)));
                // 1.4.5.x draws NPC dialog through NPCChatPanel, not the dead Main.DrawNPCChatButtons.
                // Its buttons are the NPCInteractions.All entries whose Condition() is true, so register
                // our shop/quest interactions and re-add them whenever that list is rebuilt.
                var interInit = AccessTools.Method(typeof(Terraria.GameContent.NPCInteractions), "Initialize");
                if (interInit != null)
                    harmony.Patch(interInit,
                        postfix: new HarmonyMethod(typeof(NpcContentPatches), nameof(AfterInteractionsInitialized)));
                else
                    log.Error("Content: NPCInteractions.Initialize not found — custom NPC shop/quest buttons unavailable");
                RegisterChatInteractions();

                // SceneMetrics scans nearby NPCs by type; its instance arrays keep the vanilla
                // NPCID.Count length, so a custom town NPC crashes biome scanning without this.
                var npcScan = AccessTools.Method(typeof(SceneMetrics), "ScanNPCPositions");
                if (npcScan != null)
                    harmony.Patch(npcScan,
                        prefix: new HarmonyMethod(typeof(NpcContentPatches), nameof(BeforeSceneNpcScan)));
                else
                    log.Error("Content: SceneMetrics.ScanNPCPositions not found — custom town NPCs would crash biome scanning");

                // Custom bosses spawned through NPC.NewNPC (as ContentTestKit and most mods do)
                // never get the vanilla "<name> has awoken!" broadcast — only NPC.SpawnBoss does.
                var newNpc = AccessTools.Method(typeof(NPC), nameof(NPC.NewNPC), new[]
                {
                    typeof(Terraria.DataStructures.IEntitySource), typeof(int), typeof(int), typeof(int),
                    typeof(int), typeof(float), typeof(float), typeof(float), typeof(float), typeof(int)
                });
                if (newNpc != null)
                    harmony.Patch(newNpc, postfix: new HarmonyMethod(typeof(NpcContentPatches), nameof(AfterNewNPC)));

                // Dropping the appended custom-shop slot when chat closes stops the inventory shop
                // state from stranding (which hid the HUD and froze input).
                var closeChat = AccessTools.Method(typeof(Main), nameof(Main.CloseNPCChatOrSign), new[] { typeof(bool) });
                if (closeChat != null)
                    harmony.Patch(closeChat, postfix: new HarmonyMethod(typeof(NpcContentPatches), nameof(AfterCloseNpcChat)));

                log.Info("Content: custom NPC defaults/AI/frame/chat bridges installed");
            }
            catch (Exception ex) { log.Error("Content: custom NPC patch installation failed", ex); }
        }

        private static bool BeforeSetDefaults(NPC __instance, int Type)
        {
            var def = _content?.GetNpc(Type);
            if (def == null) return true;
            if (!_content.IsSessionAllowed(def.ModId))
            {
                __instance.active = false;
                return false;
            }
            try
            {
                ResetNpc(__instance);
                __instance.type = Type; __instance.netID = Type;
                def.Npc = __instance; def.SetDefaults();
                if (def.IsTownNpc) { __instance.townNPC = true; __instance.friendly = true; }
            }
            catch (Exception ex)
            {
                __instance.active = false;
                _log?.Error("Content: NPC SetDefaults failed for " + def.ContentKey, ex);
            }
            finally { def.Npc = null; }
            return false;
        }

        private static void ResetNpc(NPC npc)
        {
            var reset = AccessTools.Method(typeof(NPC), "ResetForNewNPC");
            reset?.Invoke(npc, null);
            npc.width = 18; npc.height = 40; npc.damage = 0; npc.defense = 0;
            npc.lifeMax = 50; npc.life = 50; npc.knockBackResist = 1f; npc.active = true;
        }

        private static bool BeforeAI(NPC __instance)
        {
            var def = _content?.GetNpc(__instance?.type ?? 0);
            if (def == null) return true;
            if (!_content.IsSessionAllowed(def.ModId)) { __instance.active = false; return false; }
            try { def.Npc = __instance; def.AI(); }
            catch (Exception ex) { _log?.Error("Content: NPC AI failed for " + def.ContentKey, ex); }
            finally { def.Npc = null; }
            return def.RunVanillaAI;
        }

        private static bool BeforeFindFrame(NPC __instance)
        {
            var def = _content?.GetNpc(__instance?.type ?? 0);
            if (def == null) return true;
            try
            {
                var frameHeight = Math.Max(1, __instance.height);
                if (!Main.dedServ)
                {
                    if (_npcTextureSlots == null) _npcTextureSlots = TextureAssetSlots.Resolve(_log, "Npc");
                    var texture = _npcTextureSlots?.GetTexture(__instance.type);
                    if (texture != null)
                    {
                        frameHeight = Math.Max(1, texture.Height / Math.Max(1, def.FrameCount));
                        if (__instance.frame.Width <= 0) __instance.frame.Width = texture.Width;
                        __instance.frame.Height = frameHeight;
                    }
                }
                else if (__instance.frame.Height <= 0) __instance.frame.Height = frameHeight;
                def.Npc = __instance; def.FindFrame(frameHeight);
            }
            catch (Exception ex) { _log?.Error("Content: NPC FindFrame failed for " + def.ContentKey, ex); }
            finally { def.Npc = null; }
            return def.RunVanillaFrame;
        }

        /// <summary>
        /// Clamp the frame vanilla FindFrame picked back inside the actually-loaded texture. A reused
        /// or small sprite (e.g. an item texture used as a placeholder) has far fewer frames than the
        /// NPC's aiStyle expects, so vanilla pushes frame.Y past the texture and nothing draws. Pinning
        /// the rect inside the texture keeps every custom NPC visible (as a static sprite for a
        /// single-frame placeholder) while leaving correctly sized multi-frame sheets untouched.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<int> _frameDiag =
            new System.Collections.Generic.HashSet<int>();

        private static void AfterFindFrame(NPC __instance)
        {
            if (Main.dedServ || __instance == null) return;
            var def = _content?.GetNpc(__instance.type);
            if (def == null) return;
            try
            {
                if (_npcTextureSlots == null) _npcTextureSlots = TextureAssetSlots.Resolve(_log, "Npc");
                var tex = _npcTextureSlots?.GetTexture(__instance.type);
                if (tex != null)
                {
                    var frameHeight = Math.Max(1, tex.Height / Math.Max(1, def.FrameCount));
                    if (__instance.frame.Width <= 0 || __instance.frame.Width > tex.Width) __instance.frame.Width = tex.Width;
                    __instance.frame.Height = frameHeight;
                    if (__instance.frame.X < 0 || __instance.frame.X >= tex.Width) __instance.frame.X = 0;
                    if (__instance.frame.Y < 0 || __instance.frame.Y + frameHeight > tex.Height) __instance.frame.Y = 0;
                }

                // One-shot per type: capture the exact draw state so an invisible custom NPC can be
                // diagnosed from the log (texture present? frame in-bounds? alpha/scale/position sane?).
                if (_frameDiag.Add(__instance.type))
                {
                    var f = __instance.frame;
                    _log?.Info("Content diag [NPC draw] " + def.ContentKey + " type=" + __instance.type
                        + " tex=" + (tex == null ? "NULL" : tex.Width + "x" + tex.Height)
                        + " npcFrameCount=" + Main.npcFrameCount[__instance.type]
                        + " frame=(" + f.X + "," + f.Y + "," + f.Width + "," + f.Height + ")"
                        + " alpha=" + __instance.alpha + " scale=" + __instance.scale
                        + " color=" + __instance.color + " active=" + __instance.active
                        + " pos=(" + (int)__instance.position.X + "," + (int)__instance.position.Y + ")");
                }
            }
            catch { /* never break vanilla framing */ }
        }

        /// <summary>
        /// Render framework NPCs ourselves. Vanilla's DrawNPCs loop skips any NPC whose
        /// <c>type &gt;= NPCID.Count</c> — and that comparison was baked into Terraria.dll against the
        /// vanilla count, so our runtime-expanded count never lets a custom id (type &gt;= vanilla
        /// count) reach DrawNPCDirect at all. The NPC updates but is never drawn. We therefore draw
        /// every framework NPC from a DrawNPCs postfix (which always runs, in the same still-open
        /// SpriteBatch), and suppress vanilla's own body draw in the prefix so a custom id that *does*
        /// reach DrawNPCDirect on some build is not drawn twice.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<int> _drawDiag =
            new System.Collections.Generic.HashSet<int>();

        // Prefix: for framework NPCs, suppress vanilla's body draw — the DrawNPCs postfix owns it.
        private static bool BeforeDrawNPCDirect(NPC rCurrentNPC)
        {
            if (Main.dedServ || rCurrentNPC == null) return true;
            if (rCurrentNPC.IsABestiaryIconDummy || rCurrentNPC.IsAPortraitDummy) return true;  // UI dummies: vanilla
            return _content?.GetNpc(rCurrentNPC.type) == null;                                  // false = skip vanilla
        }

        // Postfix: draw every active framework NPC in this behind/in-front pass ourselves.
        private static void AfterDrawNPCs(bool behindTiles)
        {
            if (Main.dedServ) return;
            try
            {
                var screenPos = Main.screenPosition;
                for (var i = 0; i < Main.npc.Length; i++)
                {
                    var npc = Main.npc[i];
                    if (npc == null || !npc.active || npc.type <= 0) continue;
                    if (npc.hide || npc.behindTiles != behindTiles) continue;
                    if (_content?.GetNpc(npc.type) == null) continue;
                    DrawFrameworkNpc(Main.spriteBatch, npc, screenPos);
                }
            }
            catch (Exception ex) { _log?.Error("Content: framework NPC draw pass failed", ex); }
        }

        private static void DrawFrameworkNpc(SpriteBatch batch, NPC npc, Vector2 screenPos)
        {
            var def = _content?.GetNpc(npc.type);
            if (def == null) return;
            try
            {
                if (_npcTextureSlots == null) _npcTextureSlots = TextureAssetSlots.Resolve(_log, "Npc");
                var tex = _npcTextureSlots?.GetTexture(npc.type);
                if (tex == null) return;

                var type = npc.type;
                var fc = Math.Max(1, Main.npcFrameCount[type]);
                var frameH = Math.Max(1, tex.Height / fc);

                var frame = npc.frame;
                if (frame.Width <= 0 || frame.Width > tex.Width) frame.Width = tex.Width;
                frame.Height = frameH;
                if (frame.X < 0 || frame.X >= tex.Width) frame.X = 0;
                if (frame.Y < 0 || frame.Y + frameH > tex.Height) frame.Y = 0;

                var origin = new Vector2(tex.Width / 2f, frameH / 2f);
                var effects = npc.spriteDirection == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                var light = Lighting.GetColor((int)(npc.Center.X / 16f), (int)(npc.Center.Y / 16f));
                var drawColor = npc.GetAlpha(light);

                var drawPos = new Vector2(
                    npc.position.X - screenPos.X + npc.width / 2f
                        - tex.Width * npc.scale / 2f + origin.X * npc.scale,
                    npc.position.Y - screenPos.Y + npc.height
                        - tex.Height * npc.scale / fc + 4f + origin.Y * npc.scale + npc.gfxOffY);

                batch.Draw(tex, drawPos, frame, drawColor, npc.rotation, origin, npc.scale, effects, 0f);

                if (_drawDiag.Add(type))
                    _log?.Info("Content diag [NPC render] " + def.ContentKey + " type=" + type
                        + " tex=" + tex.Width + "x" + tex.Height
                        + " frame=(" + frame.X + "," + frame.Y + "," + frame.Width + "," + frame.Height + ")"
                        + " light=" + light + " drawColor=" + drawColor
                        + " drawPos=(" + (int)drawPos.X + "," + (int)drawPos.Y + ")"
                        + " scale=" + npc.scale + " screenPos=(" + (int)screenPos.X + "," + (int)screenPos.Y + ")");
            }
            catch (Exception ex) { _log?.Error("Content: custom NPC draw failed for " + def.ContentKey, ex); }
        }

        private static bool BeforeGetChat(NPC __instance, ref string __result)
        {
            var def = _content?.GetNpc(__instance?.type ?? 0);
            if (def == null) return true;
            try { def.Npc = __instance; __result = def.GetChat(Main.LocalPlayer) ?? ""; }
            catch (Exception ex) { _log?.Error("Content: NPC GetChat failed for " + def.ContentKey, ex); __result = ""; }
            finally { def.Npc = null; }
            return false;
        }

        private static bool BeforeGetName(int __0, ref string __result)
        {
            var def = _content?.GetNpc(__0);
            if (def == null) return true;
            __result = def.DisplayName ?? "";
            return false;
        }

        /// <summary>
        /// SceneMetrics instances are constructed before content activation, so every array they
        /// hold that is indexed by NPC type keeps the vanilla NPCID.Count length. Those are
        /// instance fields, invisible to the assembly-wide static array expander, so
        /// ScanNPCPositions throws IndexOutOfRange the moment a custom town NPC (id >= vanilla
        /// count) is nearby. Grow every vanilla-length NPC-indexed array on this metrics object
        /// before it scans — mirrors the tile-side BeforeSceneTileScan fix and covers Main's
        /// player and camera metrics as well as any instance created later. Matching by length
        /// (== vanilla count) is exactly how VanillaArrayExpander finds id-indexed arrays, and it
        /// leaves the tile-count array (a different length) untouched.
        /// </summary>
        private static bool BeforeSceneNpcScan(SceneMetrics __instance)
        {
            if (__instance == null || _content == null)
                return true;
            try
            {
                var oldCount = _content.VanillaNpcCount;
                int required;
                try { required = (int)Terraria.ID.NPCID.Count; }
                catch { return true; }
                if (required <= oldCount)
                    return true;

                var safe = true;
                foreach (var f in typeof(SceneMetrics).GetFields(
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!f.FieldType.IsArray || f.FieldType.GetArrayRank() != 1)
                        continue;
                    Array current;
                    try { current = f.GetValue(__instance) as Array; }
                    catch { continue; }
                    if (current == null || current.Length != oldCount)
                        continue;

                    try
                    {
                        var grown = Array.CreateInstance(f.FieldType.GetElementType(), required);
                        Array.Copy(current, grown, current.Length);
                        f.SetValue(__instance, grown);
                        _log?.Info("Content: expanded SceneMetrics." + f.Name + " "
                                   + current.Length + " -> " + required);
                    }
                    catch (Exception ex)
                    {
                        safe = false;
                        _log?.Warn("Content: could not expand SceneMetrics." + f.Name + ": " + ex.Message);
                    }
                }

                // A short array left in place would crash the scan; skipping one scan only leaves
                // biome NPC counts briefly stale, which is a safe degradation.
                if (!safe)
                    _log?.Error("Content: a SceneMetrics NPC array could not be grown — skipping this NPC scan");
                return safe;
            }
            catch (Exception ex)
            {
                _log?.Error("Content: SceneMetrics NPC-array expansion failed — skipping this NPC scan", ex);
                return false;
            }
        }

        private static TimfNpc CurrentTalkingDefinition()
        {
            try
            {
                var index = Main.LocalPlayer.talkNPC;
                if (index < 0 || index >= Main.npc.Length) return null;
                return _content?.GetNpc(Main.npc[index].type);
            }
            catch { return null; }
        }

        private static int _customShopSlot = -1;

        /// <summary>
        /// NPCInteractions.Initialize rebuilds the master interaction list, so re-add ours after each
        /// rebuild. In 1.4.5.x the NPCChatPanel draws one button per NPCInteractions.All entry whose
        /// Condition() passes for the current talk NPC.
        /// </summary>
        private static void AfterInteractionsInitialized() => RegisterChatInteractions();

        private static void RegisterChatInteractions()
        {
            try
            {
                var listField = AccessTools.Field(typeof(Terraria.GameContent.NPCInteractions), "All");
                var list = listField?.GetValue(null) as System.Collections.IList;
                if (list == null)
                {
                    _log?.Error("Content: NPCInteractions.All not found — custom NPC shop/quest buttons unavailable");
                    return;
                }
                foreach (var existing in list)
                    if (existing is TimfShopInteraction) return; // already registered this cycle
                list.Add(new TimfShopInteraction());
                list.Add(new TimfQuestInteraction());
                _log?.Info("Content: registered custom NPC shop/quest chat interactions");
            }
            catch (Exception ex) { _log?.Error("Content: could not register NPC chat interactions", ex); }
        }

        /// <summary>Adds the "Shop" button for a framework town NPC that exposes a shop.</summary>
        private sealed class TimfShopInteraction : Terraria.GameContent.NPCInteraction
        {
            public override bool Condition()
            {
                var def = CurrentTalkingDefinition();
                if (def == null) return false;
                try { var shop = def.GetShop(Main.LocalPlayer); return shop != null && shop.Count > 0; }
                catch { return false; }
            }

            public override string GetText()
            {
                try { return Terraria.Lang.inter[28].Value; } catch { return "Shop"; }
            }

            public override void Interact()
            {
                var def = CurrentTalkingDefinition();
                if (def == null) return;
                try { OpenCustomShop(def, Main.LocalPlayer); }
                catch (Exception ex) { _log?.Error("Content: opening NPC shop failed for " + def.ContentKey, ex); }
            }
        }

        /// <summary>Adds the "Quest" button for a framework town NPC with an active daily quest.</summary>
        private sealed class TimfQuestInteraction : Terraria.GameContent.NPCInteraction
        {
            public override bool Condition()
            {
                var def = CurrentTalkingDefinition();
                return def != null && NpcQuestSystem.Current(def, Main.LocalPlayer) != null;
            }

            public override string GetText() => "Quest";

            public override void Interact()
            {
                var def = CurrentTalkingDefinition();
                if (def == null) return;
                try { Main.npcChatText = NpcQuestSystem.TryComplete(def, Main.LocalPlayer); }
                catch (Exception ex) { _log?.Error("Content: completing NPC quest failed for " + def.ContentKey, ex); }
            }
        }

        /// <summary>
        /// Mirror the vanilla "&lt;name&gt; has awoken!" broadcast for framework bosses. Vanilla only
        /// announces from NPC.SpawnBoss/SpawnOnPlayer, so a boss created through NewNPC stays silent.
        /// The template is localized via the vanilla "Announcement.HasAwoken" key; the name is the
        /// mod's DisplayName (GetTypeNetName would resolve to the empty vanilla name key for a modded
        /// type). Skips the boss-spawn source so a boss routed through SpawnBoss is not announced twice.
        /// </summary>
        private static void AfterNewNPC(Terraria.DataStructures.IEntitySource __0, int __result)
        {
            try
            {
                if (Main.netMode == 1) return;                                     // clients get the server broadcast
                if (__0 is Terraria.DataStructures.EntitySource_BossSpawn) return;  // vanilla already announced
                if (__result < 0 || __result >= Main.npc.Length) return;
                var npc = Main.npc[__result];
                if (npc == null || !npc.active || !npc.boss) return;
                var def = _content?.GetNpc(npc.type);
                if (def == null) return;                                            // only framework NPCs
                var name = Terraria.Localization.NetworkText.FromLiteral(def.DisplayName ?? npc.TypeName);
                Terraria.Chat.ChatHelper.BroadcastChatMessage(
                    Terraria.Localization.NetworkText.FromKey("Announcement.HasAwoken", new object[] { name }),
                    new Color(175, 75, 255), -1);
            }
            catch (Exception ex) { _log?.Error("Content: custom boss awoken announcement failed", ex); }
        }

        /// <summary>Drops our appended shop slot when the chat/sign closes, so it cannot strand.</summary>
        private static void AfterCloseNpcChat()
        {
            if (_customShopSlot >= 0 && Main.npcShop == _customShopSlot)
            {
                try { Main.SetNPCShopIndex(0); } catch { /* ignore */ }
            }
        }

        /// <summary>Close the vanilla NPC chat panel (keeps talkNPC) via reflection.</summary>
        private static void CloseChatPanel()
        {
            try
            {
                var field = AccessTools.Field(typeof(Main), "_newChatPanel");
                var panel = field?.GetValue(Main.instance);
                if (panel == null) return;
                AccessTools.Method(panel.GetType(), "Close")?.Invoke(panel, null);
            }
            catch (Exception ex) { _log?.Error("Content: closing NPC chat panel failed", ex); }
        }

        private static void OpenCustomShop(TimfNpc def, Player player)
        {
            var entries = def.GetShop(player);
            if (entries == null || entries.Count == 0) return;
            var shops = Main.instance.shop;
            if (_customShopSlot < 0)
            {
                var grown = new Chest[shops.Length + 1];
                Array.Copy(shops, grown, shops.Length);
                _customShopSlot = shops.Length;
                Main.instance.shop = grown;
                shops = grown;
            }
            var slot = _customShopSlot;
            var chest = Chest.CreateShop();
            shops[slot] = chest;
            var write = 0;
            for (var i = 0; i < entries.Count && write < chest.item.Length; i++)
            {
                var entry = entries[i];
                if (entry == null || entry.ItemType <= 0 || (entry.Condition != null && !entry.Condition(player))) continue;
                var item = new Item(); item.SetDefaults(entry.ItemType);
                if (item.type != entry.ItemType) continue;
                item.stack = Math.Max(1, Math.Min(item.maxStack, entry.Stack));
                item.shopCustomPrice = entry.CustomPrice; item.isAShopItem = true;
                chest.item[write++] = item;
            }
            Main.playerInventory = true; Main.stackSplit = 9999; Main.npcChatText = ""; Main.SetNPCShopIndex(slot);
            // Match vanilla shop-open: close the NPC chat panel so it doesn't overlap the inventory
            // shop and strand the interface (which hid the HUD / froze input). talkNPC stays set, so
            // the shop stays open until the player walks away or closes the inventory.
            CloseChatPanel();
        }
    }
}
