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
        public bool CanEnrich(string errorText)
        {
            return errorText.StartsWith("Could not resolve cross-reference to");
        }

        public string? Enrich(string errorText)
        {
            // 格式: "Could not resolve cross-reference to Verse.WorkTypeDef named DoctorRescue ..."
            const string namedMarker = " named ";
            int namedIdx = errorText.IndexOf(namedMarker, StringComparison.Ordinal);
            if (namedIdx < 0) return errorText;

            int defNameStart = namedIdx + namedMarker.Length;
            // defName 以空格、换行或字符串结尾为止
            int defNameEnd = errorText.IndexOfAny([' ', '\n', '\r'], defNameStart);
            if (defNameEnd < 0) defNameEnd = errorText.Length;

            string defName = errorText[defNameStart..defNameEnd].Trim();
            if (string.IsNullOrEmpty(defName)) return errorText;

            var sb = new StringBuilder();
            sb.AppendLine(errorText);

            bool hasAnyInfo = false;

            // 1) 在 XmlBuckets 中查找引用了该 defName 的 XML 节点
            if (XmlBuckets.ByDefName.TryGetValue(defName, out var bag) && !bag.IsEmpty)
            {
                hasAnyInfo = true;
                string crossRefLabel = "BMS_Error_CrossRefUsedIn".TranslateSafe();
                sb.AppendLine($"  -> [{crossRefLabel}: {defName}]");

                foreach (var node in bag)
                {
                    var (rootDefType, rootDefName) = GetRootDefInfo(node);
                    string nodePath = GetXmlNodePath(node);

                    var sourceAsset = !string.IsNullOrEmpty(rootDefType) && !string.IsNullOrEmpty(rootDefName)
                        ? DefSourceMap.GetAsset(rootDefType!, rootDefName!)
                        : null;

                    if (sourceAsset != null)
                    {
                        var mod = sourceAsset.mod;
                        string modName = mod?.Name ?? "(unknown)";
                        string pkgId = mod?.PackageIdPlayerFacing ?? mod?.PackageId ?? "";
                        string file = sourceAsset.FullFilePath ?? sourceAsset.name ?? "";
                        sb.AppendLine($"     - [Mod: {modName} ({pkgId})] {nodePath}");
                        if (!string.IsNullOrEmpty(file))
                            sb.AppendLine($"       File: {file}");
                    }
                    else
                        sb.AppendLine($"     - {nodePath}");

                    // 2) 在 PatchSourceMap 中查找可能修改了该 Def 的 Patch
                    var patches = PatchSourceMap.Get(rootDefName ?? "")
                        .Where(p => PatchValueContainsDefName(p.Operation, defName))
                        .GroupBy(p => (p.Mod?.PackageId ?? "", p.Operation?.GetType().Name ?? ""))
                        .Select(g => g.First())
                        .ToList();
                        
                    if (patches.Count > 0)
                    {
                        hasAnyInfo = true;
                        string patchLabel2 = "BMS_Error_PatchInvolving".TranslateSafe();
                        sb.AppendLine($"  -> [{patchLabel2}: {rootDefName ?? ""}]");
                        foreach (var patchInfo in patches)
                        {
                            var patchMod = patchInfo.Mod;
                            string patchModName = patchMod?.Name ?? "(unknown)";
                            string patchPkgId = patchMod?.PackageIdPlayerFacing ?? patchMod?.PackageId ?? "";
                            string opType = patchInfo.Operation?.GetType().Name ?? "PatchOperation";
                            string findModInfo = !string.IsNullOrEmpty(patchInfo.FindModTarget)
                                ? $" (FindMod: {patchInfo.FindModTarget})"
                                : "";
                            sb.AppendLine($"     - [Mod: {patchModName} ({patchPkgId})] {opType}{findModInfo}");
                        }
                    }
                }
            }

            return hasAnyInfo ? sb.ToString().TrimEnd() : errorText;
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
}
