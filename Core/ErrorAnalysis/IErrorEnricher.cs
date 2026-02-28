namespace BetterModSort.Core.ErrorAnalysis;

/// <summary>
/// 错误文本强化器接口。Priority 越小优先级越高，默认 100。
/// </summary>
public interface IErrorEnricher
{
    /// <summary>
    /// 优先级，数值越小越优先执行。专用 enricher 建议 50，通用兜底建议 200。
    /// </summary>
    int Priority => 100;

    bool CanEnrich(string errorText);
    string? Enrich(string errorText);
}
