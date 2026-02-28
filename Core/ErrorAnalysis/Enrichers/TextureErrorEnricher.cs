using System;
using System.Text;
using System.Text.RegularExpressions;
using BetterModSort.Tools;
using Verse;

namespace BetterModSort.Core.ErrorAnalysis.Enrichers
{
    public class TextureErrorEnricher : IErrorEnricher
    {
        private static readonly Regex WorkshopIdInPathRegex = new(@"[\\/]content[\\/]\d+[\\/](\d+)(?:[\\/]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public bool CanEnrich(string errorText)
        {
            return errorText.StartsWith("Exception loading UnityEngine.Texture2D from file.", StringComparison.Ordinal);
        }

        public string? Enrich(string errorText)
        {
            string? absFilePath = ExtractLineValue(errorText, "absFilePath:");
            if (string.IsNullOrEmpty(absFilePath)) return null;

            uint? workshopId = TryExtractWorkshopId(absFilePath!);

            ModContentPack? mod = null;
            if (workshopId.HasValue)
                mod = TryFindModBySteamWorkshopId(workshopId.Value);

            if (absFilePath != null)
                mod ??= TryFindModByPath(absFilePath);

            var extraInfo = new StringBuilder();
            extraInfo.Append("  -> [TextureSource] ");
            if (workshopId.HasValue)
                extraInfo.Append($"[WorkshopId: {workshopId.Value}] ");

            if (mod != null)
            {
                string modName = mod.Name ?? "(unknown)";
                string pkgId = mod.PackageIdPlayerFacing ?? mod.PackageId ?? "";
                extraInfo.Append($"[Mod: {modName} ({pkgId})]");
            }
            else
                extraInfo.Append($"[Mod: {"BMS_Error_ModNotMatched".TranslateSafe()}]");
                
            int insertIdx = errorText.IndexOf(absFilePath, StringComparison.OrdinalIgnoreCase);
            if (insertIdx >= 0)
            {
                int nextNewline = errorText.IndexOf('\n', insertIdx);
                if (nextNewline >= 0)
                    return errorText.Insert(nextNewline + 1, extraInfo.ToString() + "\n");
            }
            return errorText + "\n" + extraInfo.ToString();
        }

        private static string? ExtractLineValue(string text, string key)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key)) return null;

            foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(key.Length).Trim();

            return null;
        }

        private static uint? TryExtractWorkshopId(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = WorkshopIdInPathRegex.Match(path);
            if (!match.Success) return null;
            return uint.TryParse(match.Groups[1].Value, out var id) ? id : null;
        }

        private static ModContentPack? TryFindModBySteamWorkshopId(uint workshopId)
        {
            foreach (var runningMod in LoadedModManager.RunningMods)
                if (runningMod.SteamAppId == workshopId)
                    return runningMod;

            return TryFindModByPathSegment(workshopId.ToString());
        }

        private static ModContentPack? TryFindModByPathSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return null;
            foreach (var mod in LoadedModManager.RunningMods)
            {
                string root = mod.RootDir ?? "";
                if (root.IndexOf(segment, StringComparison.OrdinalIgnoreCase) >= 0)
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
