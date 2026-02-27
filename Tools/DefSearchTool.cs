using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace BetterModSort.Tools
{
    /// <summary>
    /// Defs 搜索工具 - 允许搜索包含特定 Defs 的 MOD 信息
    /// </summary>
    public static class DefSearchTool
    {
        /// <summary>
        /// 搜索包含指定 DefName 的 MOD
        /// </summary>
        public static List<ModDefInfo> SearchModsByDefName(string defName)
        {
            var results = new List<ModDefInfo>();

            foreach (var mod in ModLister.AllInstalledMods)
            {
                var defs = FindDefsInMod(mod, defName);
                if (defs.Any())
                {
                    results.Add(new ModDefInfo
                    {
                        ModMetaData = mod,
                        PackageId = mod.PackageId,
                        ModName = mod.Name,
                        MatchedDefs = defs
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// 搜索指定类型的 Defs
        /// </summary>
        public static List<ModDefInfo> SearchModsByDefType(Type defType)
        {
            var results = new List<ModDefInfo>();

            var allDefs = GenDefDatabase.GetAllDefsInDatabaseForDef(defType);
            var modDefMap = new Dictionary<string, List<string>>();

            foreach (var def in allDefs)
            {
                var packageId = def.modContentPack?.PackageId ?? "Unknown";
                if (!modDefMap.ContainsKey(packageId))
                {
                    modDefMap[packageId] = new List<string>();
                }
                modDefMap[packageId].Add(def.defName);
            }

            foreach (var kvp in modDefMap)
            {
                var mod = ModLister.GetModWithIdentifier(kvp.Key);
                if (mod != null)
                {
                    results.Add(new ModDefInfo
                    {
                        ModMetaData = mod,
                        PackageId = kvp.Key,
                        ModName = mod.Name,
                        MatchedDefs = kvp.Value
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// 获取 MOD 的所有 Def 信息
        /// </summary>
        public static List<string> GetAllDefsFromMod(ModMetaData mod)
        {
            var defs = new List<string>();

            foreach (var def in DefDatabase<Def>.AllDefs)
            {
                if (def.modContentPack?.PackageId == mod.PackageId)
                {
                    defs.Add($"{def.GetType().Name}:{def.defName}");
                }
            }

            return defs;
        }

        /// <summary>
        /// 搜索包含关键词的 Defs
        /// </summary>
        public static List<ModDefInfo> SearchModsByKeyword(string keyword)
        {
            var results = new List<ModDefInfo>();
            var modDefMap = new Dictionary<string, List<string>>();

            foreach (var def in DefDatabase<Def>.AllDefs)
            {
                if (def.defName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (def.label != null && def.label.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var packageId = def.modContentPack?.PackageId ?? "Unknown";
                    if (!modDefMap.ContainsKey(packageId))
                    {
                        modDefMap[packageId] = new List<string>();
                    }
                    modDefMap[packageId].Add($"{def.GetType().Name}:{def.defName}");
                }
            }

            foreach (var kvp in modDefMap)
            {
                var mod = ModLister.GetModWithIdentifier(kvp.Key);
                results.Add(new ModDefInfo
                {
                    ModMetaData = mod,
                    PackageId = kvp.Key,
                    ModName = mod?.Name ?? kvp.Key,
                    MatchedDefs = kvp.Value
                });
            }

            return results;
        }

        private static List<string> FindDefsInMod(ModMetaData mod, string defName)
        {
            var defs = new List<string>();

            foreach (var def in DefDatabase<Def>.AllDefs)
            {
                if (def.modContentPack?.PackageId == mod.PackageId &&
                    def.defName.IndexOf(defName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    defs.Add($"{def.GetType().Name}:{def.defName}");
                }
            }

            return defs;
        }
    }

    public class ModDefInfo
    {
        public ModMetaData? ModMetaData { get; set; }
        public string PackageId { get; set; } = "";
        public string ModName { get; set; } = "";
        public List<string> MatchedDefs { get; set; } = new List<string>();

        public override string ToString()
        {
            return $"[{PackageId}] {ModName} - Defs: {string.Join(", ", MatchedDefs)}";
        }
    }
}
