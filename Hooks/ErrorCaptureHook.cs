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

        private static readonly HashSet<Type> _registeredTypes = [];
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
            if (!_registeredTypes.Add(enricher.GetType())) return;
            _enricherSet.Add(new EnricherEntry(enricher, _insertionCounter++));
        }

        /// <summary>
        /// 遍历所有 enricher，对第一个匹配的调用 Collect()，返回收集到的数据对象。
        /// </summary>
        public static IEnrichmentData? TryCollectEnrichment(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            foreach (var entry in _enricherSet)
            {
                if (!entry.Enricher.CanEnrich(text)) continue;
                var data = entry.Enricher.Collect(text);
                if (data != null) return data;
            }
            return null;
        }

        public static void OnErrorCaptured(string errorText, Exception? exception, IEnrichmentData? enrichment)
        {
            if (ErrorHistoryManager.IsDuplicate(errorText))
                return;

            var stackTrace = new StackTrace(true);
            var capturedInfo = ErrorAnalyzer.AnalyzeError(errorText ?? "", stackTrace, exception);
            capturedInfo.Enrichment = enrichment;

            // 将 enrichment 识别到的 MOD 合并到 RelatedMods
            if (enrichment != null)
            {
                var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in capturedInfo.RelatedMods)
                    existingIds.Add(m.PackageId);

                foreach (var mod in enrichment.GetInvolvedMods())
                {
                    if (mod != null && !existingIds.Contains(mod.PackageId))
                    {
                        existingIds.Add(mod.PackageId);
                        capturedInfo.RelatedMods.Add(new Tools.ModInfo
                        {
                            ModContentPack = mod,
                            PackageId = mod.PackageId,
                            ModName = mod.Name
                        });
                    }
                }
            }

            ErrorHistoryManager.RecordError(capturedInfo);
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
