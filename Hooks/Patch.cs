using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Verse;

namespace BetterModSort.Hooks
{
    [HarmonyPatch(typeof(ModsConfig), nameof(ModsConfig.TrySortMods))]
    public static class ModsConfig_TrySortMods_Patch
    {
        public static bool Prefix()
        {
            List<ModMetaData> list = ModsConfig.ActiveModsInLoadOrder.ToList();
            return true;
        }
    }

    /// <summary>
    /// 修复 GetModWarnings 在存在重复 PackageId 时崩溃的问题，并输出详细信息
    /// </summary>
    [HarmonyPatch(typeof(ModsConfig), nameof(ModsConfig.GetModWarnings))]
    public static class ModsConfig_GetModWarnings_Patch
    {
        private static readonly HashSet<string> _reportedDuplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Func<List<string>, Func<ModMetaData, bool>, List<string>> FindConflicts =
            AccessTools.MethodDelegate<Func<List<string>, Func<ModMetaData, bool>, List<string>>>(AccessTools.Method(typeof(ModsConfig), "FindConflicts"));

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
                    sb.AppendLine("ModWithSameIdAlreadyActive".Translate(activeModWithIdentifier.Name));

                List<string> incompatible = FindConflicts(mod.IncompatibleWith, null);
                if (incompatible.Any())
                    sb.AppendLine("ModIncompatibleWithTip".Translate(incompatible.ToCommaList(useAnd: true)));

                // 合并 LoadBefore 与 ForceLoadBefore，统一检测并输出一次
                List<string> beforeConflicts = FindConflicts(MergeLists(mod.LoadBefore, mod.ForceLoadBefore), m => mods.IndexOf(m) < i);
                if (beforeConflicts.Any())
                    sb.AppendLine("ModMustLoadBefore".Translate(beforeConflicts.ToCommaList(useAnd: true)));

                // 合并 LoadAfter 与 ForceLoadAfter，统一检测并输出一次
                List<string> afterConflicts = FindConflicts(MergeLists(mod.LoadAfter, mod.ForceLoadAfter), m => mods.IndexOf(m) > i);
                if (afterConflicts.Any())
                    sb.AppendLine("ModMustLoadAfter".Translate(afterConflicts.ToCommaList(useAnd: true)));

                List<string> unsatisfied = mod.UnsatisfiedDependencies();
                if (unsatisfied.Any())
                    sb.AppendLine("ModUnsatisfiedDependency".Translate(unsatisfied.ToCommaList(useAnd: true)));

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
            sb.AppendLine($"\n[BetterModSort] ========== 检测到重复的 PackageId ==========");
            sb.AppendLine($"[BetterModSort] PackageId: {packageId}");
            sb.AppendLine($"[BetterModSort] 发现 {duplicates.Count} 个 MOD 使用相同的 PackageId:");
            foreach (var m in duplicates)
            {
                sb.AppendLine($"[BetterModSort]   - {m.Name}");
                sb.AppendLine($"[BetterModSort]     路径: {m.RootDir?.FullName ?? "未知"}");
                sb.AppendLine($"[BetterModSort]     来源: {m.Source}");
            }
            sb.AppendLine("[BetterModSort] 建议: 请移除重复的 MOD，只保留一个版本。");
            sb.Append("[BetterModSort] ================================================");
            Log.Warning(sb.ToString());
        }
    }
}
