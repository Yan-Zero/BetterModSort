using System;
using System.Collections.Generic;
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
            LLMClient.Provider = Settings.Provider;
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

            listing.Label("BMS_Settings_Provider".TranslateSafe() + ": " + $"BMS_Provider_{Settings.Provider}".TranslateSafe());
            if (listing.ButtonText($"BMS_Provider_{Settings.Provider}".TranslateSafe()))
            {
                var list = new List<FloatMenuOption>();
                foreach (LLMProvider provider in Enum.GetValues(typeof(LLMProvider)))
                {
                    LLMProvider currentProvider = provider; // captures local copy
                    list.Add(new FloatMenuOption($"BMS_Provider_{currentProvider}".TranslateSafe(), () =>
                    {
                        Settings.Provider = currentProvider;
                        if (currentProvider == LLMProvider.OpenAI)
                        {
                            Settings.BaseUrl = ""; // Default handled in LLMClient
                            Settings.ModelName = "gpt-4o";
                        }
                        else if (currentProvider == LLMProvider.Anthropic)
                        {
                            Settings.BaseUrl = "";
                            Settings.ModelName = "claude-4-6-haiku";
                        }
                        else if (currentProvider == LLMProvider.Gemini)
                        {
                            Settings.BaseUrl = "";
                            Settings.ModelName = "gemini-3-flash-preview";
                        }
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }
            listing.Gap();

            listing.Label("BMS_Settings_LabelApiKey".TranslateSafe());
            Settings.ApiKey = listing.TextEntry(Settings.ApiKey);
            
            string baseUrlHintSuffix = "/chat/completions";
            if (Settings.Provider == LLMProvider.Anthropic) baseUrlHintSuffix = "/v1/messages";
            else if (Settings.Provider == LLMProvider.Gemini) baseUrlHintSuffix = "/v1beta/";

            listing.Label("BMS_Settings_LabelBaseUrl".TranslateSafe(baseUrlHintSuffix));
            Settings.BaseUrl = listing.TextEntry(Settings.BaseUrl);

            listing.Label("BMS_Settings_LabelModelName".TranslateSafe());
            Settings.ModelName = listing.TextEntry(Settings.ModelName);

            listing.Gap();

            // ========== 实验性功能 ==========
            listing.Label("BMS_Settings_SectionExperimental".TranslateSafe());
            listing.GapLine();

            listing.CheckboxLabeled("BMS_Settings_EnableAISorting".TranslateSafe(), ref Settings.EnableAISorting,
                "BMS_Settings_EnableAISortingDesc".TranslateSafe());
            

            listing.Label("BMS_Settings_LabelErrorLogMaxChars".TranslateSafe());
            string errorLogMaxStr = Settings.ErrorLogMaxChars.ToString();
            errorLogMaxStr = listing.TextEntry(errorLogMaxStr);
            if (int.TryParse(errorLogMaxStr, out int parsedErrMax) && parsedErrMax > 0)
                Settings.ErrorLogMaxChars = parsedErrMax;

            listing.Label("BMS_Settings_LabelShortDescMaxChars".TranslateSafe());
            string shortDescMaxStr = Settings.ShortDescMaxChars.ToString();
            shortDescMaxStr = listing.TextEntry(shortDescMaxStr);
            if (int.TryParse(shortDescMaxStr, out int parsedDescMax) && parsedDescMax > 0)
                Settings.ShortDescMaxChars = parsedDescMax;

            listing.Label("BMS_Settings_LabelShortDescBypassThreshold".TranslateSafe());
            string shortDescBypassStr = Settings.ShortDescBypassThreshold.ToString();
            shortDescBypassStr = listing.TextEntry(shortDescBypassStr);
            if (int.TryParse(shortDescBypassStr, out int parsedBypass) && parsedBypass >= 0)
                Settings.ShortDescBypassThreshold = parsedBypass;

            listing.Label("BMS_Settings_LabelLLMTimeout".TranslateSafe());
            string timeoutStr = Settings.LLMTimeoutSeconds.ToString();
            timeoutStr = listing.TextEntry(timeoutStr);
            if (int.TryParse(timeoutStr, out int parsedTimeout) && parsedTimeout > 0)
                Settings.LLMTimeoutSeconds = parsedTimeout;

            listing.Gap();

            // ========== 调试选项 ==========
            listing.Label("BMS_Settings_SectionDebug".TranslateSafe());
            listing.GapLine();

            listing.CheckboxLabeled("BMS_Settings_EnableDebugDump".TranslateSafe(), ref Settings.EnableDebugDump,
                "BMS_Settings_EnableDebugDumpDesc".TranslateSafe());

            string rootDir = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "BetterModSort");
            if (listing.ButtonText("BMS_Settings_OpenDumpFolder".TranslateSafe()))
            {
                
                if (!System.IO.Directory.Exists(rootDir))
                    System.IO.Directory.CreateDirectory(rootDir);
                // 使用跨平台方式打开文件夹
                Application.OpenURL(rootDir);
            }
            listing.SubLabel(rootDir, 0.6f);

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
