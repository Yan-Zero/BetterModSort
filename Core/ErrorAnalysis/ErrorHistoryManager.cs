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

        public static void RecordError(CapturedErrorInfo capturedInfo)
        {
            ErrorHistory.Add(capturedInfo);
            if (ErrorHistory.Count > MaxHistoryCount)
                ErrorHistory.RemoveAt(0);

            if (capturedInfo.RelatedMods?.Count > 0)
                AI.MetaDataManager.AppendSuspectMods(capturedInfo.RelatedMods);

            try
            {
                string textToWrite;
                if (capturedInfo.Enrichment != null)
                    // enrichment 数据对象自己决定如何生成精简的文件日志
                    textToWrite = $"[{capturedInfo.CapturedTime:yyyy-MM-dd HH:mm:ss}]\n{capturedInfo.Enrichment.FormatForFile()}\n\n";
                else if (capturedInfo.RelatedMods != null && capturedInfo.RelatedMods.Count > 0)
                    // 非 enriched 但有 DLL 堆栈分析结果，生成精简格式
                    textToWrite = GenerateCompactFileOutput(capturedInfo);
                else
                    // 无任何分析结果，直接记录原始文本
                    textToWrite = $"[{capturedInfo.CapturedTime:yyyy-MM-dd HH:mm:ss}]\n{TruncateString(capturedInfo.ErrorMessage ?? "", 200)}\n\n";
                File.AppendAllText(ErrorLogFilePath, textToWrite);
            }
            catch { }

            if (EnableDebugOutput && capturedInfo.Enrichment == null && capturedInfo.RelatedMods != null && capturedInfo.RelatedMods.Count > 0)
                OutputErrorAnalysis(capturedInfo);
        }

        /// <summary>
        /// 精简的文件输出格式（非 enriched 错误，有 DLL 堆栈分析结果时）
        /// </summary>
        private static string GenerateCompactFileOutput(CapturedErrorInfo info)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{info.CapturedTime:yyyy-MM-dd HH:mm:ss}]");
            sb.AppendLine(TruncateString(info.ErrorMessage ?? "", 200));
            foreach (var mod in info.RelatedMods)
            {
                string location = !string.IsNullOrEmpty(mod.LocationContext) ? $" | {mod.LocationContext}" : "";
                string dll = !string.IsNullOrEmpty(mod.DllName) ? $" DLL:{mod.DllName}" : "";
                sb.AppendLine($"  -> [{mod.PackageId}] {mod.ModName}{dll}{location}");
            }
            sb.AppendLine();
            return sb.ToString();
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
                if (!string.IsNullOrEmpty(mod.DllName))
                    output += $"    DLL: {mod.DllName}\n";
                if (!string.IsNullOrEmpty(mod.LocationContext))
                {
                    output += $"    {"BMS_Error_Location".TranslateSafe()}: {mod.LocationContext}\n";
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
