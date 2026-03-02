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
            
            // 初始化 MetaDataManager：计算当前 LoadOrder Hash，备份或沿用上次会话的嫌疑 MOD 列表
            MetaDataManager.InitializeCurrentSession();

            // 动态挂载特殊的报错强化器，仅当存在目标 MOD 时开启正则校验以节约性能
            if (ModLister.GetActiveModWithIdentifier("ceteam.combatextended", true) != null)
            {
                ErrorCaptureHook.RegisterEnricher(new CombatExtendedErrorEnricher());
                Log.Message("[BetterModSort] Loaded specialized error enricher for Combat Extended.");
            }

            Log.Message("[BetterModSort] " + "BMS_Log_HarmonyPatchesApplied".TranslateSafe());
        }

        private void DrawLabeledTextEntry(Listing_Standard listing, string label, ref string value)
        {
            Rect rect = listing.GetRect(30f);
            Rect labelRect = new(rect.x, rect.y, rect.width * 0.25f, rect.height);
            Rect textRect = new(rect.x + rect.width * 0.25f, rect.y, rect.width * 0.75f, rect.height);
            
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            
            value = Widgets.TextField(textRect, value ?? "");
            listing.Gap(listing.verticalSpacing);
        }

        private void DrawLabeledTextEntryWithHint(Listing_Standard listing, string label, string hint, ref string value)
        {
            // Give extra height for the sublabel to render properly
            Rect rect = listing.GetRect(50f);
            Rect labelRect = new(rect.x, rect.y, rect.width * 0.25f, 30f);
            Rect textRect = new(rect.x + rect.width * 0.25f, rect.y, rect.width * 0.75f, 30f);
            
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            
            value = Widgets.TextField(textRect, value ?? "");
            // Draw the hint immediately below the text field, starting from the left edge
            Rect hintRect = new(rect.x, rect.y + 30f, rect.width, 20f);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(hintRect, hint);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            
            listing.Gap(listing.verticalSpacing);
        }

        private void DrawLabeledNumberEntry(Listing_Standard listing, string label, ref int value, int min = 0)
        {
            Rect rect = listing.GetRect(30f);
            Rect labelRect = new Rect(rect.x, rect.y, rect.width * 0.7f, rect.height);
            Rect textRect = new Rect(rect.x + rect.width * 0.7f, rect.y, rect.width * 0.3f, rect.height);
            
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            
            string buffer = value.ToString();
            buffer = Widgets.TextField(textRect, buffer);
            if (int.TryParse(buffer, out int parsed) && parsed >= min)
            {
                value = parsed;
            }
            listing.Gap(listing.verticalSpacing);
        }

        private void DrawLLMConfigBlock(Listing_Standard listing, LLMConfigData config, string providerLabelKey)
        {
            Rect rect = listing.GetRect(30f);
            Rect labelRect = new(rect.x, rect.y, rect.width * 0.25f, rect.height);
            Rect buttonRect = new(rect.x + rect.width * 0.25f, rect.y, rect.width * 0.75f, rect.height);

            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(labelRect, providerLabelKey.TranslateSafe());
            Text.Anchor = TextAnchor.UpperLeft;

            if (Widgets.ButtonText(buttonRect, $"BMS_Provider_{config.Provider}".TranslateSafe()))
            {
                var list = new List<FloatMenuOption>();
                foreach (LLMProvider provider in Enum.GetValues(typeof(LLMProvider)))
                {
                    LLMProvider currentProvider = provider; 
                    list.Add(new FloatMenuOption($"BMS_Provider_{currentProvider}".TranslateSafe(), () =>
                    {
                        config.Provider = currentProvider;
                        if (currentProvider == LLMProvider.OpenAI)
                        {
                            config.BaseUrl = ""; 
                            config.ModelName = "gpt-4o";
                        }
                        else if (currentProvider == LLMProvider.Anthropic)
                        {
                            config.BaseUrl = "";
                            config.ModelName = "claude-4-6-haiku";
                        }
                        else if (currentProvider == LLMProvider.Gemini)
                        {
                            config.BaseUrl = "";
                            config.ModelName = "gemini-3-flash-preview";
                        }
                        else if (currentProvider == LLMProvider.DeepSeek)
                        {
                            config.BaseUrl = "";
                            config.ModelName = "deepseek-chat";
                        }
                        else if (currentProvider == LLMProvider.SiliconFlow)
                        {
                            config.BaseUrl = "";
                            config.ModelName = "deepseek-ai/DeepSeek-V3.2";
                        }
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }
            listing.Gap(listing.verticalSpacing);

            DrawLabeledTextEntry(listing, "BMS_Settings_LabelApiKey".TranslateSafe(), ref config.ApiKey);
            string defaultUrl = "https://api.openai.com/v1/chat/completions";
            string baseUrlHintSuffix = "/chat/completions";
            if (config.Provider == LLMProvider.Anthropic) { defaultUrl = "https://api.anthropic.com/v1/messages"; baseUrlHintSuffix = "/v1/messages"; }
            else if (config.Provider == LLMProvider.Gemini) { defaultUrl = "https://generativelanguage.googleapis.com/v1beta/"; baseUrlHintSuffix = "/v1beta/"; }
            else if (config.Provider == LLMProvider.DeepSeek) { defaultUrl = "https://api.deepseek.com/chat/completions"; baseUrlHintSuffix = "/chat/completions"; }
            else if (config.Provider == LLMProvider.SiliconFlow) { defaultUrl = "https://api.siliconflow.cn/v1/chat/completions"; baseUrlHintSuffix = "/chat/completions"; }

            string hintStr = string.IsNullOrWhiteSpace(config.BaseUrl)
                ? "BMS_Settings_LabelBaseUrlHint_Default".TranslateSafe(defaultUrl)
                : "BMS_Settings_LabelBaseUrlHint_Custom".TranslateSafe(baseUrlHintSuffix);

            DrawLabeledTextEntryWithHint(listing, "BMS_Settings_LabelBaseUrl".TranslateSafe(), hintStr, ref config.BaseUrl);
            DrawLabeledTextEntry(listing, "BMS_Settings_LabelModelName".TranslateSafe(), ref config.ModelName);
            DrawLabeledNumberEntry(listing, "BMS_Settings_LabelMaxTokens".TranslateSafe(), ref config.MaxTokens, 0);
        }

        private Vector2 _scrollPosition = Vector2.zero;
        private float _scrollViewHeight = 1000f;

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (Settings.MainLLM == null) Settings.MainLLM = new LLMConfigData { Provider = LLMProvider.OpenAI, ModelName = "gpt-4o" };
            if (Settings.SummaryLLM == null) Settings.SummaryLLM = new LLMConfigData { Provider = LLMProvider.Gemini, ModelName = "gemini-3.0-flash" };

            Rect outRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
            // viewRect acts as the scroll boundary for the scrollbar
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, _scrollViewHeight);

            Widgets.BeginScrollView(outRect, ref _scrollPosition, viewRect, true);

            Listing_Standard listing = new();
            listing.Begin(new Rect(0f, 0f, viewRect.width, 99999f));

            // ========== AI 连接配置 ==========
            listing.Label("BMS_Settings_SectionAI".TranslateSafe());
            listing.GapLine();

            DrawLLMConfigBlock(listing, Settings.MainLLM, "BMS_Settings_Provider");

            listing.Gap();
            listing.CheckboxLabeled("BMS_Settings_UseSeparateSummaryModel".TranslateSafe(), ref Settings.UseSeparateSummaryModel, "BMS_Settings_UseSeparateSummaryModelDesc".TranslateSafe());
            if (Settings.UseSeparateSummaryModel)
            {
                listing.Gap();
                listing.Label("BMS_Settings_SummaryProviderInfo".TranslateSafe());
                
                listing.ColumnWidth -= 20f;
                listing.Indent(20f);
                
                DrawLLMConfigBlock(listing, Settings.SummaryLLM, "BMS_Settings_SummaryProvider");
                
                listing.Gap();
                DrawLabeledNumberEntry(listing, "BMS_Settings_LabelMaxConcurrentSummaryRequests".TranslateSafe(), ref Settings.MaxConcurrentSummaryRequests, 1);
                
                listing.Outdent(20f);
                listing.ColumnWidth += 20f;
            }

            listing.Gap();

            // ========== 实验性功能 ==========
            listing.Label("BMS_Settings_SectionExperimental".TranslateSafe());
            listing.GapLine();

            listing.CheckboxLabeled("BMS_Settings_EnableAISorting".TranslateSafe(), ref Settings.EnableAISorting,
                "BMS_Settings_EnableAISortingDesc".TranslateSafe());
            
            listing.CheckboxLabeled("BMS_Settings_EnableRimSortExport".TranslateSafe(), ref Settings.EnableRimSortExport,
                "BMS_Settings_EnableRimSortExportDesc".TranslateSafe());
            
            DrawLabeledNumberEntry(listing, "BMS_Settings_LabelErrorLogMaxChars".TranslateSafe(), ref Settings.ErrorLogMaxChars, 1);
            DrawLabeledNumberEntry(listing, "BMS_Settings_LabelShortDescMaxChars".TranslateSafe(), ref Settings.ShortDescMaxChars, 1);
            DrawLabeledNumberEntry(listing, "BMS_Settings_LabelShortDescBypassThreshold".TranslateSafe(), ref Settings.ShortDescBypassThreshold, 0);
            DrawLabeledNumberEntry(listing, "BMS_Settings_LabelLLMTimeout".TranslateSafe(), ref Settings.LLMTimeoutSeconds, 1);

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
            _scrollViewHeight = listing.CurHeight;
            Widgets.EndScrollView();

            base.DoSettingsWindowContents(inRect);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
        }

        public override string SettingsCategory()
        {
            return "Better Mod Sort";
        }
    }
}
