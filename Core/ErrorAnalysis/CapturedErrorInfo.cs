using BetterModSort.Tools;

namespace BetterModSort.Core.ErrorAnalysis
{
    public class CapturedErrorInfo
    {
        public string? ErrorMessage { get; set; }
        public DateTime CapturedTime { get; set; }
        public string? StackTraceText { get; set; }
        public List<ModDllInfo> RelatedMods { get; set; } = [];

        public override string ToString()
        {
            return $"[{CapturedTime:HH:mm:ss}] {TruncateString(ErrorMessage ?? "", 50)} - MODs: {string.Join(", ", RelatedMods.Select(m => m.ModName))}";
        }

        private static string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
                return str;
            int len = maxLength;
            if (char.IsHighSurrogate(str[len - 1])) len--;
            return str[..len] + "...";
        }
    }
}
