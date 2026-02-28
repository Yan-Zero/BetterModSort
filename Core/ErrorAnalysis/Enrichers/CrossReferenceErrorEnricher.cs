using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using BetterModSort.Hooks;
using BetterModSort.Tools;
using Verse;

namespace BetterModSort.Core.ErrorAnalysis.Enrichers
{
    public class CrossReferenceErrorEnricher : IErrorEnricher
    {
        public int Priority => 50;

        public bool CanEnrich(string errorText)
        {
            return errorText.StartsWith("Could not resolve cross-reference to");
        }

        public IEnrichmentData? Collect(string errorText)
        {
            const string namedMarker = " named ";
            int namedIdx = errorText.IndexOf(namedMarker, StringComparison.Ordinal);
            if (namedIdx < 0) return null;

            int defNameStart = namedIdx + namedMarker.Length;
            int defNameEnd = errorText.IndexOfAny([' ', '\n', '\r'], defNameStart);
            if (defNameEnd < 0) defNameEnd = errorText.Length;

            string defName = errorText[defNameStart..defNameEnd].Trim();
            if (string.IsNullOrEmpty(defName)) return null;

            var usages = new List<CrossRefUsage>();
            var patches = new List<CrossRefPatch>();

            if (XmlBuckets.ByDefName.TryGetValue(defName, out var bag) && !bag.IsEmpty)
            {
                foreach (var node in bag)
                {
                    var (rootDefType, rootDefName) = GetRootDefInfo(node);
                    string nodePath = GetXmlNodePath(node);

                    var sourceAsset = !string.IsNullOrEmpty(rootDefType) && !string.IsNullOrEmpty(rootDefName)
                        ? DefSourceMap.GetAsset(rootDefType!, rootDefName!)
                        : null;

                    usages.Add(new CrossRefUsage(
                        sourceAsset?.mod,
                        sourceAsset?.FullFilePath ?? sourceAsset?.name,
                        nodePath));

                    var patchList = PatchSourceMap.Get(rootDefName ?? "")
                        .Where(p => PatchValueContainsDefName(p.Operation, defName))
                        .GroupBy(p => (p.Mod?.PackageId ?? "", p.Operation?.GetType().Name ?? ""))
                        .Select(g => g.First())
                        .ToList();

                    foreach (var patchInfo in patchList)
                    {
                        patches.Add(new CrossRefPatch(
                            rootDefName ?? "",
                            patchInfo.Mod,
                            patchInfo.Operation?.GetType().Name ?? "PatchOperation",
                            patchInfo.FindModTarget));
                    }
                }
            }

            if (usages.Count == 0 && patches.Count == 0)
                return null;

            return new CrossRefEnrichmentData(defName, usages, patches, errorText);
        }

        private static (string? defType, string? defName) GetRootDefInfo(XmlNode node)
        {
            var current = node;
            while (current != null && current.NodeType != XmlNodeType.Document)
            {
                var defNameChild = current["defName"]?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(defNameChild))
                    return (current.Name, defNameChild);
                current = current.ParentNode;
            }
            return (null, null);
        }

        private static string GetXmlNodePath(XmlNode node)
        {
            var parts = new List<string>();
            var current = node;
            while (current != null && current.NodeType != XmlNodeType.Document)
            {
                string name = current.Name;
                var defNameAttr = current.Attributes?["defName"]?.Value;
                var defNameChild = current["defName"]?.InnerText;
                string? identifier = defNameAttr ?? defNameChild;
                if (!string.IsNullOrEmpty(identifier))
                    name = $"{name}[defName={identifier}]";
                parts.Add(name);
                current = current.ParentNode;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static bool PatchValueContainsDefName(PatchOperation? operation, string defName)
        {
            if (operation == null || string.IsNullOrEmpty(defName))
                return false;

            try
            {
                var valueField = operation.GetType().GetField("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (valueField == null) return false;

                var value = valueField.GetValue(operation);
                if (value == null) return false;

                string? text = ExtractPatchValueText(value);
                if (string.IsNullOrEmpty(text)) return false;

                return text?.IndexOf(defName, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static string? ExtractPatchValueText(object value)
        {
            if (value is XmlContainer container)
                return container.node?.OuterXml;

            if (value is XmlNode node)
                return node.OuterXml;

            var nodeField = value.GetType().GetField("node", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (nodeField?.GetValue(value) is XmlNode reflectedNode)
                return reflectedNode.OuterXml;

            var nodeProperty = value.GetType().GetProperty("node", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (nodeProperty?.GetValue(value) is XmlNode propNode)
                return propNode.OuterXml;

            return value.ToString();
        }
    }

    public class CrossRefUsage
    {
        public ModContentPack? Mod { get; }
        public string? FilePath { get; }
        public string NodePath { get; }
        public CrossRefUsage(ModContentPack? mod, string? filePath, string nodePath)
        {
            Mod = mod;
            FilePath = filePath;
            NodePath = nodePath;
        }
    }

    public class CrossRefPatch
    {
        public string TargetDefName { get; }
        public ModContentPack? Mod { get; }
        public string OpType { get; }
        public string? FindModTarget { get; }
        public CrossRefPatch(string targetDefName, ModContentPack? mod, string opType, string? findModTarget)
        {
            TargetDefName = targetDefName;
            Mod = mod;
            OpType = opType;
            FindModTarget = findModTarget;
        }
    }

    public class CrossRefEnrichmentData(
        string targetDefName,
        List<CrossRefUsage> usages,
        List<CrossRefPatch> patches,
        string originalErrorText) : IEnrichmentData
    {
        public string TargetDefName { get; } = targetDefName;
        public List<CrossRefUsage> Usages { get; } = usages;
        public List<CrossRefPatch> Patches { get; } = patches;
        public string OriginalErrorText { get; } = originalErrorText;

        public IEnumerable<ModContentPack> GetInvolvedMods()
        {
            foreach (var u in Usages)
                if (u.Mod != null) yield return u.Mod;
            foreach (var p in Patches)
                if (p.Mod != null) yield return p.Mod;
        }

        public string FormatForConsole()
        {
            var sb = new StringBuilder();
            sb.AppendLine(OriginalErrorText);

            if (Usages.Count > 0)
            {
                string crossRefLabel = "BMS_Error_CrossRefUsedIn".TranslateSafe();
                sb.AppendLine($"  -> [{crossRefLabel}: {TargetDefName}]");

                foreach (var usage in Usages)
                {
                    if (usage.Mod != null)
                    {
                        string modName = usage.Mod.Name ?? "(unknown)";
                        string pkgId = usage.Mod.PackageIdPlayerFacing ?? usage.Mod.PackageId ?? "";
                        sb.AppendLine($"     - [Mod: {modName} ({pkgId})] {usage.NodePath}");
                        if (!string.IsNullOrEmpty(usage.FilePath))
                            sb.AppendLine($"       File: {usage.FilePath}");
                    }
                    else
                    {
                        sb.AppendLine($"     - {usage.NodePath}");
                    }
                }
            }

            if (Patches.Count > 0)
            {
                string patchLabel = "BMS_Error_PatchInvolving".TranslateSafe();
                sb.AppendLine($"  -> [{patchLabel}: {TargetDefName}]");
                foreach (var patch in Patches)
                {
                    string patchModName = patch.Mod?.Name ?? "(unknown)";
                    string patchPkgId = patch.Mod?.PackageIdPlayerFacing ?? patch.Mod?.PackageId ?? "";
                    string findModInfo = !string.IsNullOrEmpty(patch.FindModTarget)
                        ? $" (FindMod: {patch.FindModTarget})"
                        : "";
                    sb.AppendLine($"     - [Mod: {patchModName} ({patchPkgId})] {patch.OpType}{findModInfo}");
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
            sb.AppendLine($"[Cross-Reference Not Found] {summary}");

            // 使用位置
            foreach (var usage in Usages)
            {
                string mod = usage.Mod?.Name ?? "?";
                string pkg = usage.Mod?.PackageIdPlayerFacing ?? usage.Mod?.PackageId ?? "";
                sb.AppendLine($"  UsedIn: {mod} ({pkg}) | {usage.NodePath}");
            }

            // Patch
            foreach (var patch in Patches)
            {
                string mod = patch.Mod?.Name ?? "?";
                string pkg = patch.Mod?.PackageIdPlayerFacing ?? patch.Mod?.PackageId ?? "";
                sb.AppendLine($"  Patch: {mod} ({pkg}) {patch.OpType}");
            }

            return sb.ToString().TrimEnd();
        }
    }
}
