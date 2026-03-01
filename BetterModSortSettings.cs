using Verse;
using BetterModSort.AI;

namespace BetterModSort
{
    public enum LLMProvider
    {
        OpenAI,
        Anthropic,
        Gemini,
        DeepSeek,
        SiliconFlow
    }

    public class BetterModSortSettings : ModSettings
    {
        public LLMConfigData MainLLM = new LLMConfigData { Provider = LLMProvider.OpenAI, ModelName = "gpt-4o" };
        
        public bool UseSeparateSummaryModel = false;
        public LLMConfigData SummaryLLM = new LLMConfigData { Provider = LLMProvider.Gemini, ModelName = "gemini-3.0-flash" };
        public int MaxConcurrentSummaryRequests = 5;

        /// <summary>
        /// 实验性功能：启用 AI 辅助排序（替代原版自动排序按钮逻辑）
        /// </summary>
        public bool EnableAISorting = false;

        /// <summary>
        /// 调试模式：开启后在文件中 Dump LLM 的原始请求和响应
        /// </summary>
        public bool EnableDebugDump = false;

        /// <summary>
        /// 发送给 AI 的报错日志最大字符数
        /// </summary>
        public int ErrorLogMaxChars = 8000;

        /// <summary>
        /// 提炼 MOD 短描述时截取原始描述的最大字符数
        /// </summary>
        public int ShortDescMaxChars = 2500;

        /// <summary>
        /// 低于此字数的原始描述将直接跳过大模型提炼（原文直出）
        /// </summary>
        public int ShortDescBypassThreshold = 200;

        /// <summary>
        /// LLM 请求超时时间（秒）
        /// </summary>
        public int LLMTimeoutSeconds = 600;

        /// <summary>
        /// LLM 最大生成 Token 数
        /// </summary>
        public int MaxTokens = 8192;

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref MainLLM, "MainLLM");
            Scribe_Values.Look(ref UseSeparateSummaryModel, "UseSeparateSummaryModel", false);
            Scribe_Deep.Look(ref SummaryLLM, "SummaryLLM");
            Scribe_Values.Look(ref MaxConcurrentSummaryRequests, "MaxConcurrentSummaryRequests", 5);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (MainLLM == null) MainLLM = new LLMConfigData { Provider = LLMProvider.OpenAI, ModelName = "gpt-4o" };
                if (SummaryLLM == null) SummaryLLM = new LLMConfigData { Provider = LLMProvider.Gemini, ModelName = "gemini-3.0-flash" };
            }

            Scribe_Values.Look(ref EnableAISorting, "EnableAISorting", false);
            Scribe_Values.Look(ref EnableDebugDump, "EnableDebugDump", false);
            Scribe_Values.Look(ref ErrorLogMaxChars, "ErrorLogMaxChars", 8000);
            Scribe_Values.Look(ref ShortDescMaxChars, "ShortDescMaxChars", 2500);
            Scribe_Values.Look(ref ShortDescBypassThreshold, "ShortDescBypassThreshold", 200);
            Scribe_Values.Look(ref LLMTimeoutSeconds, "LLMTimeoutSeconds", 600);
            Scribe_Values.Look(ref MaxTokens, "MaxTokens", 8192);
            base.ExposeData();
        }
    }
}
