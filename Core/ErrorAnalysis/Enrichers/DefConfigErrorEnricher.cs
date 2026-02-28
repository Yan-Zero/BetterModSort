using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BetterModSort.Tools;
using Verse;

namespace BetterModSort.Core.ErrorAnalysis.Enrichers
{
    public class DefConfigErrorEnricher : IErrorEnricher
    {
        public int Priority => 50;

        public bool CanEnrich(string errorText)
        {
            return errorText.StartsWith("Config error in ") || errorText.StartsWith("Exception in ConfigErrors() of ");
        }

        public IEnrichmentData? Collect(string errorText)
        {
            var declaringType = FindDefDatabaseDeclaringType(new StackTrace(false));
            if (declaringType == null || !declaringType.IsGenericType) return null;

            int startIndex = errorText.StartsWith("Config error in ") ? 16 : 31;
            int endIndex = errorText.IndexOf(':', startIndex);
            if (endIndex <= startIndex) return null;

            string possibleDefName = errorText[startIndex..endIndex];
            if (string.IsNullOrEmpty(possibleDefName)) return null;

            try
            {
                var defType = declaringType.GetGenericArguments()[0];
                string defTypeName = defType.Name;

                var sourceAsset = DefSourceMap.GetAsset(defTypeName, possibleDefName)
                    ?? DefSourceMap.GetAssetByDefName(possibleDefName);

                var parentChain = BuildParentChain(defTypeName, possibleDefName);

                var allDefNames = new List<string> { possibleDefName };
                allDefNames.AddRange(parentChain.Select(p => p.parentName));

                var allPatches = allDefNames
                    .SelectMany(dn => PatchSourceMap.Get(dn).Select(p => (defName: dn, patch: p)))
                    .GroupBy(x => (x.patch.Mod?.PackageId ?? "", x.patch.Operation?.GetType().Name ?? "", x.defName))
                    .Select(g => g.First())
                    .ToList();

                if (sourceAsset == null && parentChain.Count == 0 && allPatches.Count == 0)
                    return null;

                return new DefConfigEnrichmentData(
                    possibleDefName, sourceAsset, parentChain, allPatches, errorText);
            }
            catch { return null; }
        }

        private static Type? FindDefDatabaseDeclaringType(StackTrace stackTrace)
        {
            foreach (var frame in stackTrace.GetFrames() ?? [])
            {
                var method = frame.GetMethod();
                if (method?.DeclaringType != null
                    && method.Name == "ErrorCheckAllDefs"
                    && method.DeclaringType.FullName != null
                    && method.DeclaringType.FullName.StartsWith("Verse.DefDatabase"))
                    return method.DeclaringType;
            }
            return null;
        }

        internal static List<(string parentName, LoadableXmlAsset? asset, int depth)> BuildParentChain(
            string defTypeName, string defName, int maxDepth = 10)
        {
            var result = new List<(string, LoadableXmlAsset?, int)>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { defName };

            string? currentName = defName;
            int depth = 0;

            while (depth < maxDepth)
            {
                string? parentName = DefSourceMap.GetParentName(defTypeName, currentName!);
                parentName ??= DefSourceMap.GetParentNameByDefName(currentName!);

                if (string.IsNullOrEmpty(parentName) || !visited.Add(parentName!))
                    break;

                var parentAsset = DefSourceMap.GetAsset(defTypeName, parentName!)
                    ?? DefSourceMap.GetAssetByDefName(parentName!);

                result.Add((parentName!, parentAsset, depth));
                currentName = parentName;
                depth++;
            }

            return result;
        }
    }

    public class DefConfigEnrichmentData : IEnrichmentData
    {
        public string DefName { get; }
        public LoadableXmlAsset? SourceAsset { get; }
        public List<(string parentName, LoadableXmlAsset? asset, int depth)> ParentChain { get; }
        public List<(string defName, PatchInfo patch)> Patches { get; }

        public string OriginalErrorText { get; }

        public DefConfigEnrichmentData(
            string defName,
            LoadableXmlAsset? sourceAsset,
            List<(string parentName, LoadableXmlAsset? asset, int depth)> parentChain,
            List<(string defName, PatchInfo patch)> patches,
            string originalErrorText)
        {
            DefName = defName;
            SourceAsset = sourceAsset;
            ParentChain = parentChain;
            Patches = patches;
            OriginalErrorText = originalErrorText;
        }

        public IEnumerable<ModContentPack> GetInvolvedMods()
        {
            if (SourceAsset?.mod != null)
                yield return SourceAsset.mod;
            foreach (var (_, asset, _) in ParentChain)
                if (asset?.mod != null)
                    yield return asset.mod;
            foreach (var (_, patch) in Patches)
                if (patch.Mod != null)
                    yield return patch.Mod;
        }

        public string FormatForConsole()
        {
            var sb = new StringBuilder();
            sb.AppendLine(OriginalErrorText);

            if (SourceAsset != null)
            {
                var mod = SourceAsset.mod;
                string modName = mod?.Name ?? "BMS_Error_UnknownOrVanilla".TranslateSafe();
                string pkgId = mod?.PackageIdPlayerFacing ?? mod?.PackageId ?? "";
                string file = SourceAsset.FullFilePath ?? SourceAsset.name ?? "";
                string sourceLabel = "BMS_Error_SourceMod".TranslateSafe();
                string fileLabel = "BMS_Error_File".TranslateSafe();
                sb.AppendLine($"  -> [{sourceLabel}: {modName} ({pkgId})]");
                if (!string.IsNullOrEmpty(file))
                    sb.AppendLine($"     [{fileLabel}: {file}]");
            }

            if (ParentChain.Count > 0)
            {
                string chainLabel = "BMS_Error_InheritanceChain".TranslateSafe();
                sb.AppendLine($"  -> [{chainLabel}]");
                foreach (var (parentName, parentAsset, depth) in ParentChain)
                {
                    string indent = new(' ', 5 + depth * 2);
                    if (parentAsset != null)
                    {
                        var pMod = parentAsset.mod;
                        string pModName = pMod?.Name ?? "(unknown)";
                        string pPkgId = pMod?.PackageIdPlayerFacing ?? pMod?.PackageId ?? "";
                        string pFile = parentAsset.FullFilePath ?? parentAsset.name ?? "";
                        string pFileLabel = "BMS_Error_File".TranslateSafe();
                        sb.AppendLine($"{indent}- ParentName=\"{parentName}\" [Mod: {pModName} ({pPkgId})]");
                        if (!string.IsNullOrEmpty(pFile))
                            sb.AppendLine($"{indent}  {pFileLabel}: {pFile}");
                    }
                    else
                    {
                        string notFoundLabel = "BMS_Error_SourceNotFound".TranslateSafe();
                        sb.AppendLine($"{indent}- ParentName=\"{parentName}\" [{notFoundLabel}]");
                    }
                }
            }

            if (Patches.Count > 0)
            {
                string patchLabel = "BMS_Error_PatchInvolving".TranslateSafe();
                sb.AppendLine($"  -> [{patchLabel}: {DefName}]");
                foreach (var (targetDefName, patchInfo) in Patches)
                {
                    var patchMod = patchInfo.Mod;
                    string patchModName = patchMod?.Name ?? "(unknown)";
                    string patchPkgId = patchMod?.PackageIdPlayerFacing ?? patchMod?.PackageId ?? "";
                    string opType = patchInfo.Operation?.GetType().Name ?? "PatchOperation";
                    string findModInfo = !string.IsNullOrEmpty(patchInfo.FindModTarget)
                        ? $" (FindMod: {patchInfo.FindModTarget})"
                        : "";
                    string targetInfo = targetDefName != DefName
                        ? $" (-> parent: {targetDefName})"
                        : "";
                    sb.AppendLine($"     - [Mod: {patchModName} ({patchPkgId})] {opType}{findModInfo}{targetInfo}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        public string FormatForFile()
        {
            var sb = new StringBuilder();

            string summary = OriginalErrorText.Length > 100
                ? OriginalErrorText[..100] + "..."
                : OriginalErrorText;
            sb.AppendLine($"[Def Config Error] {summary}");

            // 来源 MOD
            if (SourceAsset?.mod != null)
            {
                string modName = SourceAsset.mod.Name ?? "(unknown)";
                string pkgId = SourceAsset.mod.PackageIdPlayerFacing ?? SourceAsset.mod.PackageId ?? "";
                string file = SourceAsset.FullFilePath ?? SourceAsset.name ?? "";
                sb.AppendLine($"  Source: {modName} ({pkgId}) | {file}");
            }

            // 继承链（精简为一行）
            if (ParentChain.Count > 0)
            {
                var parents = ParentChain.Select(p =>
                {
                    string pMod = p.asset?.mod?.Name ?? "?";
                    return $"{p.parentName}[{pMod}]";
                });
                sb.AppendLine($"  Parents: {string.Join(" -> ", parents)}");
            }

            // Patch（精简为每行一个）
            foreach (var (targetDefName, patchInfo) in Patches)
            {
                string pMod = patchInfo.Mod?.Name ?? "?";
                string pPkg = patchInfo.Mod?.PackageIdPlayerFacing ?? patchInfo.Mod?.PackageId ?? "";
                string opType = patchInfo.Operation?.GetType().Name ?? "PatchOp";
                sb.AppendLine($"  Patch: {pMod} ({pPkg}) {opType}");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
