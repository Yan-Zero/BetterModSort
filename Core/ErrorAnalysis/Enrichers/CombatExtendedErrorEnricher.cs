using System.Collections.Generic;
using System.Text.RegularExpressions;
using BetterModSort.Tools;
using Verse;

namespace BetterModSort.Core.ErrorAnalysis.Enrichers
{
    public class CombatExtendedErrorEnricher : IErrorEnricher
    {
        public int Priority => 80;

        private static readonly Regex CeStatRegex = new(
            @"from\s+(.*?)\s+which\s+has\s+no\s+support\s+for\s+Combat\s+Extended\.",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public bool CanEnrich(string errorText)
        {
            return errorText.StartsWith("Trying to get stat ") &&
                   errorText.EndsWith(" which has no support for Combat Extended.");
        }

        public IEnrichmentData? Collect(string errorText)
        {
            var match = CeStatRegex.Match(errorText);
            if (!match.Success) return null;

            string defName = match.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(defName)) return null;

            var asset = DefSourceMap.GetAssetByDefName(defName);
            if (asset?.mod == null) return null;

            return new CEEnrichmentData(defName, asset.mod, errorText);
        }
    }

    public class CEEnrichmentData(string targetDefName, ModContentPack sourceMod, string originalErrorText) : IEnrichmentData
    {
        public string TargetDefName { get; } = targetDefName;
        public ModContentPack SourceMod { get; } = sourceMod;
        public string OriginalErrorText { get; } = originalErrorText;

        public IEnumerable<ModContentPack> GetInvolvedMods() => [SourceMod];

        public string FormatForConsole()
        {
            string modName = SourceMod.Name ?? "(unknown mod)";
            string pkgId = SourceMod.PackageIdPlayerFacing ?? SourceMod.PackageId ?? "(unknown packageId)";
            string translatedReason = "BMS_Error_CENotSupported".TranslateSafe(TargetDefName, modName, pkgId);
            return $"{OriginalErrorText}\n  -> {translatedReason}";
        }

        public string FormatForFile()
        {
            string modName = SourceMod.Name ?? "(unknown)";
            string pkgId = SourceMod.PackageIdPlayerFacing ?? SourceMod.PackageId ?? "";
            return $"[Combat Extended Incompatibility] {TargetDefName} -> Mod:{modName} ({pkgId})";
        }
    }
}
