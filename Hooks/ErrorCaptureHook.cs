using System;
using System.Collections.Generic;
using System.Diagnostics;
using BetterModSort.Core.ErrorAnalysis;
using BetterModSort.Core.ErrorAnalysis.Enrichers;

namespace BetterModSort.Hooks
{
    public static class ErrorCaptureHook
    {
        private static readonly List<IErrorEnricher> Enrichers = new List<IErrorEnricher>
        {
            new TextureErrorEnricher(),
            new DefConfigErrorEnricher(),
            new XmlErrorEnricher(),
            new CrossReferenceErrorEnricher()
        };

        public static void RegisterEnricher(IErrorEnricher enricher)
        {
            if (enricher != null && !Enrichers.Contains(enricher))
                Enrichers.Add(enricher);
        }

        public static string? TryEnrichErrorText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            foreach (var enricher in Enrichers)
                if (enricher.CanEnrich(text))
                    return enricher.Enrich(text);
            return null;
        }

        public static void OnErrorCaptured(string errorText, Exception? exception, bool isEnriched)
        {
            if (ErrorHistoryManager.IsDuplicate(errorText))
                return;

            var stackTrace = new StackTrace(true);
            var capturedInfo = ErrorAnalyzer.AnalyzeError(errorText ?? "", stackTrace, exception);

            ErrorHistoryManager.RecordError(capturedInfo, isEnriched);
        }
        
        // 保留原有的公开 API，指向 ErrorHistoryManager
        public static bool EnableDebugOutput 
        { 
            get => ErrorHistoryManager.EnableDebugOutput; 
            set => ErrorHistoryManager.EnableDebugOutput = value; 
        }

        public static List<CapturedErrorInfo> ErrorHistory => ErrorHistoryManager.ErrorHistory;

        public static int MaxHistoryCount 
        { 
            get => ErrorHistoryManager.MaxHistoryCount; 
            set => ErrorHistoryManager.MaxHistoryCount = value; 
        }

        public static string ErrorLogFilePath => ErrorHistoryManager.ErrorLogFilePath;
        public static string PrevErrorLogFilePath => ErrorHistoryManager.PrevErrorLogFilePath;

        public static void ClearHistory()
        {
            ErrorHistoryManager.ClearHistory();
        }

        public static Dictionary<string, int> GetErrorStatsByMod()
        {
            return ErrorHistoryManager.GetErrorStatsByMod();
        }
    }
}
