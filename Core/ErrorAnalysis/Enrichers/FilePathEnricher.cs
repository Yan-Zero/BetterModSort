using System.Collections.Generic;
using System.Text.RegularExpressions;
using Verse;

namespace BetterModSort.Core.ErrorAnalysis.Enrichers
{
    /// <summary>
    /// 通用文件路径 Enricher：检测错误文本中的 Steam Workshop 路径或 MOD 根目录路径，
    /// 自动识别来源 MOD。作为兜底 enricher 放在列表末尾。
    /// </summary>
    public class FilePathEnricher : IErrorEnricher
    {
        public int Priority => 200;

        private static readonly Regex WorkshopPathRegex = new(
            @"steamapps[\\/]workshop[\\/]content[\\/]\d+[\\/](\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AbsolutePathRegex = new(
            @"[A-Za-z]:[\\/][^\s""<>|*?]+",
            RegexOptions.Compiled);

        public bool CanEnrich(string errorText)
        {
            return WorkshopPathRegex.IsMatch(errorText) || AbsolutePathRegex.IsMatch(errorText);
        }

        public IEnrichmentData? Collect(string errorText)
        {
            // 优先通过 Workshop ID 匹配
            var workshopMatch = WorkshopPathRegex.Match(errorText);
            if (workshopMatch.Success && uint.TryParse(workshopMatch.Groups[1].Value, out uint workshopId))
            {
                var mod = TryFindModBySteamWorkshopId(workshopId);
                if (mod != null)
                    return new FilePathEnrichmentData(mod, workshopMatch.Value, errorText);
            }

            // 通过绝对路径前缀匹配 MOD 根目录
            var pathMatch = AbsolutePathRegex.Match(errorText);
            if (pathMatch.Success)
            {
                var mod = TryFindModByPath(pathMatch.Value);
                if (mod != null)
                    return new FilePathEnrichmentData(mod, pathMatch.Value, errorText);
            }

            return null;
        }

        private static ModContentPack? TryFindModBySteamWorkshopId(uint workshopId)
        {
            foreach (var mod in LoadedModManager.RunningMods)
                if (mod.SteamAppId == workshopId)
                    return mod;

            string idStr = workshopId.ToString();
            foreach (var mod in LoadedModManager.RunningMods)
            {
                string root = mod.RootDir ?? "";
                if (root.IndexOf(idStr, System.StringComparison.OrdinalIgnoreCase) >= 0)
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
                if (filePath.StartsWith(root, System.StringComparison.OrdinalIgnoreCase))
                    return mod;
            }
            return null;
        }
    }

    public class FilePathEnrichmentData(ModContentPack mod, string matchedPath, string originalErrorText) : IEnrichmentData
    {
        public ModContentPack Mod { get; } = mod;
        public string MatchedPath { get; } = matchedPath;
        public string OriginalErrorText { get; } = originalErrorText;

        public IEnumerable<ModContentPack> GetInvolvedMods() => [Mod];

        public string FormatForConsole()
        {
            string modName = Mod.Name ?? "(unknown)";
            string pkgId = Mod.PackageIdPlayerFacing ?? Mod.PackageId ?? "";
            return $"[FileSource] [Mod: {modName} ({pkgId})]\n{OriginalErrorText}";
        }

        public string FormatForFile()
        {
            string modName = Mod.Name ?? "(unknown)";
            string pkgId = Mod.PackageIdPlayerFacing ?? Mod.PackageId ?? "";
            string truncated = OriginalErrorText.Length > 200
                ? OriginalErrorText[..200] + "..."
                : OriginalErrorText;
            return $"[File Path Error] Mod:{modName} ({pkgId})\n{truncated}";
        }
    }
}
