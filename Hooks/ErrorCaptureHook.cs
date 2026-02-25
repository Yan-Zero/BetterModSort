using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        public static void Postfix(string text)
        {
            try
            {
                ErrorCaptureHook.OnErrorCaptured(text, null);
            }
            catch
            {
                // 防止 Hook 本身的错误导致无限循环
            }
        }
    }

    /// <summary>
    /// 带异常的错误捕获
    /// </summary>
    [HarmonyPatch(typeof(Log), "ErrorOnce")]
    public static class Log_ErrorOnce_Patch
    {
        public static void Postfix(string text, int key)
        {
            try
            {
                ErrorCaptureHook.OnErrorCaptured(text, null);
            }
            catch
            {
                // 防止 Hook 本身的错误导致无限循环
            }
        }
    }

    public static class ErrorCaptureHook
    {
        public static bool EnableDebugOutput { get; set; } = true;

        /// <summary>
        /// 已捕获的错误记录（避免重复输出）
        /// </summary>
        private static readonly HashSet<int> _capturedErrorHashes = new HashSet<int>();

        /// <summary>
        /// 错误历史记录
        /// </summary>
        public static List<CapturedErrorInfo> ErrorHistory { get; } = new List<CapturedErrorInfo>();

        /// <summary>
        /// 最大历史记录数量
        /// </summary>
        public static int MaxHistoryCount { get; set; } = 100;

        public static void OnErrorCaptured(string errorText, Exception exception)
        {
            // 避免重复处理相同的错误
            var errorHash = errorText?.GetHashCode() ?? 0;
            if (_capturedErrorHashes.Contains(errorHash))
            {
                return;
            }
            _capturedErrorHashes.Add(errorHash);

            // 获取当前堆栈
            var stackTrace = new StackTrace(true);
            var capturedInfo = AnalyzeError(errorText, stackTrace, exception);

            // 保存到历史记录
            ErrorHistory.Add(capturedInfo);
            if (ErrorHistory.Count > MaxHistoryCount)
            {
                ErrorHistory.RemoveAt(0);
            }

            // Debug 输出（即使没有找到相关 MOD 也输出，方便调试）
            if (EnableDebugOutput)
            {
                OutputErrorAnalysis(capturedInfo);
            }
        }

        private static CapturedErrorInfo AnalyzeError(string errorText, StackTrace stackTrace, Exception exception)
        {
            var info = new CapturedErrorInfo
            {
                ErrorMessage = errorText,
                CapturedTime = DateTime.Now,
                StackTraceText = stackTrace.ToString()
            };

            // 从堆栈中分析涉及的 MOD
            var analyzedMods = new Dictionary<string, ModDllInfo>();

            foreach (var frame in stackTrace.GetFrames() ?? Array.Empty<StackFrame>())
            {
                var method = frame.GetMethod();
                if (method == null) continue;

                var assembly = method.DeclaringType?.Assembly;
                if (assembly == null) continue;

                // 跳过系统程序集和 Unity 程序集
                var assemblyName = assembly.GetName().Name;
                if (IsSystemAssembly(assemblyName)) continue;

                var modInfo = DllLookupTool.GetModFromAssembly(assembly);
                if (modInfo != null && !analyzedMods.ContainsKey(modInfo.PackageId))
                {
                    modInfo.StackFrameInfo = $"{method.DeclaringType?.FullName}.{method.Name}";
                    analyzedMods[modInfo.PackageId] = modInfo;
                }
            }

            // 如果有异常，也从异常中分析
            if (exception != null)
            {
                var exceptionMods = DllLookupTool.GetModsFromException(exception);
                foreach (var mod in exceptionMods)
                {
                    if (!analyzedMods.ContainsKey(mod.PackageId))
                    {
                        analyzedMods[mod.PackageId] = mod;
                    }
                }
            }

            // 从错误文本中分析可能的 MOD（通过 DLL 名称匹配）
            var textMods = DllLookupTool.AnalyzeErrorLog(errorText);
            foreach (var mod in textMods)
            {
                if (!analyzedMods.ContainsKey(mod.PackageId))
                {
                    analyzedMods[mod.PackageId] = mod;
                }
            }

            info.RelatedMods = analyzedMods.Values.ToList();
            return info;
        }

        private static void OutputErrorAnalysis(CapturedErrorInfo info)
        {
            var output = "\n[BetterModSort] ========== 错误分析 ==========\n";
            output += $"[BetterModSort] 时间: {info.CapturedTime:HH:mm:ss}\n";
            output += $"[BetterModSort] 错误: {TruncateString(info.ErrorMessage, 200)}\n";
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
            return str.Substring(0, maxLength) + "...";
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
            {
                foreach (var mod in error.RelatedMods)
                {
                    if (!stats.ContainsKey(mod.PackageId))
                    {
                        stats[mod.PackageId] = 0;
                    }
                    stats[mod.PackageId]++;
                }
            }

            return stats.OrderByDescending(x => x.Value)
                        .ToDictionary(x => x.Key, x => x.Value);
        }
    }

    public class CapturedErrorInfo
    {
        public string ErrorMessage { get; set; }
        public DateTime CapturedTime { get; set; }
        public string StackTraceText { get; set; }
        public List<ModDllInfo> RelatedMods { get; set; } = new List<ModDllInfo>();

        public override string ToString()
        {
            return $"[{CapturedTime:HH:mm:ss}] {TruncateString(ErrorMessage, 50)} - MODs: {string.Join(", ", RelatedMods.Select(m => m.ModName))}";
        }

        private static string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
                return str;
            return str.Substring(0, maxLength) + "...";
        }
    }
}
