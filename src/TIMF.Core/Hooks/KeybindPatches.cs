using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.GameInput;
using Terraria.Localization;
using Terraria.UI;
using TIMF.Abstractions;
using TIMF.Core.Keybinds;

namespace TIMF.Core.Hooks
{
    /// <summary>
    /// Injects TIMF keybinds into the vanilla Controls UI — one titled panel per mod.
    ///
    /// Patches are registered MANUALLY via <see cref="Install"/> (not only PatchAll),
    /// because method-level [HarmonyPatch] without a class-level attribute is easy to miss.
    /// </summary>
    internal static class KeybindUiPatches
    {
        private static KeybindService _service;
        private static ILogger _log;
        private static MethodInfo _createBindingGroup;
        private static MethodInfo _fillList;
        private static FieldInfo _bindsKeyboard;
        private static FieldInfo _bindsKeyboardUI;
        private static FieldInfo _bindsGamepad;
        private static FieldInfo _bindsGamepadUI;
        private static FieldInfo _orderIndexField;
        private static FieldInfo _elementsField;
        private static bool _resolved;
        private static bool _resolveFailed;
        private static bool _installed;
        private static int _injectLogBudget = 5;

        private static readonly Color[] GroupTints =
        {
            Color.Turquoise,
            Color.Goldenrod,
            Color.HotPink,
            Color.Indigo,
            Color.LimeGreen,
            Color.Orange,
            Color.MediumPurple,
            Color.DeepSkyBlue,
        };

        internal static void SetService(KeybindService service, ILogger log = null)
        {
            _service = service;
            if (log != null)
                _log = log;
        }

        /// <summary>
        /// Explicitly register Harmony patches. Call from GameHooks.Install after PatchAll.
        /// </summary>
        internal static void Install(Harmony harmony, ILogger log)
        {
            _log = log;
            if (_installed)
                return;

            try
            {
                var fillList = AccessTools.Method(typeof(UIManageControls), "FillList");
                var assemble = AccessTools.Method(typeof(UIManageControls), "AssembleBindPanels");
                var onInit = AccessTools.Method(typeof(UIManageControls), "OnInitialize");
                var initPi = AccessTools.Method(typeof(PlayerInput), "Initialize");
                var friendly = AccessTools.Method(typeof(UIKeybindingListItem), "GetFriendlyName");

                if (fillList != null)
                {
                    harmony.Patch(fillList,
                        prefix: new HarmonyMethod(typeof(KeybindUiPatches), nameof(FillList_Prefix)));
                    Log("Patched UIManageControls.FillList (prefix)");
                }
                else
                {
                    LogError("UIManageControls.FillList not found");
                }

                if (assemble != null)
                {
                    harmony.Patch(assemble,
                        postfix: new HarmonyMethod(typeof(KeybindUiPatches), nameof(AssembleBindPanels_Postfix)));
                    Log("Patched UIManageControls.AssembleBindPanels (postfix)");
                }
                else
                {
                    LogError("UIManageControls.AssembleBindPanels not found");
                }

                if (onInit != null)
                {
                    harmony.Patch(onInit,
                        postfix: new HarmonyMethod(typeof(KeybindUiPatches), nameof(OnInitialize_Postfix)));
                    Log("Patched UIManageControls.OnInitialize (postfix)");
                }

                if (initPi != null)
                {
                    harmony.Patch(initPi,
                        postfix: new HarmonyMethod(typeof(KeybindUiPatches), nameof(PlayerInput_Initialize_Postfix)));
                    Log("Patched PlayerInput.Initialize (postfix)");
                }

                if (friendly != null)
                {
                    harmony.Patch(friendly,
                        postfix: new HarmonyMethod(typeof(KeybindUiPatches), nameof(GetFriendlyName_Postfix)));
                    Log("Patched UIKeybindingListItem.GetFriendlyName (postfix)");
                }

                _installed = true;
                EnsureResolved();
                Log("Keybind UI patches installed. resolved=" + _resolved + " groups=" +
                    (_service != null ? _service.GetGroupsByMod().Count.ToString() : "null-svc"));
            }
            catch (Exception ex)
            {
                LogError("KeybindUiPatches.Install failed", ex);
            }
        }

        private static void EnsureResolved()
        {
            if (_resolved || _resolveFailed)
                return;

            try
            {
                var t = typeof(UIManageControls);
                _createBindingGroup = AccessTools.Method(
                    t,
                    "CreateBindingGroup",
                    new[] { typeof(int), typeof(List<string>), typeof(InputMode) });
                _fillList = AccessTools.Method(t, "FillList");
                _bindsKeyboard = AccessTools.Field(t, "_bindsKeyboard");
                _bindsKeyboardUI = AccessTools.Field(t, "_bindsKeyboardUI");
                _bindsGamepad = AccessTools.Field(t, "_bindsGamepad");
                _bindsGamepadUI = AccessTools.Field(t, "_bindsGamepadUI");
                _orderIndexField = AccessTools.Field(typeof(UISortableElement), "OrderIndex");
                _elementsField = AccessTools.Field(typeof(UIElement), "Elements");

                _resolved = _createBindingGroup != null
                            && _bindsKeyboard != null
                            && _bindsKeyboardUI != null
                            && _orderIndexField != null;

                if (!_resolved)
                {
                    _resolveFailed = true;
                    LogError("Keybind UI resolve failed: createGroup=" + (_createBindingGroup != null)
                             + " kb=" + (_bindsKeyboard != null)
                             + " kbUI=" + (_bindsKeyboardUI != null)
                             + " order=" + (_orderIndexField != null));
                }
                else
                {
                    Log("Keybind UI reflection resolved OK");
                }
            }
            catch (Exception ex)
            {
                _resolveFailed = true;
                LogError("Keybind UI EnsureResolved exception", ex);
            }
        }

        // ---- Patch bodies (public so HarmonyMethod can bind by name) ----

        public static void PlayerInput_Initialize_Postfix()
        {
            try { _service?.ReinjectAll(); }
            catch (Exception ex) { LogError("PlayerInput.Initialize postfix", ex); }
        }

        public static void FillList_Prefix(UIManageControls __instance)
        {
            try { EnsureModGroupsPresent(__instance, "FillList"); }
            catch (Exception ex) { LogError("FillList prefix", ex); }
        }

        public static void AssembleBindPanels_Postfix(UIManageControls __instance)
        {
            try { EnsureModGroupsPresent(__instance, "Assemble"); }
            catch (Exception ex) { LogError("Assemble postfix", ex); }
        }

        /// <summary>
        /// After OnInitialize runs Assemble+FillList, inject and re-fill so the first open
        /// of the controls menu always shows TIMF groups.
        /// </summary>
        public static void OnInitialize_Postfix(UIManageControls __instance)
        {
            try
            {
                EnsureModGroupsPresent(__instance, "OnInitialize");
                // Force a second FillList so the freshly-appended groups appear.
                if (_fillList != null)
                    _fillList.Invoke(__instance, null);
            }
            catch (Exception ex)
            {
                LogError("OnInitialize postfix", ex);
            }
        }

        public static void GetFriendlyName_Postfix(object __instance, ref string __result)
        {
            try
            {
                if (_service == null)
                    return;

                var field = AccessTools.Field(__instance.GetType(), "_keybind");
                if (field == null)
                    return;

                var id = field.GetValue(__instance) as string;
                if (string.IsNullOrEmpty(id))
                    return;

                string display;
                if (_service.TryGetDisplayName(id, out display) && !string.IsNullOrEmpty(display))
                    __result = display;
            }
            catch
            {
                // ignore
            }
        }

        // ---- Injection ----

        private static void EnsureModGroupsPresent(UIManageControls instance, string caller)
        {
            if (_service == null || instance == null)
            {
                BudgetLog("EnsureModGroupsPresent(" + caller + "): service/instance null");
                return;
            }

            var groups = _service.GetGroupsByMod();
            if (groups == null || groups.Count == 0)
            {
                BudgetLog("EnsureModGroupsPresent(" + caller + "): no groups registered");
                return;
            }

            _service.ReinjectAll();
            EnsureResolved();
            if (_resolveFailed)
            {
                BudgetLog("EnsureModGroupsPresent(" + caller + "): resolve failed");
                return;
            }

            if (AlreadyInjected(instance))
            {
                BudgetLog("EnsureModGroupsPresent(" + caller + "): already injected, skip");
                return;
            }

            const int colorIndex = 4; // turquoise chrome; title overwritten
            var sortBase = 50;
            var added = 0;

            for (var g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                if (group.Ids == null || group.Ids.Count == 0)
                    continue;

                // Fresh list per group (CreateBindingGroup / CreateElementGroup must not share mutables).
                var bindings = new List<string>(group.Ids.Count + 1);
                for (var i = 0; i < group.Ids.Count; i++)
                    bindings.Add(group.Ids[i]);
                bindings.Add("sp9");

                var order = sortBase + g;
                // Only keyboard gameplay list is what the user screenshot shows (sword tab + keyboard).
                // Still inject into all four so gamepad / UI tabs work too.
                if (AppendModGroup(instance, _bindsKeyboard, order, colorIndex, new List<string>(bindings), InputMode.Keyboard, group.Title, g))
                    added++;
                AppendModGroup(instance, _bindsKeyboardUI, order, colorIndex, new List<string>(bindings), InputMode.KeyboardUI, group.Title, g);
                if (_bindsGamepad != null)
                    AppendModGroup(instance, _bindsGamepad, order, colorIndex, new List<string>(bindings), InputMode.XBoxGamepad, group.Title, g);
                if (_bindsGamepadUI != null)
                    AppendModGroup(instance, _bindsGamepadUI, order, colorIndex, new List<string>(bindings), InputMode.XBoxGamepadUI, group.Title, g);
            }

            Log("EnsureModGroupsPresent(" + caller + "): added " + added + " keyboard groups for " + groups.Count + " mods");
        }

        private static bool AlreadyInjected(UIManageControls instance)
        {
            return ListHasTimfGroup(_bindsKeyboard, instance)
                   || ListHasTimfGroup(_bindsKeyboardUI, instance);
        }

        private static bool ListHasTimfGroup(FieldInfo listField, UIManageControls instance)
        {
            if (listField == null || _orderIndexField == null)
                return false;
            var list = listField.GetValue(instance) as List<UIElement>;
            if (list == null || list.Count == 0)
                return false;

            for (var i = 0; i < list.Count; i++)
            {
                var el = list[i] as UISortableElement;
                if (el == null)
                    continue;
                try
                {
                    if ((int)_orderIndexField.GetValue(el) >= 50)
                        return true;
                }
                catch { /* ignore */ }
            }

            return false;
        }

        private static bool AppendModGroup(
            UIManageControls instance,
            FieldInfo listField,
            int orderIndex,
            int colorIndex,
            List<string> bindings,
            InputMode mode,
            string title,
            int tintIndex)
        {
            if (listField == null)
                return false;

            var list = listField.GetValue(instance) as List<UIElement>;
            if (list == null)
            {
                BudgetLog("AppendModGroup: list null for " + title);
                return false;
            }

            UIElement group;
            try
            {
                group = _createBindingGroup.Invoke(
                    instance,
                    new object[] { colorIndex, bindings, mode }) as UIElement;
            }
            catch (Exception ex)
            {
                LogError("CreateBindingGroup failed for " + title + " mode=" + mode, ex);
                return false;
            }

            if (group == null)
            {
                BudgetLog("CreateBindingGroup returned null for " + title);
                return false;
            }

            try
            {
                if (_orderIndexField != null)
                    _orderIndexField.SetValue(group, orderIndex);
            }
            catch { /* ignore */ }

            ApplyGroupTitle(group, title, tintIndex);
            list.Add(group);
            return true;
        }

        private static void ApplyGroupTitle(UIElement group, string title, int tintIndex)
        {
            if (group == null || string.IsNullOrEmpty(title) || _elementsField == null)
                return;

            try
            {
                var children = _elementsField.GetValue(group) as List<UIElement>;
                if (children == null || children.Count == 0)
                    return;

                for (var i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    if (child == null)
                        continue;

                    var textPanel = child as UITextPanel<LocalizedText>;
                    if (textPanel != null)
                    {
                        try
                        {
                            var ctor = typeof(LocalizedText).GetConstructor(
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                                null,
                                new[] { typeof(string), typeof(string) },
                                null);
                            if (ctor != null)
                            {
                                var lt = (LocalizedText)ctor.Invoke(
                                    new object[] { "TIMF.KeybindGroup." + title, title });
                                textPanel.SetText(lt, 0.7f, false);
                            }
                        }
                        catch (Exception ex)
                        {
                            BudgetLog("SetText title failed: " + ex.Message);
                        }
                        continue;
                    }

                    var panel = child as UIPanel;
                    if (panel != null)
                    {
                        try
                        {
                            var tint = GroupTints[tintIndex % GroupTints.Length];
                            panel.BackgroundColor = Color.Lerp(panel.BackgroundColor, tint, 0.18f);
                        }
                        catch { /* ignore */ }
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private static void Log(string msg)
        {
            try { _log?.Info("[KeybindUI] " + msg); }
            catch { /* ignore */ }
        }

        private static void LogError(string msg, Exception ex = null)
        {
            try
            {
                if (ex != null)
                    _log?.Error("[KeybindUI] " + msg, ex);
                else
                    _log?.Error("[KeybindUI] " + msg);
            }
            catch { /* ignore */ }
        }

        private static void BudgetLog(string msg)
        {
            if (_injectLogBudget <= 0)
                return;
            _injectLogBudget--;
            Log(msg);
        }
    }
}
