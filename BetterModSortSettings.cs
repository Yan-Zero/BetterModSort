using Verse;

namespace BetterModSort
{
    public enum LLMProvider
    {
        OpenAI,
        Anthropic,
        Gemini
    }

    public class BetterModSortSettings : ModSettings
    {
        public LLMProvider Provider = LLMProvider.OpenAI;
        public string ApiKey = "";
        public string BaseUrl = "https://api.openai.com/v1/chat/completions";
        public string ModelName = "gpt-4o";

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

        public override void ExposeData()
        {
            Scribe_Values.Look(ref Provider, "Provider", LLMProvider.OpenAI);
            Scribe_Values.Look(ref ApiKey, "ApiKey", "");
            Scribe_Values.Look(ref BaseUrl, "BaseUrl", "https://api.openai.com/v1/chat/completions");
            Scribe_Values.Look(ref ModelName, "ModelName", "gpt-4o");
            Scribe_Values.Look(ref EnableAISorting, "EnableAISorting", false);
            Scribe_Values.Look(ref EnableDebugDump, "EnableDebugDump", false);
            Scribe_Values.Look(ref ErrorLogMaxChars, "ErrorLogMaxChars", 8000);
            Scribe_Values.Look(ref ShortDescMaxChars, "ShortDescMaxChars", 2500);
            Scribe_Values.Look(ref ShortDescBypassThreshold, "ShortDescBypassThreshold", 200);
            Scribe_Values.Look(ref LLMTimeoutSeconds, "LLMTimeoutSeconds", 600);
            base.ExposeData();
        }
    }
}
