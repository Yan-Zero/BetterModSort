using System.Text.RegularExpressions;
using BetterModSort.Tools;

namespace BetterModSort.Core.ErrorAnalysis.Enrichers
{
    public class CombatExtendedErrorEnricher : IErrorEnricher
    {
        public int Priority => 80;

        // 匹配 "Trying to get stat XXX from YYY which has no support for Combat Extended." 中的 YYY
        private static readonly Regex CeStatRegex = new(
            @"from\s+(.*?)\s+which\s+has\s+no\s+support\s+for\s+Combat\s+Extended\.",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public bool CanEnrich(string errorText)
        {
            return errorText.StartsWith("Trying to get stat ") && 
                   errorText.EndsWith(" which has no support for Combat Extended.");
        }

        public string? Enrich(string errorText)
        {
            var match = CeStatRegex.Match(errorText);
            if (!match.Success) return errorText;

            string defName = match.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(defName)) return errorText;

            var asset = DefSourceMap.GetAssetByDefName(defName);
            if (asset != null)
            {
                var mod = asset.mod;
                string modName = mod?.Name ?? "(unknown mod)";
                string pkgId = mod?.PackageIdPlayerFacing ?? mod?.PackageId ?? "(unknown packageId)";
                
                string translatedReason = "BMS_Error_CENotSupported".TranslateSafe(defName, modName, pkgId);
                return $"{errorText}\n  -> {translatedReason}";
            }

            return errorText;
        }
    }
}
