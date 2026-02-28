using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BetterModSort.Tools;
using Verse;

namespace BetterModSort.Core.ErrorAnalysis
{
    public static class ErrorHistoryManager
    {
        public static bool EnableDebugOutput { get; set; } = true;

        private static readonly HashSet<int> _capturedErrorHashes = [];
        public static List<CapturedErrorInfo> ErrorHistory { get; } = [];
        public static int MaxHistoryCount { get; set; } = 100;

        private static string EnsureSaveDataFolder()
        {
            string dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "BetterModSort");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }

        public static string ErrorLogFilePath => Path.Combine(EnsureSaveDataFolder(), "BetterModSort.Error.txt");
        public static string PrevErrorLogFilePath => Path.Combine(EnsureSaveDataFolder(), "BetterModSort.Error.Prev.txt");

        static ErrorHistoryManager()
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

        public static bool IsDuplicate(string errorText)
        {
            var errorHash = errorText?.GetHashCode() ?? 0;
            if (_capturedErrorHashes.Contains(errorHash))
                return true;
            _capturedErrorHashes.Add(errorHash);
            return false;
        }

        public static void RecordError(CapturedErrorInfo capturedInfo, bool isEnriched)
        {
            ErrorHistory.Add(capturedInfo);
            if (ErrorHistory.Count > MaxHistoryCount)
                ErrorHistory.RemoveAt(0);

            if (capturedInfo.RelatedMods?.Count > 0)
                AI.MetaDataManager.AppendSuspectMods(capturedInfo.RelatedMods);

            try
            {
                string textToWrite;
                if (isEnriched || capturedInfo.RelatedMods == null || capturedInfo.RelatedMods.Count == 0)
                    textToWrite = $"[{capturedInfo.CapturedTime:yyyy-MM-dd HH:mm:ss}]\n{TrimEnrichedMessage(capturedInfo.ErrorMessage ?? "")}\n\n";
                else
                    textToWrite = GenerateAnalysisOutput(capturedInfo) + "\n" + "BMS_Error_RawStackHeader".TranslateSafe() + "\n" + capturedInfo.ErrorMessage + "\n\n";
                File.AppendAllText(ErrorLogFilePath, textToWrite);
            }
            catch { }

            if (EnableDebugOutput && !isEnriched && capturedInfo.RelatedMods != null && capturedInfo.RelatedMods.Count > 0)
                OutputErrorAnalysis(capturedInfo);
        }

        public static string GenerateAnalysisOutput(CapturedErrorInfo info)
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

        public static void OutputErrorAnalysis(CapturedErrorInfo info)
        {
            Log.Message(GenerateAnalysisOutput(info));
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
        /// 对 enriched 的错误文本做智能瘦身，仅用于写入文件（不影响内存和控制台）。
        /// 截断 XML Context 块和 Unity 堆栈，保留 enricher 注入的元数据。
        /// </summary>
        private static string TrimEnrichedMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            var lines = message.Split('\n');
            var sb = new StringBuilder();
            int stackLineCount = 0;

            foreach (var line in lines)
            {
                var trimmed = line.TrimStart();

                // 截断 XML Context: 后的内容（保留前 200 字符）
                int ctxIdx = line.IndexOf("Context: <", StringComparison.Ordinal);
                if (ctxIdx >= 0)
                {
                    string beforeCtx = line.Substring(0, ctxIdx + "Context: ".Length);
                    string ctxContent = line.Substring(ctxIdx + "Context: ".Length);
                    if (ctxContent.Length > 200)
                        ctxContent = ctxContent.Substring(0, 200) + "...(truncated)";
                    sb.AppendLine(beforeCtx + ctxContent);
                    continue;
                }

                // 限制 Unity/Mono 堆栈行数（以 "at " 开头的行只保留前 3 行）
                if (trimmed.StartsWith("at ", StringComparison.Ordinal))
                {
                    stackLineCount++;
                    if (stackLineCount > 3)
                    {
                        if (stackLineCount == 4)
                            sb.AppendLine("  ... (stack trace truncated)");
                        continue;
                    }
                }
                else
                {
                    stackLineCount = 0;
                }

                sb.AppendLine(line);
            }

            return sb.ToString().TrimEnd();
        }

        public static void ClearHistory()
        {
            ErrorHistory.Clear();
            _capturedErrorHashes.Clear();
        }

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
