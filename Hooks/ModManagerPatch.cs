using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;
using BetterModSort.AI;
using BetterModSort.Tools;

namespace BetterModSort.Hooks
{
    [StaticConstructorOnStartup]
    public static class ModManagerPatch
    {
        public static bool ExecuteModManagerSort = false;

        private static Type? _modButtonManagerType;
        private static MethodInfo? _sortMethod;
        private static MethodInfo? _activeButtonsGetter;
        private static MethodInfo? _issuesGetter;
        private static Type? _dependencyType;
        private static PropertyInfo? _severityProp;

        private static Type? _beforeType;
        private static Type? _afterType;
        private static Type? _incompatType;

        // Lazy cached properties for ModButton and Mod Button Data
        private static MethodInfo? _btnManifestProp;
        private static MethodInfo? _btnModProp;
        private static MethodInfo? _manifestLoadBeforeProp;
        private static MethodInfo? _manifestLoadAfterProp;
        private static MethodInfo? _manifestIncompProp;

        static ModManagerPatch()
        {
            var inst = BetterModSortMod.HarmonyInstance;
            if (inst != null)
                PatchIfNeeded(inst);
        }

        public static void PatchIfNeeded(Harmony harmony)
        {
            _modButtonManagerType = AccessTools.TypeByName("ModManager.ModButtonManager");
            if (_modButtonManagerType != null)
            {
                var anyIssueMethod = AccessTools.PropertyGetter(_modButtonManagerType, "AnyIssue");
                if (anyIssueMethod != null)
                    harmony.Patch(anyIssueMethod, postfix: new HarmonyMethod(typeof(ModManagerPatch), nameof(AnyIssue_Postfix)));
                
                _sortMethod = AccessTools.Method(_modButtonManagerType, "Sort");
                if (_sortMethod != null)
                    harmony.Patch(_sortMethod, prefix: new HarmonyMethod(typeof(ModManagerPatch), nameof(Sort_Prefix)));
                
                _activeButtonsGetter = AccessTools.PropertyGetter(_modButtonManagerType, "ActiveButtons");
                _issuesGetter = AccessTools.PropertyGetter(_modButtonManagerType, "Issues");

                _dependencyType = AccessTools.TypeByName("ModManager.Dependency");
                if (_dependencyType != null)
                    _severityProp = AccessTools.Property(_dependencyType, "Severity");

                _beforeType = AccessTools.TypeByName("ModManager.LoadOrder_Before");
                _afterType = AccessTools.TypeByName("ModManager.LoadOrder_After");
                _incompatType = AccessTools.TypeByName("ModManager.Incompatible");

                var pageBetterModConfigType = AccessTools.TypeByName("ModManager.Page_BetterModConfig");
                if (pageBetterModConfigType != null)
                {
                    var closeMethod = AccessTools.Method(pageBetterModConfigType, "Close");
                    if (closeMethod != null)
                        harmony.Patch(closeMethod, prefix: new HarmonyMethod(typeof(ModManagerPatch), nameof(Page_BetterModConfig_Close_Prefix)));
                }

                // Pre-cache property getters using base types to avoid TargetException
                var modButtonType = AccessTools.TypeByName("ModManager.ModButton");
                if (modButtonType != null)
                {
                    _btnManifestProp = AccessTools.PropertyGetter(modButtonType, "Manifest");
                    _btnModProp = AccessTools.PropertyGetter(modButtonType, "Mod");
                }

                var manifestType = AccessTools.TypeByName("ModManager.Manifest");
                if (manifestType != null)
                {
                    _manifestLoadBeforeProp = AccessTools.PropertyGetter(manifestType, "LoadBefore");
                    _manifestLoadAfterProp = AccessTools.PropertyGetter(manifestType, "LoadAfter");
                    _manifestIncompProp = AccessTools.PropertyGetter(manifestType, "Incompatibilities");
                }

                Log.Message("[BetterModSort] Successfully hooked ModManager for AI sorting support.");
            }
        }

        public static void AnyIssue_Postfix(ref bool __result)
        {
            if (!BetterModSortMod.Settings.EnableAISorting)
                return;
            __result = true;
        }

        public static bool Page_BetterModConfig_Close_Prefix(Window __instance, bool doCloseSound = true)
        {
            if (!BetterModSortMod.Settings.EnableAISorting)
                return true;

            // Check if there are ACTUAL severe issues
            bool hasSevereIssues = false;
            if (_issuesGetter != null && _severityProp != null)
            {
                var issues = _issuesGetter.Invoke(null, null) as IEnumerable;
                if (issues != null)
                    foreach (var issue in issues)
                        if (issue != null)
                        {
                            var severity = (int)_severityProp.GetValue(issue);
                            if (severity > 1)
                            {
                                hasSevereIssues = true;
                                break;
                            }
                        }
            }

            // If there are real issues, let ModManager's Close() run (which will show the warning dialog)
            if (hasSevereIssues)
                return true;

            // No real issues, just us spoofing AnyIssue = true. Bypass the warning dialog.
            Find.WindowStack.TryRemove(__instance, doCloseSound);
            return false;
        }

        public static bool Sort_Prefix()
        {
            if (ExecuteModManagerSort)
                return true;
            if (!BetterModSortMod.Settings.EnableAISorting)
                return true;
            Find.WindowStack.Add(new Dialog_AILoading(SortInvoker.ModManager));
            return false;
        }

        /// <summary>
        /// Reads ModManager.ModButtonManager.ActiveButtons and writes dependencies.
        /// </summary>
        public static List<string> ApplyConstraintsToModManagerAndSort(List<SoftConstraintInfo> constraints)
        {
            var sortedIds = new List<string>();
            try
            {
                if (_modButtonManagerType == null || _activeButtonsGetter == null) return sortedIds;

                var activeButtons = (IEnumerable)_activeButtonsGetter.Invoke(null, null);
                if (activeButtons == null) return sortedIds;

                // Cache Manifest elements by packageId
                var manifestDict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var btn in activeButtons)
                {
                    if (btn == null) continue;

                    if (_btnManifestProp != null && _btnModProp != null)
                    {
                        var manifest = _btnManifestProp.Invoke(btn, null);
                        var mod = _btnModProp.Invoke(btn, null) as ModMetaData;
                        if (manifest != null && mod != null && !string.IsNullOrEmpty(mod.PackageId))
                            manifestDict[mod.PackageId] = manifest;
                    }
                }

                foreach (var constraint in constraints)
                {
                    if (string.IsNullOrEmpty(constraint.PackageId)) continue;
                    if (!manifestDict.TryGetValue(constraint.PackageId!, out object? manifest) || manifest == null) continue;

                    if (constraint.LoadBefore != null && _manifestLoadBeforeProp != null && _beforeType != null)
                        if (_manifestLoadBeforeProp.Invoke(manifest, null) is IList list)
                            foreach (var target in constraint.LoadBefore)
                                list.Add(Activator.CreateInstance(_beforeType, manifest, target));

                    if (constraint.LoadAfter != null && _manifestLoadAfterProp != null && _afterType != null)
                        if (_manifestLoadAfterProp.Invoke(manifest, null) is IList list)
                            foreach (var target in constraint.LoadAfter)
                                list.Add(Activator.CreateInstance(_afterType, manifest, target));

                    if (constraint.IncompatibleWith != null && _manifestIncompProp != null && _incompatType != null)
                        if (_manifestIncompProp.Invoke(manifest, null) is IList list)
                            foreach (var target in constraint.IncompatibleWith)
                                list.Add(Activator.CreateInstance(_incompatType, manifest, target));
                }

                if (_sortMethod != null)
                {
                    ExecuteModManagerSort = true;
                    try
                    {
                        _sortMethod.Invoke(null, null);
                    }
                    finally
                    {
                        ExecuteModManagerSort = false;
                    }
                }

                // 排序后读取 ActiveButtons 收集经过 ModManager 排序的 packageId
                if (_modButtonManagerType != null && _activeButtonsGetter != null)
                {
                    var postSortButtons = (IEnumerable)_activeButtonsGetter.Invoke(null, null);
                    if (postSortButtons != null && _btnModProp != null)
                    {
                        foreach (var btn in postSortButtons)
                        {
                            if (btn == null) continue;
                            if (_btnModProp.Invoke(btn, null) is ModMetaData mod && !string.IsNullOrEmpty(mod.PackageId))
                                sortedIds.Add(mod.PackageId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BetterModSort] Failed to write constraints to ModManager: {ex}");
            }
            return sortedIds;
        }
    }
}
