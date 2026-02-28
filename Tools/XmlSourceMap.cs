using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using Verse;

namespace BetterModSort.Tools
{
    public static class XmlSourceTracker
    {
        [ThreadStatic] public static Stack<LoadableXmlAsset>? AssetStack;
        [ThreadStatic] public static XmlNode? CurrentDefRoot;

        public static void Push(LoadableXmlAsset asset, XmlNode defRoot)
        {
            AssetStack ??= new Stack<LoadableXmlAsset>();
            AssetStack.Push(asset);
            CurrentDefRoot = defRoot;
        }

        public static void Pop()
        {
            if (AssetStack == null || AssetStack.Count == 0) return;
            AssetStack.Pop();
            CurrentDefRoot = null;
        }

        public static LoadableXmlAsset? Peek() =>
            (AssetStack != null && AssetStack.Count > 0) ? AssetStack.Peek() : null;
    }

    /// <summary>
    /// 记录 Def 定义来源：(defType, defName) -> LoadableXmlAsset
    /// </summary>
    public static class DefSourceMap
    {
        public static readonly ConcurrentDictionary<(string defType, string defName), LoadableXmlAsset> Map = new();
        public static readonly ConcurrentDictionary<(string defType, string defName), string> ParentNameMap = new();

        public static void Add(string defType, string defName, LoadableXmlAsset asset, string? parent_name = null)
        {
            if (string.IsNullOrEmpty(defType) || string.IsNullOrEmpty(defName)) return;
            Map.TryAdd((defType, defName), asset);
            if (!string.IsNullOrEmpty(parent_name))
                ParentNameMap.TryAdd((defType, defName), parent_name ?? "");
        }

        public static LoadableXmlAsset? GetAsset(string defType, string defName)
        {
            return Map.TryGetValue((defType, defName), out var asset) ? asset : null;
        }
        public static string? GetParentName(string defType, string defName)
        {
            return ParentNameMap.TryGetValue((defType, defName), out var parent) ? parent : null;
        }

        /// <summary>
        /// 根据 defName 模糊查找（不指定 defType）
        /// </summary>
        public static LoadableXmlAsset? GetAssetByDefName(string defName)
        {
            foreach (var kvp in Map)
                if (kvp.Key.defName == defName)
                    return kvp.Value;
            return null;
        }

        public static string? GetParentNameByDefName(string defName)
        {
            foreach (var kvp in ParentNameMap)
                if (kvp.Key.defName == defName)
                    return kvp.Value;
            return null;
        }

        public static void Clear()
        {
            Map.Clear();
            ParentNameMap.Clear();
        }
    }

    public static class XmlBuckets
    {
        // key: defName string (e.g. "DoctorRescue")
        // value: all listRootNodes that referenced this defName
        public static readonly ConcurrentDictionary<string, ConcurrentBag<XmlNode>> ByDefName
            = new(StringComparer.Ordinal);

        public static void Add(string? defName, XmlNode listRootNode)
        {
            string key = defName?.Trim() ?? "";
            if (key.Length == 0) return;
            var bag = ByDefName.GetOrAdd(key, _ => []);
            bag.Add(listRootNode);
        }

        public static void Clear() => ByDefName.Clear();
    }

    /// <summary>
    /// 统一清理入口，在加载完成后调用以释放内存
    /// </summary>
    public static class XmlTrackingCleanup
    {
        public static void ClearAll()
        {
            //DefSourceMap.Clear();
            XmlBuckets.Clear();
            PatchSourceMap.Clear();
            // To Keep Message?
        }
    }

    /// <summary>
    /// 记录 Patch 来源：defName -> List of (ModContentPack, xpath)
    /// </summary>
    public static class PatchSourceMap
    {
        // key 可以是 defName 也可以是 Name 属性值（用于 abstract/meta Def）
        public static readonly ConcurrentDictionary<string, ConcurrentBag<PatchInfo>> ByDefName = new(StringComparer.Ordinal);

        // 匹配 xpath 中的 defName，如 [defName="xxx"] 或 [defName='xxx']
        private static readonly Regex DefNamePattern = new(@"defName\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled);
        // 匹配 xpath 中的 @Name 属性，如 [@Name="BasePawn"] 或 [@Name='BaseWeapon']
        // 也匹配 [Name="xxx"]（无 @ 前缀）用于兼容不同的 xpath 写法
        // (?<!\w) 避免匹配到 defName 中的 Name 后缀
        private static readonly Regex NameAttrPattern = new(@"(?<!\w)@?Name\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled);
        private static readonly FieldInfo? XPathField = typeof(PatchOperationPathed)
            .GetField("xpath", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void Add(PatchOperation operation, ModContentPack mod, string? findModTarget = null)
        {
            if (operation == null || mod == null) return;

            var identifiers = new HashSet<string>(StringComparer.Ordinal);

            // 从 xpath 中提取 defName 和 Name 属性值
            if (operation is PatchOperationPathed && XPathField != null)
            {
                string? xpath = XPathField.GetValue(operation) as string;
                if (!string.IsNullOrEmpty(xpath))
                {
                    // 1) 匹配 defName="xxx"
                    foreach (Match match in DefNamePattern.Matches(xpath))
                        if (match.Success)
                        {
                            string dn = match.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(dn)) identifiers.Add(dn);
                        }
                    // 2) 匹配 @Name="xxx" 或 Name="xxx"（abstract/meta Def）
                    foreach (Match match in NameAttrPattern.Matches(xpath))
                        if (match.Success)
                        {
                            string name = match.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(name)) identifiers.Add(name);
                        }
                }
            }
            // 只有匹配上标识符的才记录
            if (identifiers.Count == 0) return;

            var patchInfo = new PatchInfo(mod, operation, findModTarget);
            foreach (var id in identifiers)
            {
                var bag = ByDefName.GetOrAdd(id, _ => []);
                bag.Add(patchInfo);
            }
        }

        public static IEnumerable<PatchInfo> Get(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return [];
            return ByDefName.TryGetValue(defName, out var bag) ? bag : [];
        }

        public static void Clear() => ByDefName.Clear();
    }

    public readonly struct PatchInfo(ModContentPack mod, PatchOperation operation, string? findModTarget = null)
    {
        public ModContentPack Mod { get; } = mod;
        public PatchOperation Operation { get; } = operation;
        public string? FindModTarget { get; } = findModTarget;
    }
}
