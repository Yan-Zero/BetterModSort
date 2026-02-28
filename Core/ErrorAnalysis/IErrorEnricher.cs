namespace BetterModSort.Core.ErrorAnalysis;

public interface IErrorEnricher
{
    bool CanEnrich(string errorText);
    string? Enrich(string errorText);
}
