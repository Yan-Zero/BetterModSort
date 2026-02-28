using BetterModSort.Hooks;
using BetterModSort.Tools;

namespace BetterModSort.Core.ErrorAnalysis.Enrichers
{
    public class XmlErrorEnricher : IErrorEnricher
    {
        public int Priority => 50;

        public bool CanEnrich(string errorText)
        {
            return errorText.StartsWith("XML error: ");
        }

        public string? Enrich(string errorText)
        {
            var asset = XmlSourceTracker.Peek();
            if (asset == null) return errorText;
            var mod = asset.mod;
            string modName = mod?.Name ?? "(unknown mod)";
            string pkgId = mod?.PackageIdPlayerFacing ?? mod?.PackageId ?? "(unknown packageId)";
            string filePath = asset.FullFilePath ?? asset.name ?? "(unknown file)";
            string defRoot = XmlSourceTracker.CurrentDefRoot?.Name ?? "(unknown root)";
            return $"[XmlSource] Mod={modName} ({pkgId}) File={filePath} Root={defRoot}\n{errorText}";
        }
    }
}
