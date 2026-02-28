using HarmonyLib;
using Verse;

namespace BetterModSort.Hooks
{
    /// <summary>
    /// 带异常的错误捕获
    /// </summary>
    [HarmonyPatch(typeof(Log), "ErrorOnce")]
    public static class Log_ErrorOnce_Patch
    {
        public static void Prefix(ref string text, out bool __state)
        {
            __state = false;
            try
            {
                var enriched = ErrorCaptureHook.TryEnrichErrorText(text);
                if (enriched != null)
                {
                    text = enriched;
                    __state = true;
                }
            }
            catch { }
        }

        public static void Postfix(string text, bool __state)
        {
            try { ErrorCaptureHook.OnErrorCaptured(text, null, __state); }
            catch { }
        }
    }

    /// <summary>
    /// 错误捕获 Hook - 自动捕获错误并输出对应的 DLL 和 MOD 信息
    /// </summary>
    [HarmonyPatch(typeof(Log), nameof(Log.Error))]
    public static class Log_Error_Patch
    {
        public static void Prefix(ref string text, out bool __state)
        {
            __state = false;
            try
            {
                var enriched = ErrorCaptureHook.TryEnrichErrorText(text);
                if (enriched != null)
                {
                    text = enriched;
                    __state = true;
                }
            }
            catch { }
        }

        public static void Postfix(string text, bool __state)
        {
            try { ErrorCaptureHook.OnErrorCaptured(text, null, __state); }
            catch { }
        }
    }
}
