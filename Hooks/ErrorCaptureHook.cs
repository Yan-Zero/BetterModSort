using System;
using System.Collections.Generic;
using System.Diagnostics;
using BetterModSort.Core.ErrorAnalysis;
using BetterModSort.Core.ErrorAnalysis.Enrichers;

namespace BetterModSort.Hooks
{
    public static class ErrorCaptureHook
    {
        private static int _insertionCounter = 0;

        private static readonly SortedSet<EnricherEntry> _enricherSet = new(EnricherEntry.Comparer.Instance);

        static ErrorCaptureHook()
        {
            RegisterEnricher(new DefConfigErrorEnricher());
            RegisterEnricher(new XmlErrorEnricher());
            RegisterEnricher(new CrossReferenceErrorEnricher());
            RegisterEnricher(new FilePathEnricher());
        }

        public static void RegisterEnricher(IErrorEnricher enricher)
        {
            if (enricher == null) return;
            // 避免重复注册同类型
            foreach (var entry in _enricherSet)
                if (entry.Enricher.GetType() == enricher.GetType())
                    return;
            _enricherSet.Add(new EnricherEntry(enricher, _insertionCounter++));
        }

        public static string? TryEnrichErrorText(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            foreach (var entry in _enricherSet)
                if (entry.Enricher.CanEnrich(text))
                    return entry.Enricher.Enrich(text);
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
        
        /// <summary>
        /// 封装 enricher 和注册顺序，用于 SortedSet 排序
        /// </summary>
        internal readonly struct EnricherEntry(IErrorEnricher enricher, int insertionOrder)
        {
            public IErrorEnricher Enricher { get; } = enricher;
            public int InsertionOrder { get; } = insertionOrder;

            /// <summary>
            /// 按 Priority 升序，相同优先级按注册顺序升序
            /// </summary>
            public class Comparer : IComparer<EnricherEntry>
            {
                public static readonly Comparer Instance = new();

                public int Compare(EnricherEntry x, EnricherEntry y)
                {
                    int cmp = x.Enricher.Priority.CompareTo(y.Enricher.Priority);
                    return cmp != 0 ? cmp : x.InsertionOrder.CompareTo(y.InsertionOrder);
                }
            }
        }
    }
}
