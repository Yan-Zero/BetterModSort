using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using BetterModSort.AI;
using BetterModSort.Tools;

namespace BetterModSort.Hooks
{
    /// <summary>
    /// 为 Prestarter.ModManager 提供与原版、ModManager 类似的 AI 排序拦截支持。
    /// 通过反射动态检测并挂载 Harmony Patch，不强依赖 Prestarter 的存在。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class PrestarterPatch
    {
        /// <summary>当为 true 时，允许 Prestarter.ModManager.TrySortMods 正常执行（放行）。</summary>
        public static bool ExecutePrestarterSort = false;

        private static Type? _prestarterModManagerType;
        private static MethodInfo? _sortMethod;
        private static FieldInfo? _activeField;

        static PrestarterPatch()
        {
            var inst = BetterModSortMod.HarmonyInstance;
            if (inst != null)
                PatchIfNeeded(inst);
        }

        public static void PatchIfNeeded(Harmony harmony)
        {
            _prestarterModManagerType = AccessTools.TypeByName("Prestarter.ModManager");
            if (_prestarterModManagerType == null)
                return;

            _sortMethod = AccessTools.Method(_prestarterModManagerType, "TrySortMods");
            if (_sortMethod == null)
                return;

            // 尝试获取 internal UniqueList<string> active 字段
            _activeField = AccessTools.Field(_prestarterModManagerType, "active");

            harmony.Patch(_sortMethod, prefix: new HarmonyMethod(typeof(PrestarterPatch), nameof(TrySortMods_Prefix)));
            Log.Message("[BetterModSort] Successfully hooked Prestarter.ModManager for AI sorting support.");
        }

        public static bool TrySortMods_Prefix()
        {
            if (ExecutePrestarterSort)
                return true;

            if (!BetterModSortMod.Settings.EnableAISorting)
                return true;

            Find.WindowStack.Add(new Dialog_AILoading(SortInvoker.Prestarter));
            return false;
        }

        /// <summary>
        /// 在 AI 完成约束注入后，调用此方法执行 Prestarter 的排序，并返回排序后的 packageId 列表。
        /// </summary>
        public static List<string> ApplyConstraintsToPrestarterAndSort(List<SoftConstraintInfo> constraints)
        {
            var sortedIds = new List<string>();
            try
            {
                if (_sortMethod == null)
                    return sortedIds;

                // Prestarter 是静态 ModManager，使用和 Vanilla 相同的 ModMetaData 依赖注入方式
                // (Dialog_AILoading 已经向 ModMetaData.ForceLoadBefore/After 注入了约束)
                // 直接放行执行原版排序
                ExecutePrestarterSort = true;
                try
                {
                    _sortMethod.Invoke(null, null);
                }
                finally
                {
                    ExecutePrestarterSort = false;
                }

                // 读取排序后的 active 字段
                if (_activeField != null)
                {
                    var activeObj = _activeField.GetValue(null);
                    if (activeObj is IEnumerable<string> activeList)
                        foreach (var id in activeList)
                            if (!string.IsNullOrEmpty(id))
                                sortedIds.Add(id);
                }
                else
                {
                    // 回退：如果找不到 active 字段，从 ModsConfig 读（不一定准确，但总比崩溃强）
                    foreach (var mod in ModsConfig.ActiveModsInLoadOrder)
                        sortedIds.Add(mod.PackageId);
                    Log.Warning("[BetterModSort] Prestarter: Could not find 'active' field, falling back to ModsConfig for RimSort export.");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[BetterModSort] Failed to apply constraints to Prestarter: {ex}");
            }
            return sortedIds;
        }

        /// <summary>
        /// 使用 Verse.ModLister.GetModWithIdentifier 获取 ModMetaData。
        /// （由 Dialog_AILoading 在 Prestarter 路径下注入约束时使用相同的 ModMetaData API）
        /// </summary>
        public static ModMetaData? GetModWithIdentifier(string modId)
            => ModLister.GetModWithIdentifier(modId);
    }
}
