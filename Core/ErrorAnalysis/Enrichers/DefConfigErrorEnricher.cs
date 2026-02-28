using System;
using System.Diagnostics;
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

        public string? Enrich(string errorText)
        {
            var declaringType = FindDefDatabaseDeclaringType(new StackTrace(false));
            if (declaringType == null || !declaringType.IsGenericType) return errorText;

            // "Config error in " = 16 chars, "Exception in ConfigErrors() of " = 31 chars
            int startIndex = errorText.StartsWith("Config error in ") ? 16 : 31;
            int endIndex = errorText.IndexOf(':', startIndex);
            if (endIndex <= startIndex) return errorText;

            string possibleDefName = errorText[startIndex..endIndex];
            if (string.IsNullOrEmpty(possibleDefName)) return errorText;

            var sb = new StringBuilder();
            sb.AppendLine(errorText);
            bool hasAnyInfo = false;

            try
            {
                var defType = declaringType.GetGenericArguments()[0];
                string defTypeName = defType.Name;
                var sourceAsset = DefSourceMap.GetAsset(defTypeName, possibleDefName)
                    ?? DefSourceMap.GetAssetByDefName(possibleDefName);
                if (sourceAsset != null)
                {
                    hasAnyInfo = true;
                    var mod = sourceAsset.mod;
                    string modName = mod?.Name ?? "BMS_Error_UnknownOrVanilla".TranslateSafe();
                    string pkgId = mod?.PackageIdPlayerFacing ?? mod?.PackageId ?? "";
                    string file = sourceAsset.FullFilePath ?? sourceAsset.name ?? "";
                    string sourceLabel = "BMS_Error_SourceMod".TranslateSafe();
                    string fileLabel2 = "BMS_Error_File".TranslateSafe();
                    sb.AppendLine($"  -> [{sourceLabel}: {modName} ({pkgId})]");
                    if (!string.IsNullOrEmpty(file))
                        sb.AppendLine($"     [{fileLabel2}: {file}]");
                }

                // 2) 遍历 ParentName 继承链，追踪可能导致错误的父 Def
                var parentChain = BuildParentChain(defTypeName, possibleDefName);
                if (parentChain.Count > 0)
                {
                    hasAnyInfo = true;
                    string chainLabel = "BMS_Error_InheritanceChain".TranslateSafe();
                    sb.AppendLine($"  -> [{chainLabel}]");
                    foreach (var (parentName, parentAsset, depth) in parentChain)
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

                // 4) 检查 PatchSourceMap 中是否有 Patch 修改了该 Def 或其父 Def
                var allDefNames = new List<string> { possibleDefName };
                allDefNames.AddRange(parentChain.Select(p => p.parentName));

                var allPatches = allDefNames
                    .SelectMany(dn => PatchSourceMap.Get(dn).Select(p => (defName: dn, patch: p)))
                    .GroupBy(x => (x.patch.Mod?.PackageId ?? "", x.patch.Operation?.GetType().Name ?? "", x.defName))
                    .Select(g => g.First())
                    .ToList();

                if (allPatches.Count > 0)
                {
                    hasAnyInfo = true;
                    string patchLabel = "BMS_Error_PatchInvolving".TranslateSafe();
                    sb.AppendLine($"  -> [{patchLabel}: {possibleDefName}]");
                    foreach (var (targetDefName, patchInfo) in allPatches)
                    {
                        var patchMod = patchInfo.Mod;
                        string patchModName = patchMod?.Name ?? "(unknown)";
                        string patchPkgId = patchMod?.PackageIdPlayerFacing ?? patchMod?.PackageId ?? "";
                        string opType = patchInfo.Operation?.GetType().Name ?? "PatchOperation";
                        string findModInfo = !string.IsNullOrEmpty(patchInfo.FindModTarget)
                            ? $" (FindMod: {patchInfo.FindModTarget})"
                            : "";
                        string targetInfo = targetDefName != possibleDefName
                            ? $" (-> parent: {targetDefName})"
                            : "";
                        sb.AppendLine($"     - [Mod: {patchModName} ({patchPkgId})] {opType}{findModInfo}{targetInfo}");
                    }
                }
            }
            catch { }

            return hasAnyInfo ? sb.ToString().TrimEnd() : errorText;
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

        private static List<(string parentName, LoadableXmlAsset? asset, int depth)> BuildParentChain(
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
}
