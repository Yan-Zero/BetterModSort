using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Verse;
using BetterModSort.Tools;

namespace BetterModSort.Hooks
{
    [HarmonyPatch(typeof(ModsConfig), nameof(ModsConfig.TrySortMods))]
    public static class ModsConfig_TrySortMods_Patch
    {
        public static bool ExecuteVanillaSort = false;

        public static bool Prefix()
        {
            if (ExecuteVanillaSort)
                return true;

            // AI 排序是实验性功能，需要用户在 MOD 设置中手动启用
            if (!BetterModSortMod.Settings.EnableAISorting)
                return true;

            Find.WindowStack.Add(new AI.Dialog_AILoading());
            return false;
        }
    }

    /// <summary>
    /// 修复 GetModWarnings 在存在重复 PackageId 时崩溃的问题，并输出详细信息
    /// </summary>
    [HarmonyPatch(typeof(ModsConfig), nameof(ModsConfig.GetModWarnings))]
    public static class ModsConfig_GetModWarnings_Patch
    {
        private static readonly HashSet<string> _reportedDuplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Func<List<string>, Func<ModMetaData, bool>?, List<string>> FindConflicts =
            AccessTools.MethodDelegate<Func<List<string>, Func<ModMetaData, bool>?, List<string>>>(AccessTools.Method(typeof(ModsConfig), "FindConflicts"));

        public static bool Prefix(ref Dictionary<string, string> __result)
        {
            var dictionary = new Dictionary<string, string>();
            List<ModMetaData> mods = ModsConfig.ActiveModsInLoadOrder.ToList();

            for (int i = 0; i < mods.Count; i++)
            {
                ModMetaData mod = mods[i];

                // 检查是否已有相同 PackageId（重复 MOD 检测）
                if (dictionary.ContainsKey(mod.PackageId))
                {
                    OutputDuplicateModWarning(mod.PackageId, mods);
                    continue;
                }

                var sb = new StringBuilder();

                ModMetaData activeModWithIdentifier = ModLister.GetActiveModWithIdentifier(mod.PackageId);
                if (activeModWithIdentifier != null && mod != activeModWithIdentifier)
                    sb.AppendLine("ModWithSameIdAlreadyActive".TranslateSafe(activeModWithIdentifier.Name));

                List<string> incompatible = FindConflicts(mod.IncompatibleWith, null);
                if (incompatible.Any())
                    sb.AppendLine("ModIncompatibleWithTip".TranslateSafe(incompatible.ToCommaList(useAnd: true)));

                // 合并 LoadBefore 与 ForceLoadBefore，统一检测并输出一次
                List<string> beforeConflicts = FindConflicts(MergeLists(mod.LoadBefore, mod.ForceLoadBefore), m => mods.IndexOf(m) < i);
                if (beforeConflicts.Any())
                    sb.AppendLine("ModMustLoadBefore".TranslateSafe(beforeConflicts.ToCommaList(useAnd: true)));

                // 合并 LoadAfter 与 ForceLoadAfter，统一检测并输出一次
                List<string> afterConflicts = FindConflicts(MergeLists(mod.LoadAfter, mod.ForceLoadAfter), m => mods.IndexOf(m) > i);
                if (afterConflicts.Any())
                    sb.AppendLine("ModMustLoadAfter".TranslateSafe(afterConflicts.ToCommaList(useAnd: true)));

                List<string> unsatisfied = mod.UnsatisfiedDependencies();
                if (unsatisfied.Any())
                    sb.AppendLine("ModUnsatisfiedDependency".TranslateSafe(unsatisfied.ToCommaList(useAnd: true)));

                dictionary.Add(mod.PackageId, sb.ToString().TrimEndNewlines());
            }

            __result = dictionary;
            return false;
        }

        private static List<string> MergeLists(List<string> a, List<string> b)
        {
            if (a == null || a.Count == 0) return b ?? new List<string>();
            if (b == null || b.Count == 0) return a;
            return a.Concat(b).ToList();
        }

        private static void OutputDuplicateModWarning(string packageId, List<ModMetaData> allMods)
        {
            if (!_reportedDuplicates.Add(packageId)) return;

            var duplicates = allMods.Where(m => m.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"\n[BetterModSort] {"BMS_Patch_DuplicateHeader".TranslateSafe()}");
            sb.AppendLine($"[BetterModSort] PackageId: {packageId}");
            sb.AppendLine($"[BetterModSort] {"BMS_Patch_DuplicateFound".TranslateSafe(duplicates.Count)}");
            foreach (var m in duplicates)
            {
                sb.AppendLine($"[BetterModSort]   - {m.Name}");
                sb.AppendLine($"[BetterModSort]     {"BMS_Patch_Path".TranslateSafe()}: {m.RootDir?.FullName ?? "BMS_Patch_Unknown".TranslateSafe()}");
                sb.AppendLine($"[BetterModSort]     {"BMS_Patch_Source".TranslateSafe()}: {m.Source}");
            }
            sb.AppendLine($"[BetterModSort] {"BMS_Patch_DuplicateAdvice".TranslateSafe()}");
            sb.Append($"[BetterModSort] {"BMS_Patch_DuplicateFooter".TranslateSafe()}");
            Log.Warning(sb.ToString());
        }
    }
}
