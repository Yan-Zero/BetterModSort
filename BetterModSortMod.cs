using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using BetterModSort.AI;

namespace BetterModSort
{
    public class BetterModSortMod : Mod
    {
        public static Harmony? HarmonyInstance { get; private set; }
        public static BetterModSortSettings Settings { get; private set; } = null!;

        public BetterModSortMod(ModContentPack content)
            : base(content)
        {
            Settings = GetSettings<BetterModSortSettings>();
            HarmonyInstance = new Harmony("com.bettermodsort.rimworld");
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            
            // 初始化 LLMClient 参数
            SyncLLMClientSettings();

            Log.Message("[BetterModSort] Harmony patches applied.");
        }

        private static void SyncLLMClientSettings()
        {
            LLMClient.BaseUrl = Settings.BaseUrl;
            LLMClient.ApiKey = Settings.ApiKey;
            LLMClient.ModelName = Settings.ModelName;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            // ========== AI 连接配置 ==========
            listing.Label("BMS_Settings_SectionAI".Translate());
            listing.GapLine();

            listing.Label("BMS_Settings_LabelApiKey".Translate());
            Settings.ApiKey = listing.TextEntry(Settings.ApiKey);
            
            listing.Label("BMS_Settings_LabelBaseUrl".Translate());
            Settings.BaseUrl = listing.TextEntry(Settings.BaseUrl);

            listing.Label("BMS_Settings_LabelModelName".Translate());
            Settings.ModelName = listing.TextEntry(Settings.ModelName);

            listing.Gap();

            // ========== 实验性功能 ==========
            listing.Label("BMS_Settings_SectionExperimental".Translate());
            listing.GapLine();

            listing.CheckboxLabeled("BMS_Settings_EnableAISorting".Translate(), ref Settings.EnableAISorting,
                "BMS_Settings_EnableAISortingDesc".Translate());
            
            listing.Gap();

            // ========== 调试选项 ==========
            listing.Label("BMS_Settings_SectionDebug".Translate());
            listing.GapLine();

            listing.CheckboxLabeled("BMS_Settings_EnableDebugDump".Translate(), ref Settings.EnableDebugDump,
                "BMS_Settings_EnableDebugDumpDesc".Translate());

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            SyncLLMClientSettings();
        }

        public override string SettingsCategory()
        {
            return "Better Mod Sort";
        }
    }
}
