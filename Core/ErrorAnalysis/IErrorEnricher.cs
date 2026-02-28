namespace BetterModSort.Core.ErrorAnalysis;

/// <summary>
/// 错误文本强化器接口。Priority 越小优先级越高，默认 100。
/// Enricher 作为工厂/策略：判定是否匹配 + 收集结构化数据。
/// 格式化逻辑由返回的 IEnrichmentData 实例自己负责。
/// </summary>
public interface IErrorEnricher
{
    /// <summary>
    /// 优先级，数值越小越优先执行。专用 enricher 建议 50，通用兜底建议 200。
    /// </summary>
    int Priority => 100;

    bool CanEnrich(string errorText);

    /// <summary>
    /// 从错误文本中收集结构化数据，返回包含专属数据和格式化逻辑的 IEnrichmentData 实例。
    /// 返回 null 表示虽然 CanEnrich 为 true 但实际未能提取到有效信息。
    /// </summary>
    IEnrichmentData? Collect(string errorText);
}
