using System.Collections.Generic;
using Verse;

namespace BetterModSort.Core.ErrorAnalysis;

/// <summary>
/// 错误 Enrichment 结果的标记接口。
/// 每个 Enricher 定义自己的实现类，包含专属的强类型数据和格式化逻辑。
/// </summary>
public interface IEnrichmentData
{
    /// <summary>
    /// Collect 时传入的原始错误文本（未被 FormatForConsole 修改过的）
    /// </summary>
    string OriginalErrorText { get; }

    /// <summary>
    /// 返回此次 enrichment 确认关联的 MOD 列表，供分析器合并到 RelatedMods
    /// </summary>
    IEnumerable<ModContentPack> GetInvolvedMods();

    /// <summary>
    /// 格式化为控制台展示文本（带排版、中文翻译等，会注入到游戏 Log 中）。
    /// 使用 OriginalErrorText 作为基础。
    /// </summary>
    string FormatForConsole();

    /// <summary>
    /// 格式化为精简的文件日志文本（给大模型看，高密度、无冗余空行）。
    /// 使用 OriginalErrorText 作为基础。
    /// </summary>
    string FormatForFile();
}
