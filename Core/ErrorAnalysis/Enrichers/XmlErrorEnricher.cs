using System.Collections.Generic;
using BetterModSort.Hooks;
using BetterModSort.Tools;
using Verse;

namespace BetterModSort.Core.ErrorAnalysis.Enrichers
{
    public class XmlErrorEnricher : IErrorEnricher
    {
        public int Priority => 50;

        public bool CanEnrich(string errorText)
        {
            return errorText.StartsWith("XML error: ");
        }

        public IEnrichmentData? Collect(string errorText)
        {
            var asset = XmlSourceTracker.Peek();
            if (asset == null) return null;

            return new XmlEnrichmentData(
                asset.mod,
                asset.FullFilePath ?? asset.name ?? "",
                XmlSourceTracker.CurrentDefRoot?.Name ?? "",
                errorText
            );
        }
    }

    public class XmlEnrichmentData(
        ModContentPack? mod,
        string filePath,
        string rootNode,
        string originalErrorText) : IEnrichmentData
    {
        public ModContentPack? Mod { get; } = mod;
        public string FilePath { get; } = filePath;
        public string RootNode { get; } = rootNode;
        public string OriginalErrorText { get; } = originalErrorText;

        public IEnumerable<ModContentPack> GetInvolvedMods()
        {
            if (Mod != null) yield return Mod;
        }

        public string FormatForConsole()
        {
            string modName = Mod?.Name ?? "(unknown mod)";
            string pkgId = Mod?.PackageIdPlayerFacing ?? Mod?.PackageId ?? "(unknown packageId)";
            return $"[XmlSource] Mod={modName} ({pkgId}) File={FilePath} Root={RootNode}\n{OriginalErrorText}";
        }

        public string FormatForFile()
        {
            string modName = Mod?.Name ?? "(unknown)";
            string pkgId = Mod?.PackageIdPlayerFacing ?? Mod?.PackageId ?? "";
            string summary = OriginalErrorText;
            int ctxIdx = summary.IndexOf(" Context: ");
            if (ctxIdx > 0)
                summary = summary[..ctxIdx];
            if (summary.Length > 100)
                summary = summary[..100] + "...";
            return $"[XML Parse Error] Mod:{modName} ({pkgId}) | File:{FilePath} | Root:{RootNode}\n{summary}";
        }
    }
}
