using System;
using System.Reflection;
using HarmonyLib;
using Terraria;
using TIMF.Abstractions;

namespace TIMF.Core.Content
{
    /// <summary>
    /// Widens the id space at the one moment it is safe to: right after the game finishes
    /// building its own content.
    ///
    /// TIMF is injected while Terraria is still booting, so mods are loaded long before
    /// <c>Main.Initialize_AlmostEverything</c> runs on the splash screen. Growing
    /// <c>ItemID.Count</c> during mod load therefore lands in the middle of vanilla's setup,
    /// and <c>ItemID.Sets.PostSetupContent</c> — which walks the id range looking every entry
    /// up in <c>ItemID.Search</c> — dies with KeyNotFoundException on the ids we invented.
    /// </summary>
    internal static class ContentBootstrapPatch
    {
        private static ContentManager _content;
        private static ILogger _log;

        internal static void Install(Harmony harmony, ContentManager content, ILogger log)
        {
            _content = content;
            _log = log;

            try
            {
                var target = AccessTools.Method(typeof(Main), "Initialize_AlmostEverything");
                if (target == null)
                {
                    log.Error("Content: Main.Initialize_AlmostEverything not found — falling back to "
                              + "the main-menu trigger, which is later but still safe");
                    return;
                }

                harmony.Patch(target,
                    postfix: new HarmonyMethod(typeof(ContentBootstrapPatch), nameof(AfterVanillaSetup)));
                log.Info("Content: id-space expansion armed on Main.Initialize_AlmostEverything");

                // If TIMF attached so late that the method already ran, the postfix will never
                // fire, so activate right here instead.
                if (VanillaSetupAlreadyRan())
                {
                    log.Info("Content: vanilla setup had already finished at patch time — activating now");
                    AfterVanillaSetup();
                }
            }
            catch (Exception ex)
            {
                log.Error("Content: could not arm the id-space expansion hook", ex);
            }
        }

        private static void AfterVanillaSetup()
        {
            try { _content?.ActivateAfterVanillaSetup(); }
            catch (Exception ex) { _log?.Error("Content: activation after vanilla setup failed", ex); }
        }

        /// <summary>
        /// Has <c>Initialize_AlmostEverything</c> already completed?
        ///
        /// <c>Main.recipe</c> is filled in a loop near the end of that method, after
        /// <c>ItemID.Sets.PostSetupContent</c>, so a populated slot 0 means the dangerous part
        /// is behind us. Deliberately not <c>Main.gameMenu</c>: that field is initialised to
        /// true, so testing it fires on the very first splash frame — before vanilla has set
        /// anything up — which is exactly how expanding too early crashed PostSetupContent.
        /// </summary>
        private static bool VanillaSetupAlreadyRan()
        {
            try { return Main.recipe != null && Main.recipe.Length > 0 && Main.recipe[0] != null; }
            catch { return false; }
        }
    }
}
