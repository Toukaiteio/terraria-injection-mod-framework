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
                try { def.SetDefaults(); }
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
