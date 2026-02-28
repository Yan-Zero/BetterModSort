using HarmonyLib;
using Verse;

namespace BetterModSort.Hooks
{
    public static class ScribeSourceTracker
    {
        private static string? _currentFilePath;

        public static string? CurrentFilePath
        {
            get
            {
                if (Scribe.loader?.curXmlParent == null)
                    return null;
                return _currentFilePath;
            }
        }

        internal static void SetFilePath(string filePath)
        {
            _currentFilePath = filePath;
        }
    }

    [HarmonyPatch(typeof(ScribeLoader), nameof(ScribeLoader.InitLoading))]
    public static class Patch_ScribeLoader_InitLoading
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(string filePath)
        {
            ScribeSourceTracker.SetFilePath(filePath);
        }
    }

    [HarmonyPatch(typeof(ScribeLoader), nameof(ScribeLoader.InitLoadingMetaHeaderOnly))]
    public static class Patch_ScribeLoader_InitLoadingMetaHeaderOnly
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(string filePath)
        {
            ScribeSourceTracker.SetFilePath(filePath);
        }
    }
}
