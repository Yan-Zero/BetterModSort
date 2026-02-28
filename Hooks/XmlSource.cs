using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using Verse;
using BetterModSort.Tools;

namespace BetterModSort.Hooks
{
    [HarmonyPatch(typeof(DirectXmlToObjectNew), nameof(DirectXmlToObjectNew.DefFromNodeNew))]
    public static class Patch_DefFromNodeNew_Context
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(XmlNode node, LoadableXmlAsset loadingAsset)
        {
            if (loadingAsset != null && node != null)
            {
                XmlSourceTracker.Push(loadingAsset, node);
                // 记录 Def 来源: (defType, defName) -> asset
                string? defType = node.Name;
                string? defName = node["defName"]?.InnerText?.Trim();
                string? parentName = null;
                if (node.Attributes != null) {
                    if (string.IsNullOrEmpty(defName))
                        defName = node.Attributes["Name"]?.Value.Trim() ?? "";
                    parentName = node.Attributes["ParentName"]?.Value.Trim() ?? ""; 
                }
                if (!string.IsNullOrEmpty(defName))
                    DefSourceMap.Add(defType, defName!, loadingAsset, parentName);
            }
        }

        static void Finalizer(Exception __exception)
        {
            XmlSourceTracker.Pop();
        }
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
                Log.Message("[BetterModSort] " + "BMS_Log_PatchXpathCollected".TranslateSafe(count));
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterModSort] " + "BMS_Log_PatchXpathFailed".TranslateSafe(ex.Message));
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
                Log.Message("[BetterModSort] " + "BMS_Log_XmlTrackerCleared".TranslateSafe(defCount, bucketCount, patchCount));
            }, null, false, null);
        }
    }
}
