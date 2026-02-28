using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using BetterModSort.AI;
using BetterModSort.Tools;
using BetterModSort.Hooks;
using BetterModSort.Core.ErrorAnalysis.Enrichers;

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

            // 初始化 MetaDataManager：计算当前 LoadOrder Hash，备份或沿用上次会话的嫌疑 MOD 列表
            AI.MetaDataManager.InitializeCurrentSession();

            // 动态挂载特殊的报错强化器，仅当存在目标 MOD 时开启正则校验以节约性能
            if (ModLister.GetActiveModWithIdentifier("ceteam.combatextended", true) != null)
            {
                ErrorCaptureHook.RegisterEnricher(new CombatExtendedErrorEnricher());
                Log.Message("[BetterModSort] Loaded specialized error enricher for Combat Extended.");
            }

            Log.Message("[BetterModSort] " + "BMS_Log_HarmonyPatchesApplied".TranslateSafe());
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
            listing.Label("BMS_Settings_SectionAI".TranslateSafe());
            listing.GapLine();

            listing.Label("BMS_Settings_LabelApiKey".TranslateSafe());
            Settings.ApiKey = listing.TextEntry(Settings.ApiKey);
            
            listing.Label("BMS_Settings_LabelBaseUrl".TranslateSafe());
            Settings.BaseUrl = listing.TextEntry(Settings.BaseUrl);

            listing.Label("BMS_Settings_LabelModelName".TranslateSafe());
            Settings.ModelName = listing.TextEntry(Settings.ModelName);

            listing.Gap();

            // ========== 实验性功能 ==========
            listing.Label("BMS_Settings_SectionExperimental".TranslateSafe());
            listing.GapLine();

            listing.CheckboxLabeled("BMS_Settings_EnableAISorting".TranslateSafe(), ref Settings.EnableAISorting,
                "BMS_Settings_EnableAISortingDesc".TranslateSafe());
            
            listing.Gap();

            // ========== 调试选项 ==========
            listing.Label("BMS_Settings_SectionDebug".TranslateSafe());
            listing.GapLine();

            listing.CheckboxLabeled("BMS_Settings_EnableDebugDump".TranslateSafe(), ref Settings.EnableDebugDump,
                "BMS_Settings_EnableDebugDumpDesc".TranslateSafe());

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
