using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;
using TIMF.Abstractions;
using TIMF.Content;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Makes vanilla treat modded ids as real items: fills in stats on
    /// <see cref="Item.SetDefaults(int)"/> and answers name lookups.
    /// </summary>
    internal static class ItemContentPatches
    {
        private static ContentManager _content;
        private static ILogger _log;

        internal static void Bind(ContentManager content, ILogger log)
        {
            _content = content;
            _log = log;
        }

        internal static void Install(Harmony harmony, ILogger log)
        {
            _log = log;
            try
            {
                var setDefaults = FindSetDefaults();
                if (setDefaults == null)
                    log.Error("Content: Item.SetDefaults(int, …) not found — custom items will be blank");
                else
                    harmony.Patch(setDefaults,
                        prefix: new HarmonyMethod(typeof(ItemContentPatches), nameof(SetDefaultsPrefix)));

                PatchLang(harmony, log, "GetItemName", nameof(GetItemNamePostfix));
                PatchLang(harmony, log, "GetItemNameValue", nameof(GetItemNameValuePostfix));
                PatchLang(harmony, log, "GetTooltip", nameof(GetTooltipPostfix));

                // Accessory effects. Vanilla applies its own bonuses here, once per tick per
                // equipped slot, which is also where modded ones have to be re-applied because
                // the player's stat fields are cleared every tick.
                var applyEquip = AccessTools.Method(typeof(Terraria.Player), "ApplyEquipFunctional");
                if (applyEquip != null)
                    harmony.Patch(applyEquip,
                        postfix: new HarmonyMethod(typeof(ItemContentPatches), nameof(ApplyEquipFunctionalPostfix)));
                else
                    log.Warn("Content: Player.ApplyEquipFunctional not found — modded accessories will have no effect");

                var updateInventory = AccessTools.Method(typeof(Player), "UpdateInventory");
                if (updateInventory == null)
                    updateInventory = AccessTools.Method(typeof(Player), "Update", new[] { typeof(int) });
                if (updateInventory != null)
                    harmony.Patch(updateInventory, postfix: new HarmonyMethod(typeof(ItemContentPatches), nameof(UpdateInventoryPostfix)));
                var holdItem = AccessTools.Method(typeof(Player), "ItemCheck");
                if (holdItem != null)
                    harmony.Patch(holdItem, postfix: new HarmonyMethod(typeof(ItemContentPatches), nameof(ItemCheckPostfix)));
                var canUse = AccessTools.Method(typeof(Player), "ItemCheck_CanUse",
                    new[] { typeof(Item), typeof(bool) });
                if (canUse != null)
                    harmony.Patch(canUse, postfix: new HarmonyMethod(typeof(ItemContentPatches), nameof(AfterCanUseItem)));
                var startUse = AccessTools.Method(typeof(Player), "ItemCheck_StartActualUse", new[] { typeof(Item) });
                if (startUse != null)
                    harmony.Patch(startUse, postfix: new HarmonyMethod(typeof(ItemContentPatches), nameof(AfterStartUseItem)));

                var updatePet = AccessTools.Method(typeof(Player), nameof(Player.UpdatePet), new[] { typeof(int) });
                if (updatePet != null)
                    harmony.Patch(updatePet, postfix: new HarmonyMethod(typeof(ItemContentPatches), nameof(AfterUpdatePet)));
                var updateLightPet = AccessTools.Method(typeof(Player), nameof(Player.UpdatePetLight), new[] { typeof(int) });
                if (updateLightPet != null)
                    harmony.Patch(updateLightPet, postfix: new HarmonyMethod(typeof(ItemContentPatches), nameof(AfterUpdateLightPet)));

                log.Info("Content: item patches installed");
            }
            catch (Exception ex)
            {
                log.Error("Content: failed to install item patches", ex);
            }
        }

        /// <summary>
        /// Resolved by shape rather than a hardcoded signature: 1.4.5 takes
        /// <c>(int, ItemVariant)</c> but the second parameter has moved between versions.
        /// </summary>
        private static MethodBase FindSetDefaults()
        {
            return typeof(Item)
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => m.Name == "SetDefaults")
                .Select(m => new { m, p = m.GetParameters() })
                .Where(x => x.p.Length >= 1 && x.p[0].ParameterType == typeof(int))
                .OrderByDescending(x => x.p.Length)   // the widest overload is the real body
                .Select(x => (MethodBase)x.m)
                .FirstOrDefault();
        }

        private static void PatchLang(Harmony harmony, ILogger log, string method, string postfix)
        {
            var target = AccessTools.Method(typeof(Lang), method, new[] { typeof(int) });
            if (target == null)
            {
                log.Warn("Content: Lang." + method + "(int) not found — modded item names may show blank");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(ItemContentPatches), postfix));
        }

        /// <summary>
        /// Vanilla's SetDefaults is a switch over known ids; a modded id would fall through it
        /// with every stat left at whatever the previous item had. So for our ids we reset the
        /// item ourselves, run the definition, and skip the vanilla body entirely.
        /// </summary>
        private static bool SetDefaultsPrefix(Item __instance, int Type)
        {
            var content = _content;
            if (content == null || __instance == null)
                return true;

            TimfItem def;
            try
            {
                if (Type < content.VanillaItemCount)
                    return true;
                def = content.GetItem(Type);
            }
            catch
            {
                return true;
            }

            if (def == null)
            {
                // An id above the vanilla range with nothing behind it: a save from a mod set
                // that is no longer installed. Blank the item rather than let vanilla index
                // its tables with an id it has no entry for.
                try { __instance.SetDefaults(0); }
                catch { /* ignore */ }
                return false;
            }

            try
            {
                ResetStats(__instance, Type);
                __instance.type = Type;
                __instance.stack = 1;
                __instance.Prefix(0);

                def.Item = __instance;
                try
                {
                    def.SetDefaults();
                    var pet = def as TimfPetItem;
                    if (pet != null)
                    {
                        // These fields are the source of truth used by ItemSlot contexts 19/20,
                        // quick-equip, tooltips, and Player.UpdatePet/UpdatePetLight. Apply them
                        // after mod defaults so a subclass cannot accidentally omit them.
                        __instance.buffType = pet.PetBuffType;
                        __instance.buffTime = Math.Max(1, pet.PetBuffDuration);
                        if (pet.PetProjectileType > 0)
                            __instance.shoot = pet.PetProjectileType;
                        __instance.consumable = false;
                    }

                    var grassSeed = def as TimfGrassSeedItem;
                    if (grassSeed != null)
                    {
                        // Seed definitions describe the conversion target; they are not ordinary
                        // blocks even though Terraria's cursor/use pipeline is entered through
                        // createTile. Apply this after mod defaults so it cannot drift.
                        __instance.createTile = grassSeed.GrassTileType;
                        __instance.consumable = true;
                    }
                }
                finally { def.Item = null; }
            }
            catch (Exception ex)
            {
                _log?.Error("Content: SetDefaults failed for " + def.ContentKey, ex);
            }

            return false;
        }

        private static MethodInfo _resetStats;

        private static void ResetStats(Item item, int type)
        {
            if (_resetStats == null)
                _resetStats = AccessTools.Method(typeof(Item), "ResetStats", new[] { typeof(int) });
            _resetStats?.Invoke(item, new object[] { type });
        }

        private static void GetItemNamePostfix(int id, ref object __result)
        {
            var def = Lookup(id);
            if (def == null)
                return;
            var text = LocalizedTextFactory.Create("TimfContent." + def.ContentKey, def.DisplayName);
            if (text != null)
                __result = text;
        }

        private static void GetItemNameValuePostfix(int id, ref string __result)
        {
            var def = Lookup(id);
            if (def != null)
                __result = def.DisplayName;
        }

        /// <summary>
        /// Supplies the hover tooltip for modded ids.
        ///
        /// Vanilla seeds every slot of <c>Lang._itemTooltipCache</c> with
        /// <c>ItemTooltip.None</c> and <c>Lang.GetTooltip</c> hands the element back unchecked,
        /// so a modded id must always resolve to a real instance. Expansion now backfills
        /// <c>None</c> for us; this adds the mod's own lines on top when it declared any.
        /// </summary>
        private static void GetTooltipPostfix(int itemId, ref object __result)
        {
            var def = Lookup(itemId);
            var lines = def?.Tooltip;
            if (lines == null || lines.Count == 0)
                return;

            var built = ItemTooltipFactory.FromLines(def.ContentKey, lines, _log);
            if (built != null)
                __result = built;
        }

        private static void ApplyEquipFunctionalPostfix(Terraria.Player __instance, Terraria.Item currentItem)
        {
            if (currentItem == null || __instance == null)
                return;

            var def = Lookup(currentItem.type);
            if (def == null || !_content.IsSessionAllowed(def))
                return;

            try
            {
                var hideVisual = false;
                try { hideVisual = currentItem.stack == 0; }
                catch { /* best effort */ }

                def.Item = currentItem;
                try { def.UpdateAccessory(__instance, hideVisual); }
                finally { def.Item = null; }
            }
            catch (Exception ex)
            {
                _log?.Error("Content: UpdateAccessory failed for " + def.ContentKey, ex);
            }
        }

        private static void UpdateInventoryPostfix(Player __instance)
        {
            if (__instance == null || __instance.inventory == null) return;
            for (var i = 0; i < __instance.inventory.Length; i++)
            {
                var item = __instance.inventory[i];
                var def = item == null ? null : Lookup(item.type);
                if (def == null || !_content.IsSessionAllowed(def)) continue;
                try { def.Item = item; def.UpdateInventory(__instance); }
                catch (Exception ex) { _log?.Error("Content: UpdateInventory failed for " + def.ContentKey, ex); }
                finally { def.Item = null; }
            }
        }

        private static void ItemCheckPostfix(Player __instance)
        {
            if (__instance == null) return;
            var item = __instance.HeldItem;
            var def = item == null ? null : Lookup(item.type);
            if (def == null || !_content.IsSessionAllowed(def)) return;
            try { def.Item = item; def.HoldItem(__instance); }
            catch (Exception ex) { _log?.Error("Content: HoldItem failed for " + def.ContentKey, ex); }
            finally { def.Item = null; }
        }

        private static void AfterCanUseItem(Player __instance, Item sItem, ref bool __result)
        {
            if (!__result || __instance == null || sItem == null) return;
            var def = Lookup(sItem.type);
            if (def == null || !_content.IsSessionAllowed(def)) return;
            try
            {
                def.Item = sItem;
                __result = def.CanUseItem(__instance);
            }
            catch (Exception ex) { _log?.Error("Content: CanUseItem failed for " + def.ContentKey, ex); __result = false; }
            finally { def.Item = null; }
        }

        private static void AfterStartUseItem(Player __instance, Item sItem)
        {
            if (__instance == null || sItem == null) return;
            var def = Lookup(sItem.type);
            if (def == null || !_content.IsSessionAllowed(def)) return;
            try { def.Item = sItem; def.OnUseItem(__instance); }
            catch (Exception ex) { _log?.Error("Content: OnUseItem failed for " + def.ContentKey, ex); }
            finally { def.Item = null; }
        }

        private static void AfterUpdatePet(Player __instance, int i)
        {
            EnsureEquippedPet(__instance, i, 0, TimfPetSlot.Pet);
        }

        private static void AfterUpdateLightPet(Player __instance, int i)
        {
            EnsureEquippedPet(__instance, i, 1, TimfPetSlot.LightPet);
        }

        /// <summary>
        /// Vanilla pet buffs normally create their projectile from Player.UpdateBuffs. This
        /// fallback runs after the original equipment-slot refresh and only creates a declared
        /// projectile when none exists, so it neither duplicates working vanilla pets nor
        /// requires a custom buff to smuggle an unmanaged projectile id through vanilla code.
        /// </summary>
        private static void EnsureEquippedPet(Player player, int playerIndex, int equipmentSlot,
            TimfPetSlot expectedSlot)
        {
            if (player == null || playerIndex != Main.myPlayer || player.whoAmI != Main.myPlayer
                || player.dead || player.miscEquips == null
                || equipmentSlot < 0 || equipmentSlot >= player.miscEquips.Length
                || player.hideMisc[equipmentSlot])
                return;

            var item = player.miscEquips[equipmentSlot];
            var pet = item == null ? null : Lookup(item.type) as TimfPetItem;
            if (pet == null || pet.PetSlot != expectedSlot || !_content.IsSessionAllowed(pet)
                || item.stack < 1 || pet.PetBuffType <= 0)
                return;

            try
            {
                if (player.FindBuffIndex(pet.PetBuffType) < 0)
                    player.AddBuff(pet.PetBuffType, Math.Max(1, pet.PetBuffDuration));

                var projectileType = pet.PetProjectileType;
                if (projectileType <= 0 || player.ownedProjectileCounts == null
                    || projectileType >= player.ownedProjectileCounts.Length
                    || player.ownedProjectileCounts[projectileType] > 0)
                    return;

                Projectile.NewProjectile(player.GetProjectileSource_Item(item),
                    player.Center.X, player.Center.Y, 0f, 0f, projectileType,
                    0, 0f, player.whoAmI, 0f, 0f, 0f, null);
            }
            catch (Exception ex)
            {
                _log?.Error("Content: equipped pet activation failed for " + pet.ContentKey, ex);
            }
        }

        private static TimfItem Lookup(int id)
        {
            var content = _content;
            if (content == null || id < content.VanillaItemCount)
                return null;
            try { return content.GetItem(id); }
            catch { return null; }
        }

        internal static bool IsItemSessionAllowed(int id)
        {
            var definition = Lookup(id);
            return definition == null || (_content != null && _content.IsSessionAllowed(definition));
        }
    }

    /// <summary>
    /// Builds <c>ItemTooltip</c> instances from plain lines.
    ///
    /// Resolved late-bound off <c>Lang.GetTooltip</c>'s own return type rather than a
    /// compile-time reference, the same way the texture code reaches ReLogic — it keeps this
    /// working regardless of which assembly the running game actually loaded the type from.
    /// </summary>
    internal static class ItemTooltipFactory
    {
        private static MethodInfo _fromHardcodedText;
        private static bool _resolved;
        private static readonly Dictionary<string, object> Cache =
            new Dictionary<string, object>(StringComparer.Ordinal);

        public static object FromLines(string contentKey, IReadOnlyList<string> lines, ILogger log)
        {
            object cached;
            if (Cache.TryGetValue(contentKey, out cached))
                return cached;

            if (!_resolved)
            {
                _resolved = true;
                try
                {
                    var tooltipType = AccessTools.Method(typeof(Lang), "GetTooltip", new[] { typeof(int) })
                        ?.ReturnType;
                    _fromHardcodedText = tooltipType?.GetMethod("FromHardcodedText",
                        BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string[]) }, null);
                    if (_fromHardcodedText == null)
                        log?.Warn("Content: ItemTooltip.FromHardcodedText(string[]) not found; "
                                  + "modded items will show no tooltip lines");
                }
                catch (Exception ex)
                {
                    log?.Warn("Content: resolving ItemTooltip factory failed: " + ex.Message);
                }
            }

            if (_fromHardcodedText == null)
                return null;

            try
            {
                var array = new string[lines.Count];
                for (var i = 0; i < lines.Count; i++)
                    array[i] = lines[i] ?? "";

                var instance = _fromHardcodedText.Invoke(null, new object[] { array });
                Cache[contentKey] = instance;
                return instance;
            }
            catch (Exception ex)
            {
                log?.Error("Content: building tooltip for " + contentKey + " failed", ex);
                return null;
            }
        }
    }

    /// <summary>
    /// Builds <c>LocalizedText</c> instances. Its constructor is non-public, so this reaches
    /// for it reflectively and caches the result per key.
    /// </summary>
    internal static class LocalizedTextFactory
    {
        private static ConstructorInfo _ctor;
        private static bool _resolved;
        private static readonly Dictionary<string, object> Cache =
            new Dictionary<string, object>(StringComparer.Ordinal);

        public static object Create(string key, string value)
        {
            object cached;
            if (Cache.TryGetValue(key, out cached))
                return cached;

            if (!_resolved)
            {
                _resolved = true;
                var t = typeof(Terraria.Localization.LocalizedText);
                _ctor = t.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(string), typeof(string) }, null);
            }

            if (_ctor == null)
                return null;

            try
            {
                var instance = _ctor.Invoke(new object[] { key, value ?? "" });
                Cache[key] = instance;
                return instance;
            }
            catch
            {
                return null;
            }
        }
    }
}
