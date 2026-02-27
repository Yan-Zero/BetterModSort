using Verse;

namespace BetterModSort
{
    public class BetterModSortSettings : ModSettings
    {
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

        public override void ExposeData()
        {
            Scribe_Values.Look(ref ApiKey, "ApiKey", "");
            Scribe_Values.Look(ref BaseUrl, "BaseUrl", "https://api.openai.com/v1/chat/completions");
            Scribe_Values.Look(ref ModelName, "ModelName", "gpt-4o");
            Scribe_Values.Look(ref EnableAISorting, "EnableAISorting", false);
            Scribe_Values.Look(ref EnableDebugDump, "EnableDebugDump", false);
            base.ExposeData();
        }
    }
}
