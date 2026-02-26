using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using HarmonyLib;
using Verse;
using BetterModSort.Tools;

namespace BetterModSort.Hooks
{
    /// <summary>
    /// 错误捕获 Hook - 自动捕获错误并输出对应的 DLL 和 MOD 信息
    /// </summary>
    [HarmonyPatch(typeof(Log), nameof(Log.Error))]
    public static class Log_Error_Patch
    {
        public static void Prefix(ref string text, out bool __state)
        {
            __state = false;
            try
            {
                var enriched = ErrorCaptureHook.TryEnrichErrorText(text);
                if (enriched != null)
                {
                    text = enriched;
                    __state = true;
                }
            }
            catch { }
        }

        public static void Postfix(string text, bool __state)
        {
            try { ErrorCaptureHook.OnErrorCaptured(text, null, __state); }
            catch { }
        }
    }

    /// <summary>
    /// 带异常的错误捕获
    /// </summary>
    [HarmonyPatch(typeof(Log), "ErrorOnce")]
    public static class Log_ErrorOnce_Patch
    {
        public static void Prefix(ref string text, out bool __state)
        {
            __state = false;
            try
            {
                var enriched = ErrorCaptureHook.TryEnrichErrorText(text);
                if (enriched != null)
                {
                    text = enriched;
                    __state = true;
                }
            }
            catch { }
        }

        public static void Postfix(string text, bool __state)
        {
            try { ErrorCaptureHook.OnErrorCaptured(text, null, __state); }
            catch { }
        }
    }

    public static class ErrorCaptureHook
    {
        private static readonly Regex WorkshopIdInPathRegex = new(@"[\\/]content[\\/]\d+[\\/](\d+)(?:[\\/]|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool EnableDebugOutput { get; set; } = true;

        /// <summary>
        /// 已捕获的错误记录（避免重复输出）
        /// </summary>
        private static readonly HashSet<int> _capturedErrorHashes = [];

        /// <summary>
        /// 错误历史记录
        /// </summary>
        public static List<CapturedErrorInfo> ErrorHistory { get; } = [];

        /// <summary>
        /// 最大历史记录数量
        /// </summary>
        public static int MaxHistoryCount { get; set; } = 100;

        /// <summary>
        /// 从堆栈中查找 DefDatabase.ErrorCheckAllDefs 的声明类型
        /// </summary>
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

        public static string? TryEnrichErrorText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            if (text.StartsWith("Exception loading UnityEngine.Texture2D from file.", StringComparison.Ordinal))
                return TryGetTextureLoadSource(text);

            if (text.StartsWith("Config error in ") || text.StartsWith("Exception in ConfigErrors() of "))
                return TryGetDefDatabaseSource(text);

            if (text.StartsWith("XML error: "))
                return TryGetXmlSource(text);

            if (text.StartsWith("Could not resolve cross-reference to"))
                return TryGetCrossReferenceSource(text);

            return null;
        }

        private static string? TryGetTextureLoadSource(string text)
        {
            string? absFilePath = ExtractLineValue(text, "absFilePath:");
            if (string.IsNullOrEmpty(absFilePath)) return null;

            uint? workshopId = TryExtractWorkshopId(absFilePath);

            ModContentPack? mod = null;
            if (workshopId.HasValue)
                mod = TryFindModBySteamWorkshopId(workshopId.Value);

            mod ??= TryFindModByPath(absFilePath);

            var sb = new StringBuilder();
            sb.AppendLine(text);
            sb.Append("  -> [TextureSource] ");
            if (workshopId.HasValue)
                sb.Append($"[WorkshopId: {workshopId.Value}] ");

            if (mod != null)
            {
                string modName = mod.Name ?? "(unknown)";
                string pkgId = mod.PackageIdPlayerFacing ?? mod.PackageId ?? "";
                sb.Append($"[Mod: {modName} ({pkgId})] ");
            }
            else
                sb.Append("[Mod: 未匹配] ");

            sb.Append($"[Path: {absFilePath}]");
            return sb.ToString();
        }

        private static string? ExtractLineValue(string text, string key)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key)) return null;

            foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                if (line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(key.Length).Trim();

            return null;
        }

        private static uint? TryExtractWorkshopId(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var match = WorkshopIdInPathRegex.Match(path);
            if (!match.Success) return null;
            return uint.TryParse(match.Groups[1].Value, out var id) ? id : null;
        }

        private static ModContentPack? TryFindModBySteamWorkshopId(uint workshopId)
        {
            foreach (var runningMod in LoadedModManager.RunningMods)
                if (runningMod.SteamAppId == workshopId)
                    return runningMod;

            return TryFindModByPathSegment(workshopId.ToString());
        }

        private static ModContentPack? TryFindModByPathSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return null;
            foreach (var mod in LoadedModManager.RunningMods)
            {
                string root = mod.RootDir ?? "";
                if (root.IndexOf(segment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return mod;
            }
            return null;
        }

        private static ModContentPack? TryFindModByPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;

            foreach (var mod in LoadedModManager.RunningMods)
            {
                string root = mod.RootDir ?? "";
                if (string.IsNullOrEmpty(root)) continue;
                if (filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return mod;
            }

            return null;
        }

        private static string? TryGetDefDatabaseSource(string text)
        {
            var declaringType = FindDefDatabaseDeclaringType(new StackTrace(false));
            if (declaringType == null || !declaringType.IsGenericType) return null;

            // "Config error in " = 16 chars, "Exception in ConfigErrors() of " = 31 chars
            int startIndex = text.StartsWith("Config error in ") ? 16 : 31;
            int endIndex = text.IndexOf(':', startIndex);
            if (endIndex <= startIndex) return null;

            string possibleDefName = text[startIndex..endIndex];
            if (string.IsNullOrEmpty(possibleDefName)) return null;

            // 部分 ToString() 会返回形如 "ThingDef(MyDefName)"，需要简单剔除括号
            if (possibleDefName.EndsWith(")") && possibleDefName.Contains('('))
            {
                int openIdx = possibleDefName.IndexOf('(');
                possibleDefName = possibleDefName.Substring(openIdx + 1, possibleDefName.Length - openIdx - 2);
            }

            try
            {
                var defType = declaringType.GetGenericArguments()[0];
                var getNamedMethod = typeof(DefDatabase<>).MakeGenericType(defType)
                    .GetMethod("GetNamed", [typeof(string), typeof(bool)]);
                if (getNamedMethod != null)
                    if (getNamedMethod.Invoke(null, [possibleDefName, false]) is Def def)
                    {
                        string modName = def.modContentPack?.Name ?? "未知或原版";
                        string fileName = def.fileName ?? "未知文件";
                        return $"{text}\n  -> [来源 MOD: {modName}] [文件: {fileName}]";
                    }
            }
            catch { }

            return null;
        }

        private static string? TryGetXmlSource(string text)
        {
            var asset = XmlSourceTracker.Peek();
            if (asset == null) return null;
            var mod = asset.mod;
            string modName = mod?.Name ?? "(unknown mod)";
            string pkgId = mod?.PackageIdPlayerFacing ?? mod?.PackageId ?? "(unknown packageId)";
            string filePath = asset.FullFilePath ?? asset.name ?? "(unknown file)";
            string defRoot = XmlSourceTracker.CurrentDefRoot?.Name ?? "(unknown root)";
            return $"[XmlSource] Mod={modName} ({pkgId}) File={filePath} Root={defRoot}\n{text}";
        }

        private static string? TryGetCrossReferenceSource(string text)
        {
            // 格式: "Could not resolve cross-reference to Verse.WorkTypeDef named DoctorRescue ..."
            const string namedMarker = " named ";
            int namedIdx = text.IndexOf(namedMarker);
            if (namedIdx < 0) return null;

            int defNameStart = namedIdx + namedMarker.Length;
            // defName 以空格、换行或字符串结尾为止
            int defNameEnd = text.IndexOfAny([' ', '\n', '\r'], defNameStart);
            if (defNameEnd < 0) defNameEnd = text.Length;

            string defName = text[defNameStart..defNameEnd].Trim();
            if (string.IsNullOrEmpty(defName)) return null;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(text);

            bool hasAnyInfo = false;

            // 1) 在 XmlBuckets 中查找引用了该 defName 的 XML 节点
            if (XmlBuckets.ByDefName.TryGetValue(defName, out var bag) && !bag.IsEmpty)
            {
                hasAnyInfo = true;
                sb.AppendLine($"  -> [引用 defName: {defName}] 在以下 Def XML 中被引用:");

                foreach (var node in bag)
                {
                    var (rootDefType, rootDefName) = GetRootDefInfo(node);
                    string nodePath = GetXmlNodePath(node);

                    var sourceAsset = !string.IsNullOrEmpty(rootDefType) && !string.IsNullOrEmpty(rootDefName)
                        ? DefSourceMap.Get(rootDefType, rootDefName)
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
                    var patches = PatchSourceMap.Get(rootDefName)
                        .Where(p => PatchValueContainsDefName(p.Operation, defName))
                        .GroupBy(p => (p.Mod?.PackageId ?? "", p.Operation?.GetType().Name ?? ""))
                        .Select(g => g.First())
                        .ToList();
                    if (patches.Count > 0)
                    {
                        hasAnyInfo = true;
                        sb.AppendLine($"  -> [Patch 涉及 defName: {rootDefName}] 以下 Mod 的 Patch 可能涉及:");
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

            return hasAnyInfo ? sb.ToString().TrimEnd() : null;
        }

        /// <summary>
        /// 从 XmlNode 向上遍历找到根 Def 节点，返回 (defType, defName)
        /// </summary>
        private static (string? defType, string? defName) GetRootDefInfo(XmlNode node)
        {
            var current = node;
            while (current != null && current.NodeType != XmlNodeType.Document)
            {
                // 检查是否有 defName 子元素（表示这是一个 Def 根节点）
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
                // 尝试获取 defName 属性或子元素
                var defNameAttr = current.Attributes?["defName"]?.Value;
                var defNameChild = current["defName"]?.InnerText;
                string identifier = defNameAttr ?? defNameChild;
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

        public static void OnErrorCaptured(string errorText, Exception? exception, bool isEnriched)
        {
            // 避免重复处理相同的错误
            var errorHash = errorText?.GetHashCode() ?? 0;
            if (_capturedErrorHashes.Contains(errorHash))
                return;
            _capturedErrorHashes.Add(errorHash);

            // 保存到历史记录
            var stackTrace = new StackTrace(true);
            var capturedInfo = AnalyzeError(errorText ?? "", stackTrace, exception);
            ErrorHistory.Add(capturedInfo);
            if (ErrorHistory.Count > MaxHistoryCount)
                ErrorHistory.RemoveAt(0);

            // 已被路由处理的错误不需要独立输出分析
            if (EnableDebugOutput && !isEnriched)
                OutputErrorAnalysis(capturedInfo);
        }

        private static CapturedErrorInfo AnalyzeError(string errorText, StackTrace stackTrace, Exception? exception)
        {
            var info = new CapturedErrorInfo
            {
                ErrorMessage = errorText,
                CapturedTime = DateTime.Now,
                StackTraceText = stackTrace.ToString()
            };

            // 从堆栈中分析涉及的 MOD
            var analyzedMods = new Dictionary<string, ModDllInfo>();

            foreach (var frame in stackTrace.GetFrames())
            {
                var method = frame.GetMethod();
                var assembly = method.DeclaringType.Assembly;
                if (IsSystemAssembly(assembly.GetName().Name)) continue;
                var modInfo = DllLookupTool.GetModFromAssembly(assembly);
                if (modInfo != null && !analyzedMods.ContainsKey(modInfo.PackageId))
                {
                    modInfo.StackFrameInfo = $"{method.DeclaringType?.FullName}.{method.Name}";
                    analyzedMods[modInfo.PackageId] = modInfo;
                }
            }

            // 如果有异常，也从异常中分析
            if (exception != null)
                foreach (var mod in DllLookupTool.GetModsFromException(exception))
                    if (!analyzedMods.ContainsKey(mod.PackageId))
                        analyzedMods[mod.PackageId] = mod;
            // 从错误文本中分析可能的 MOD（通过 DLL 名称匹配）
            foreach (var mod in DllLookupTool.AnalyzeErrorLog(errorText))
                if (!analyzedMods.ContainsKey(mod.PackageId))
                    analyzedMods[mod.PackageId] = mod;

            info.RelatedMods = [.. analyzedMods.Values];
            return info;
        }

        private static void OutputErrorAnalysis(CapturedErrorInfo info)
        {
            var output = "\n[BetterModSort] ========== 错误分析 ==========\n";
            output += $"[BetterModSort] 时间: {info.CapturedTime:HH:mm:ss}\n";
            output += $"[BetterModSort] 错误: {TruncateString(info.ErrorMessage ?? "", 200)}\n";
            output += $"[BetterModSort] 涉及的 MOD ({info.RelatedMods.Count}):\n";

            foreach (var mod in info.RelatedMods)
            {
                output += $"[BetterModSort]   - [{mod.PackageId}] {mod.ModName}\n";
                output += $"[BetterModSort]     DLL: {mod.DllName}\n";
                if (!string.IsNullOrEmpty(mod.StackFrameInfo))
                {
                    output += $"[BetterModSort]     位置: {mod.StackFrameInfo}\n";
                }
            }

            output += "[BetterModSort] =====================================";

            // 使用 Message 而不是 Error 避免递归
            Log.Message(output);
        }

        private static bool IsSystemAssembly(string assemblyName)
        {
            var systemPrefixes = new[]
            {
                "mscorlib", "System", "Unity", "Mono", 
                "Assembly-CSharp", "0Harmony", "HarmonyLib",
                "netstandard", "Microsoft", "BetterModSort"
            };

            return systemPrefixes.Any(prefix => 
                assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
                return str;
            return str[..maxLength] + "...";
        }

        /// <summary>
        /// 清除历史记录
        /// </summary>
        public static void ClearHistory()
        {
            ErrorHistory.Clear();
            _capturedErrorHashes.Clear();
        }

        /// <summary>
        /// 获取错误统计信息（按 MOD 分组）
        /// </summary>
        public static Dictionary<string, int> GetErrorStatsByMod()
        {
            var stats = new Dictionary<string, int>();

            foreach (var error in ErrorHistory)
                foreach (var mod in error.RelatedMods)
                {
                    if (!stats.ContainsKey(mod.PackageId))
                        stats[mod.PackageId] = 0;
                    stats[mod.PackageId]++;
                }

            return stats.OrderByDescending(x => x.Value)
                        .ToDictionary(x => x.Key, x => x.Value);
        }
    }

    public class CapturedErrorInfo
    {
        public string? ErrorMessage { get; set; }
        public DateTime CapturedTime { get; set; }
        public string? StackTraceText { get; set; }
        public List<ModDllInfo> RelatedMods { get; set; } = [];

        public override string ToString()
        {
            return $"[{CapturedTime:HH:mm:ss}] {TruncateString(ErrorMessage ?? "", 50)} - MODs: {string.Join(", ", RelatedMods.Select(m => m.ModName))}";
        }

        private static string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
                return str;
            return str[..maxLength] + "...";
        }
    }
}
