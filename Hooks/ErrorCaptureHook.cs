using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Verse;
using BetterModSort.Tools;

namespace BetterModSort.Hooks
{
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

        private static string EnsureSaveDataFolder()
        {
            string dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "BetterModSort");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public static string ErrorLogFilePath => Path.Combine(EnsureSaveDataFolder(), "BetterModSort.Error.txt");
        public static string PrevErrorLogFilePath => Path.Combine(EnsureSaveDataFolder(), "BetterModSort.Error.Prev.txt");

        static ErrorCaptureHook()
        {
            try
            {
                if (File.Exists(ErrorLogFilePath))
                {
                    if (File.Exists(PrevErrorLogFilePath))
                        File.Delete(PrevErrorLogFilePath);
                    File.Move(ErrorLogFilePath, PrevErrorLogFilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterModSort] " + "BMS_Error_BackupFailed".TranslateSafe() + ": " + ex);
            }
        }

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

            uint? workshopId = TryExtractWorkshopId(absFilePath!);

            ModContentPack? mod = null;
            if (workshopId.HasValue)
                mod = TryFindModBySteamWorkshopId(workshopId.Value);

            if (absFilePath != null)
                mod ??= TryFindModByPath(absFilePath);

            var extraInfo = new StringBuilder();
            extraInfo.Append("  -> [TextureSource] ");
            if (workshopId.HasValue)
                extraInfo.Append($"[WorkshopId: {workshopId.Value}] ");

            if (mod != null)
            {
                string modName = mod.Name ?? "(unknown)";
                string pkgId = mod.PackageIdPlayerFacing ?? mod.PackageId ?? "";
                extraInfo.Append($"[Mod: {modName} ({pkgId})]");
            }
            else
                extraInfo.Append($"[Mod: {"BMS_Error_ModNotMatched".TranslateSafe()}]");
            int insertIdx = text.IndexOf(absFilePath, StringComparison.OrdinalIgnoreCase);
            if (insertIdx >= 0)
            {
                int nextNewline = text.IndexOf('\n', insertIdx);
                if (nextNewline >= 0)
                    return text.Insert(nextNewline + 1, extraInfo.ToString() + "\n");
            }
            return text + "\n" + extraInfo.ToString();
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

            var sb = new StringBuilder();
            sb.AppendLine(text);
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
                        string indent = new string(' ', 5 + depth * 2);
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

            return hasAnyInfo ? sb.ToString().TrimEnd() : null;
        }

        /// <summary>
        /// 沿 ParentName 继承链向上遍历，收集所有祖先 Def 的来源信息。
        /// 最多遍历 10 层以防止循环引用。
        /// </summary>
        private static List<(string parentName, LoadableXmlAsset? asset, int depth)> BuildParentChain(
            string defTypeName, string defName, int maxDepth = 10)
        {
            var result = new List<(string, LoadableXmlAsset?, int)>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { defName };

            string? currentName = defName;
            int depth = 0;

            while (depth < maxDepth)
            {
                // 先用精确的 (defType, defName) 查找 ParentName
                string? parentName = DefSourceMap.GetParentName(defTypeName, currentName!);
                // 如果精确查找失败，使用模糊查找
                parentName ??= DefSourceMap.GetParentNameByDefName(currentName!);

                if (string.IsNullOrEmpty(parentName) || !visited.Add(parentName!))
                    break;

                // 查找 parent 的 asset
                var parentAsset = DefSourceMap.GetAsset(defTypeName, parentName!)
                    ?? DefSourceMap.GetAssetByDefName(parentName!);

                result.Add((parentName!, parentAsset, depth));
                currentName = parentName;
                depth++;
            }

            return result;
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

            var sb = new StringBuilder();
            sb.AppendLine(text);

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

            // 将本次报错涉及的嫌疑 MOD 追加到 bms.meta.txt（内部去重，用于崩溃后恢复）
            if (capturedInfo.RelatedMods?.Count > 0)
                BetterModSort.AI.MetaDataManager.AppendSuspectMods(capturedInfo.RelatedMods);

            try
            {
                string textToWrite;
                if (isEnriched || capturedInfo.RelatedMods == null || capturedInfo.RelatedMods.Count == 0)
                    textToWrite = $"[{capturedInfo.CapturedTime:yyyy-MM-dd HH:mm:ss}]\n{capturedInfo.ErrorMessage}\n\n";
                else
                    textToWrite = GenerateAnalysisOutput(capturedInfo) + "\n" + "BMS_Error_RawStackHeader".TranslateSafe() + "\n" + capturedInfo.ErrorMessage + "\n\n";
                System.IO.File.AppendAllText(ErrorLogFilePath, textToWrite);
            }
            catch { }

            // 已被路由处理的错误不需要独立输出分析
            if (EnableDebugOutput && !isEnriched && capturedInfo.RelatedMods != null && capturedInfo.RelatedMods.Count > 0)
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
                if (method?.DeclaringType == null) continue;

                var assembly = method.DeclaringType.Assembly;
                var assemblyName = assembly.GetName().Name;

                if (IsSystemAssembly(assemblyName)) continue;

                // 重点排除日志拦截器自身的堆栈，防止所有其他 MOD 的报错被无辜牵连到 BetterModSort
                // 但保留 BetterModSort 其他业务逻辑（例如 UI、AI 请求）里发生的真实报错
                if (assemblyName == "BetterModSort" && method.DeclaringType.Namespace == "BetterModSort.Hooks")
                    continue;

                var modInfo = DllLookupTool.GetModFromAssembly(assembly);
                if (modInfo != null && !analyzedMods.ContainsKey(modInfo.PackageId))
                {
                    modInfo.StackFrameInfo = $"{method.DeclaringType.FullName}.{method.Name}";
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

        private static string GenerateAnalysisOutput(CapturedErrorInfo info)
        {
            var output = $"\n[BetterModSort] {"BMS_Error_AnalysisHeader".TranslateSafe()}\n";
            output += $"{"BMS_Error_Time".TranslateSafe()}: {info.CapturedTime:HH:mm:ss}\n";
            output += $"{"BMS_Error_ErrorText".TranslateSafe()}: {TruncateString(info.ErrorMessage ?? "", 200)}\n";
            output += $"{"BMS_Error_RelatedMods".TranslateSafe()} ({info.RelatedMods.Count}):\n";

            foreach (var mod in info.RelatedMods)
            {
                output += $"  - [{mod.PackageId}] {mod.ModName}\n";
                output += $"    DLL: {mod.DllName}\n";
                if (!string.IsNullOrEmpty(mod.StackFrameInfo))
                {
                    output += $"    {"BMS_Error_Location".TranslateSafe()}: {mod.StackFrameInfo}\n";
                }
            }

            output += "BMS_Error_AnalysisFooter".TranslateSafe();
            return output;
        }

        private static void OutputErrorAnalysis(CapturedErrorInfo info)
        {
            // 使用 Message 而不是 Error 避免递归
            Log.Message(GenerateAnalysisOutput(info));
        }

        private static bool IsSystemAssembly(string assemblyName)
        {
            var systemPrefixes = new[]
            {
                "mscorlib", "System", "Unity", "Mono", 
                "Assembly-CSharp", "0Harmony", "HarmonyLib",
                "netstandard", "Microsoft"
            };

            return systemPrefixes.Any(prefix => 
                assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
                return str;
            int len = maxLength;
            if (char.IsHighSurrogate(str[len - 1])) len--;
            return str[..len] + "...";
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
}
