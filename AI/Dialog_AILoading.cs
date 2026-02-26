using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using RimWorld;
using Newtonsoft.Json;
using BetterModSort.Hooks;

namespace BetterModSort.AI
{
    public class SoftConstraintInfo
    {
        public string? PackageId;
        public List<string>? LoadBefore;
        public List<string>? LoadAfter;
    }

    public class SoftConstraintResponse
    {
        [JsonProperty("constraints")]
        public List<SoftConstraintInfo>? Constraints;
    }

    public class Dialog_AILoading : Window
    {
        private string _statusText = "";
        private Task<string>? _aiTask;
        private bool _completed = false;
        private bool _failed = false;

        public override Vector2 InitialSize => new Vector2(400f, 150f);

        public Dialog_AILoading()
        {
            this.forcePause = true;
            this.absorbInputAroundWindow = true;
            this.closeOnClickedOutside = false;
            this.closeOnAccept = false;
            this.closeOnCancel = false;
        }

        public override void PreOpen()
        {
            base.PreOpen();
            _statusText = "BMS_AILoading_Requesting".Translate();
            // StartAIRequestAsync 自己管理 _aiTask 或者我们用一个包装 task
            _aiTask = StartAIRequestAsync();
        }

        private async Task<string> StartAIRequestAsync()
        {
            var activeMods = ModsConfig.ActiveModsInLoadOrder.ToList();
            
            // 1. 获取本次（或跨次）嫌疑 MOD 列表
            var suspectIds = MetaDataManager.GetSuspectPackageIds();
            var suspectShortDescs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var packageId in suspectIds)
            {
                var mod = activeMods.FirstOrDefault(m => string.Equals(m.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
                if (mod == null) continue;

                string rawDesc = mod.Description ?? "";
                
                // 2. 尝试拿本地提炼缓存
                if (MetaDataManager.TryGetShortDesc(packageId, rawDesc, out string shortDesc))
                    suspectShortDescs[packageId] = shortDesc;
                else
                {
                    // 3. 缓存失效或不存在，小请求 AI 提炼
                    _statusText = "BMS_AILoading_AnalyzingDesc".Translate(mod.Name);
                    string promptShort = PromptBuilder.BuildShortDescPrompt(mod.PackageId, mod.Name, rawDesc);
                    try
                    {
                        string aiShortResult = await LLMClient.SendChatRequestAsync(promptShort, expectJsonFormat: false);
                        if (!string.IsNullOrWhiteSpace(aiShortResult))
                        {
                            MetaDataManager.SaveShortDesc(packageId, rawDesc, aiShortResult.Trim());
                            suspectShortDescs[packageId] = aiShortResult.Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[BetterModSort] 无法提炼 {packageId} 的短描述: {ex.Message}");
                    }
                }
            }

            // 4. 读取错误日志
            _statusText = "BMS_AILoading_Requesting".Translate();
            string errorLogContent = "";
            try
            {
                if (System.IO.File.Exists(ErrorCaptureHook.ErrorLogFilePath))
                    errorLogContent = System.IO.File.ReadAllText(ErrorCaptureHook.ErrorLogFilePath);
            }
            catch { }

            // 5. 组合终极 Prompt
            string prompt = PromptBuilder.BuildSortingSoftConstraintsPrompt(activeMods, errorLogContent, suspectShortDescs);
            
            // 返回大请求的 Task
            return await LLMClient.SendChatRequestAsync(prompt, expectJsonFormat: true);
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();

            if (_aiTask != null && _aiTask.IsCompleted && !_completed)
            {
                _completed = true;
                if (_aiTask.IsCanceled)
                {
                    _statusText = "BMS_AILoading_Timeout".Translate();
                    _failed = true;
                }
                else if (_aiTask.IsFaulted)
                {
                    Log.Error("[BetterModSort] LLM 请求崩溃抛出底层异常:\n" + _aiTask.Exception?.ToString());
                    _statusText = "BMS_AILoading_FaultedStatus".Translate();
                    _failed = true;
                }
                else
                {
                    try
                    {
                        string json = _aiTask.Result;
                        json = ExtractInnerJson(json);

                        var constraintsResponse = JsonConvert.DeserializeObject<SoftConstraintResponse>(json);
                        ApplyConstraintsAndSort(constraintsResponse?.Constraints);
                        this.Close();
                        Messages.Message("BMS_AILoading_SortDone".Translate(), MessageTypeDefOf.PositiveEvent, false);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[BetterModSort] 解析 AI 返回数据失败: " + ex);
                        _statusText = "BMS_AILoading_ParseFailed".Translate();
                        _failed = true;
                    }
                }
            }
        }

        private string ExtractInnerJson(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "{}";
            int startIdx = raw.IndexOf('{');
            int endIdx = raw.LastIndexOf('}');
            if (startIdx >= 0 && endIdx >= startIdx)
            {
                return raw.Substring(startIdx, endIdx - startIdx + 1);
            }
            return raw;
        }

        private void ApplyConstraintsAndSort(List<SoftConstraintInfo>? constraints)
        {
            if (constraints == null) return;

            var allMods = ModsConfig.ActiveModsInLoadOrder.ToList();

            foreach (var constraint in constraints)
            {
                if (string.IsNullOrEmpty(constraint.PackageId)) continue;
                
                var mod = allMods.FirstOrDefault(m => m.PackageId.Equals(constraint.PackageId, StringComparison.OrdinalIgnoreCase));
                if (mod != null)
                {
                    // 给原版 ForceLoadBefore 和 ForceLoadAfter 添加通过 AI 生成的软约束依赖
                    if (constraint.LoadBefore != null)
                    {
                        foreach (var target in constraint.LoadBefore)
                        {
                            if (!mod.ForceLoadBefore.Contains(target))
                                mod.ForceLoadBefore.Add(target);
                        }
                    }

                    if (constraint.LoadAfter != null)
                    {
                        foreach (var target in constraint.LoadAfter)
                        {
                            if (!mod.ForceLoadAfter.Contains(target))
                                mod.ForceLoadAfter.Add(target);
                        }
                    }
                }
            }

            // 标记，让 Prefix 能够放行
            ModsConfig_TrySortMods_Patch.ExecuteVanillaSort = true;
            try
            {
                ModsConfig.TrySortMods();
            }
            finally
            {
                ModsConfig_TrySortMods_Patch.ExecuteVanillaSort = false;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            Widgets.Label(inRect, _statusText);

            if (_failed)
            {
                Rect closeBtnRect = new Rect(inRect.width / 2 - 50f, inRect.height - 30f, 100f, 30f);
                if (Widgets.ButtonText(closeBtnRect, "BMS_AILoading_BtnClose".Translate(), true, false, true))
                {
                    Close();
                }
            }
            else
            {
                // 等待动画
                int dots = (int)(Time.realtimeSinceStartup * 2f) % 4;
                string dotStr = new('.', dots);
                Rect dotsRect = new(0, inRect.height / 2 + 20f, inRect.width, 30f);
                Widgets.Label(dotsRect, "BMS_AILoading_Waiting".Translate() + dotStr);
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
