using System;
using System.Text.RegularExpressions;
using Verse;

namespace BetterModSort.Core.ErrorAnalysis.Enrichers
{
    /// <summary>
    /// 通用文件路径 Enricher：检测错误文本中的 Steam Workshop 路径或 MOD 根目录路径，
    /// 自动识别来源 MOD 并在文本开头注入 [FileSource] 标签。
    /// 作为兜底 enricher 放在列表末尾，优先级低于 XmlErrorEnricher 等专用 enricher。
    /// </summary>
    public class FilePathEnricher : IErrorEnricher
    {
        public int Priority => 200;


        // 匹配 steamapps/workshop/content/{appId}/{workshopId}/ 中的 workshopId
        private static readonly Regex WorkshopPathRegex = new(
            @"steamapps[\\/]workshop[\\/]content[\\/]\d+[\\/](\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 匹配 Windows 绝对路径（盘符:\...）
        private static readonly Regex AbsolutePathRegex = new(
            @"[A-Za-z]:[\\/][^\s""<>|*?]+",
            RegexOptions.Compiled);

        public bool CanEnrich(string errorText)
        {
            return WorkshopPathRegex.IsMatch(errorText) || AbsolutePathRegex.IsMatch(errorText);
        }

        public string? Enrich(string errorText)
        {
            // 优先通过 Workshop ID 匹配
            var workshopMatch = WorkshopPathRegex.Match(errorText);
            if (workshopMatch.Success && uint.TryParse(workshopMatch.Groups[1].Value, out uint workshopId))
            {
                var mod = TryFindModBySteamWorkshopId(workshopId);
                if (mod != null)
                {
                    string modName = mod.Name ?? "(unknown)";
                    string pkgId = mod.PackageIdPlayerFacing ?? mod.PackageId ?? "";
                    return $"[FileSource] [Mod: {modName} ({pkgId})]\n{errorText}";
                }
            }

            // 通过绝对路径前缀匹配 MOD 根目录
            var pathMatch = AbsolutePathRegex.Match(errorText);
            if (pathMatch.Success)
            {
                string filePath = pathMatch.Value;
                var mod = TryFindModByPath(filePath);
                if (mod != null)
                {
                    string modName = mod.Name ?? "(unknown)";
                    string pkgId = mod.PackageIdPlayerFacing ?? mod.PackageId ?? "";
                    return $"[FileSource] [Mod: {modName} ({pkgId})]\n{errorText}";
                }
            }

            return null;
        }

        private static ModContentPack? TryFindModBySteamWorkshopId(uint workshopId)
        {
            foreach (var mod in LoadedModManager.RunningMods)
                if (mod.SteamAppId == workshopId)
                    return mod;

            // 回退：路径片段匹配
            string idStr = workshopId.ToString();
            foreach (var mod in LoadedModManager.RunningMods)
            {
                string root = mod.RootDir ?? "";
                if (root.IndexOf(idStr, StringComparison.OrdinalIgnoreCase) >= 0)
                    return mod;
            }
            return null;
        }

        private static ModContentPack? TryFindModByPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            foreach (var mod in LoadedModManager.RunningMods)
            {
                string root = mod.RootDir ?? "";
                if (string.IsNullOrEmpty(root)) continue;
                if (filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return mod;
            }
            return null;
        }
    }
}
