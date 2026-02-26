using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using Verse;

namespace BetterModSort.Hooks
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

    [HarmonyPatch(typeof(DirectXmlToObjectNew), nameof(DirectXmlToObjectNew.DefFromNodeNew))]
    public static class Patch_DefFromNodeNew_Context
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(XmlNode node, LoadableXmlAsset loadingAsset)
        {
            if (loadingAsset != null)
            {
                XmlSourceTracker.Push(loadingAsset, node);
                // 记录 Def 来源: (defType, defName) -> asset
                string? defType = node.Name;
                string? defName = node["defName"]?.InnerText?.Trim() ?? "";
                if (!string.IsNullOrEmpty(defName))
                    DefSourceMap.Add(defType, defName, loadingAsset);
            }
        }

        static void Finalizer(Exception __exception)
        {
            XmlSourceTracker.Pop();
        }
    }

    /// <summary>
    /// 记录 Def 定义来源：(defType, defName) -> LoadableXmlAsset
    /// </summary>
    public static class DefSourceMap
    {
        public static readonly ConcurrentDictionary<(string defType, string defName), LoadableXmlAsset> Map = new();

        public static void Add(string defType, string defName, LoadableXmlAsset asset)
        {
            if (string.IsNullOrEmpty(defType) || string.IsNullOrEmpty(defName)) return;
            Map.TryAdd((defType, defName), asset);
        }

        public static LoadableXmlAsset? Get(string defType, string defName)
        {
            return Map.TryGetValue((defType, defName), out var asset) ? asset : null;
        }

        /// <summary>
        /// 根据 defName 模糊查找（不指定 defType）
        /// </summary>
        public static LoadableXmlAsset? GetByDefName(string defName)
        {
            foreach (var kvp in Map)
                if (kvp.Key.defName == defName)
                    return kvp.Value;
            return null;
        }

        public static void Clear() => Map.Clear();
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
            DefSourceMap.Clear();
            XmlBuckets.Clear();
            PatchSourceMap.Clear();
        }
    }

    /// <summary>
    /// 记录 Patch 来源：defName -> List of (ModContentPack, xpath)
    /// </summary>
    public static class PatchSourceMap
    {
        public static readonly ConcurrentDictionary<string, ConcurrentBag<PatchInfo>> ByDefName = new(StringComparer.Ordinal);

        // 匹配 xpath 中的 defName，如 [defName="xxx"] 或 [defName='xxx']
        private static readonly Regex DefNamePattern = new(@"defName\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled);
        private static readonly FieldInfo? XPathField = typeof(PatchOperationPathed)
            .GetField("xpath", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void Add(PatchOperation operation, ModContentPack mod, string? findModTarget = null)
        {
            if (operation == null || mod == null) return;

            var defNames = new HashSet<string>(StringComparer.Ordinal);

            // 1) 从 xpath 中提取 defName
            if (operation is PatchOperationPathed && XPathField != null)
            {
                string? xpath = XPathField.GetValue(operation) as string;
                if (!string.IsNullOrEmpty(xpath))
                    foreach (Match match in DefNamePattern.Matches(xpath))
                        if (match.Success)
                        {
                            string dn = match.Groups[1].Value.Trim();
                            if (!string.IsNullOrEmpty(dn)) defNames.Add(dn);
                        }
            }
            // 只有匹配上 defName 的才记录
            if (defNames.Count == 0) return;

            var patchInfo = new PatchInfo(mod, operation, findModTarget);
            foreach (var defName in defNames)
            {
                var bag = ByDefName.GetOrAdd(defName, _ => []);
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

    /// <summary>
    /// 在 ApplyPatches 之前收集所有 PatchOperationPathed 的 xpath
    /// </summary>
    [HarmonyPatch(typeof(LoadedModManager), nameof(LoadedModManager.ApplyPatches))]
    public static class Patch_ApplyPatches_CollectXPath
    {


        [HarmonyPriority(Priority.First)]
        static void Prefix(XmlDocument xmlDoc, Dictionary<XmlNode, LoadableXmlAsset> assetlookup)
        {

            try
            {
                int count = 0;
                foreach (var mod in LoadedModManager.RunningMods)
                {
                    foreach (var patch in mod.Patches)
                    {
                        // 递归收集所有 PatchOperationPathed（包括嵌套在 Sequence/FindMod 等里面的）
                        CollectPatchOperations(patch, mod, ref count);
                    }
                }
                Log.Message($"[BetterModSort] 已收集 {count} 个 Patch xpath 映射");
            }
            catch (Exception ex)
            {
                Log.Warning($"[BetterModSort] 收集 Patch xpath 失败: {ex.Message}");
            }
        }

        private static void CollectPatchOperations(PatchOperation patch, ModContentPack mod, ref int count, string? findModTarget = null)
        {
            if (patch == null) return;

            // 如果是 PatchOperationPathed 子类，提取 xpath 和 value 中的 defName
            if (patch is PatchOperationPathed)
            {
                PatchSourceMap.Add(patch, mod, findModTarget);
                count++;
            }

            // 递归处理嵌套的 PatchOperation
            var type = patch.GetType();

            // 检查 PatchOperationFindMod.mods 字段，提取目标 MOD 名作为上下文
            var modsField = type.GetField("mods", BindingFlags.NonPublic | BindingFlags.Instance);
            var findMods = modsField?.GetValue(patch) as List<string>;
            string? childFindModTarget = findMods is { Count: > 0 }
                ? string.Join(", ", findMods)
                : findModTarget;

            // PatchOperationSequence.operations
            var operationsField = type.GetField("operations", BindingFlags.NonPublic | BindingFlags.Instance);
            if (operationsField?.GetValue(patch) is List<PatchOperation> operations)
                foreach (var op in operations)
                    CollectPatchOperations(op, mod, ref count, childFindModTarget);

            // match / nomatch (PatchOperationFindMod, PatchOperationConditional, PatchOperationTest 等)
            var matchField = type.GetField("match", BindingFlags.NonPublic | BindingFlags.Instance);
            if (matchField?.GetValue(patch) is PatchOperation matchOp)
                CollectPatchOperations(matchOp, mod, ref count, childFindModTarget);

            var nomatchField = type.GetField("nomatch", BindingFlags.NonPublic | BindingFlags.Instance);
            if (nomatchField?.GetValue(patch) is PatchOperation nomatchOp)
                CollectPatchOperations(nomatchOp, mod, ref count, childFindModTarget);
        }
    }

    [HarmonyPatch(typeof(DirectXmlToObject), nameof(DirectXmlToObject.ValidateListNode))]
    public static class Patch_ValidateListNode_Context
    {
        [HarmonyPriority(Priority.First)]
        static void Postfix(ref bool __result, XmlNode listEntryNode, XmlNode listRootNode, Type listItemType)
        {
            if (!__result) return;

            // 1) KeyValuePair<K,V> 情况：分别判断 K / V 是否为 Def
            if (listItemType.IsGenericType && listItemType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                Type[] args = listItemType.GetGenericArguments();
                Type kType = args[0];
                Type vType = args[1];

                if (GenTypes.IsDef(kType))
                {
                    string? kName = listEntryNode["key"]?.InnerText;
                    if (!kName.NullOrEmpty())
                        XmlBuckets.Add(kName, listRootNode);
                }
                if (GenTypes.IsDef(vType))
                {
                    string? vName = listEntryNode["value"]?.InnerText;
                    if (!vName.NullOrEmpty())
                        XmlBuckets.Add(vName, listRootNode);
                }
                return;
            }

            // 2) listItemType 本身是 Def：用 entry 的 InnerText
            if (GenTypes.IsDef(listItemType))
            {
                string? name = listEntryNode.InnerText;
                if (!name.NullOrEmpty())
                    XmlBuckets.Add(name, listRootNode);
            }
        }
    }

    /// <summary>
    /// 在游戏初始化完成后清理 XML 追踪数据以释放内存
    /// Hook UIRoot_Entry.Init（主菜单初始化完成）
    /// </summary>
    [HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.Init))]
    public static class Patch_UIRoot_Entry_Init_Cleanup
    {
        static void Postfix()
        {
            // 延迟清理，确保所有错误已经处理完毕
            LongEventHandler.QueueLongEvent(() =>
            {
                int defCount = DefSourceMap.Map.Count;
                int bucketCount = XmlBuckets.ByDefName.Count;
                int patchCount = PatchSourceMap.ByDefName.Count;
                XmlTrackingCleanup.ClearAll();
                Log.Message($"[BetterModSort] XML 追踪数据已清理 (DefSourceMap: {defCount}, XmlBuckets: {bucketCount}, PatchSourceMap: {patchCount})");
            }, null, false, null);
        }
    }
}