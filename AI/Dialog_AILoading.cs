using BetterModSort.Core.ErrorAnalysis;
using BetterModSort.Hooks;
using BetterModSort.Tools;
using Newtonsoft.Json;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterModSort.AI
{
    public class SoftConstraintInfo
    {
        public string? PackageId;
        public List<string>? LoadBefore;
        public List<string>? LoadAfter;
        public List<string>? IncompatibleWith;
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
            _statusText = "BMS_AILoading_Requesting".TranslateSafe();
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
                    // 3. 缓存失效或不存在，小请求 AI
                    try
                    {
                        // 如果原始描述很短，直接跳过 AI 提炼使用原文
                        if (rawDesc.Length <= BetterModSortMod.Settings.ShortDescBypassThreshold)
                        {
                            suspectShortDescs[packageId] = rawDesc;
                            MetaDataManager.SaveShortDesc(packageId, rawDesc, rawDesc);
                            continue;
                        }

                        _statusText = "BMS_AILoading_AnalyzingDesc".TranslateSafe(mod.Name);
                        string promptForDesc = PromptBuilder.BuildShortDescPrompt(mod.PackageId, mod.Name, rawDesc);
                        string aiShortResult = await LLMClient.SendChatRequestAsync(promptForDesc, expectJsonFormat: false);
                        
                        if (!string.IsNullOrWhiteSpace(aiShortResult))
                        {
                            string finalDesc = aiShortResult.Trim();
                            // 如果 AI 生成的内容不仅没缩短，反而比原文还长，那就直接弃用 AI 生成的，改用原文。
                            if (finalDesc.Length >= rawDesc.Length)
                            {
                                finalDesc = rawDesc;
                            }
                            MetaDataManager.SaveShortDesc(packageId, rawDesc, finalDesc);
                            suspectShortDescs[packageId] = finalDesc;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[BetterModSort] " + "BMS_Log_AILoadingExtractFailed".TranslateSafe(packageId, ex.Message));
                    }
                }
            }

            // 4. 读取错误日志
            _statusText = "BMS_AILoading_Requesting".TranslateSafe();
            string errorLogContent = "";
            try
            {
                if (File.Exists(ErrorHistoryManager.ErrorLogFilePath))
                    errorLogContent = File.ReadAllText(ErrorHistoryManager.ErrorLogFilePath);
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
                    _statusText = "BMS_AILoading_Timeout".TranslateSafe();
                    _failed = true;
                }
                else if (_aiTask.IsFaulted)
                {
                    Log.Error("[BetterModSort] " + "BMS_Log_AILoadingException".TranslateSafe(_aiTask.Exception?.ToString() ?? ""));
                    _statusText = "BMS_AILoading_FaultedStatus".TranslateSafe();
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
                        Messages.Message("BMS_AILoading_SortDone".TranslateSafe(), MessageTypeDefOf.PositiveEvent, false);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[BetterModSort] " + "BMS_Log_AILoadingParseFailed".TranslateSafe(ex.ToString()));
                        _statusText = "BMS_AILoading_ParseFailed".TranslateSafe();
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

            // 1. 构建 packageId → index 映射
            var idToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < allMods.Count; i++)
                idToIndex[allMods[i].PackageId] = i;

            // 2. 用邻接表构建当前依赖图（包含原版硬约束）
            //    边 from → to 表示 from 必须排在 to 前面
            var adj = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < allMods.Count; i++)
                adj[i] = new HashSet<int>();

            for (int i = 0; i < allMods.Count; i++)
            {
                var mod = allMods[i];
                // LoadBefore: 本 mod 应排在 target 之前 → 边 target → i (target 在 i 后面)
                foreach (var before in mod.LoadBefore.Concat(mod.ForceLoadBefore))
                {
                    if (idToIndex.TryGetValue(before, out int targetIdx))
                        adj[targetIdx].Add(i);
                }
                // LoadAfter: 本 mod 应排在 target 之后 → 边 i → target (i 在 target 后面)
                foreach (var after in mod.LoadAfter.Concat(mod.ForceLoadAfter))
                {
                    if (idToIndex.TryGetValue(after, out int targetIdx))
                        adj[i].Add(targetIdx);
                }
            }

            // 3. 逐条验证 AI 约束，只注入不会产生环的
            int skipped = 0;
            foreach (var constraint in constraints)
            {
                if (string.IsNullOrEmpty(constraint.PackageId)) continue;
                if (!idToIndex.TryGetValue(constraint.PackageId!, out int modIdx)) continue;
                var mod = allMods[modIdx];

                // LoadBefore: 本 mod 应排在 target 之前
                // 等价于添加边 targetIdx → modIdx（target 在 mod 后面）
                if (constraint.LoadBefore != null)
                    foreach (var target in constraint.LoadBefore)
                    {
                        if (!idToIndex.TryGetValue(target, out int targetIdx)) continue;
                        if (adj[targetIdx].Contains(modIdx)) continue; // 已存在
                        // 如果从 modIdx 可以到达 targetIdx，加边 targetIdx → modIdx 会成环
                        if (CanReach(adj, modIdx, targetIdx))
                        {
                            skipped++;
                            Log.Warning($"[BetterModSort] AI constraint skipped (would create cycle): {constraint.PackageId} loadBefore {target}");
                            continue;
                        }
                        adj[targetIdx].Add(modIdx);
                        if (!mod.ForceLoadBefore.Contains(target))
                            mod.ForceLoadBefore.Add(target);
                    }

                // LoadAfter: 本 mod 应排在 target 之后
                // 等价于添加边 modIdx → targetIdx（mod 在 target 后面）
                if (constraint.LoadAfter != null)
                    foreach (var target in constraint.LoadAfter)
                    {
                        if (!idToIndex.TryGetValue(target, out int targetIdx)) continue;
                        if (adj[modIdx].Contains(targetIdx)) continue; // 已存在
                        // 如果从 targetIdx 可以到达 modIdx，加边 modIdx → targetIdx 会成环
                        if (CanReach(adj, targetIdx, modIdx))
                        {
                            skipped++;
                            Log.Warning($"[BetterModSort] AI constraint skipped (would create cycle): {constraint.PackageId} loadAfter {target}");
                            continue;
                        }
                        adj[modIdx].Add(targetIdx);
                        if (!mod.ForceLoadAfter.Contains(target))
                            mod.ForceLoadAfter.Add(target);
                    }

                if (constraint.IncompatibleWith != null)
                    foreach (var target in constraint.IncompatibleWith)
                        if (!mod.IncompatibleWith.Contains(target))
                            mod.IncompatibleWith.Add(target);
            }

            if (skipped > 0)
                Log.Warning($"[BetterModSort] {skipped} AI constraint(s) were skipped to prevent cyclic dependencies.");

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

        /// <summary>
        /// BFS 检查在邻接表 adj 中从 source 是否可达 destination。
        /// 用于判断添加一条反向边是否会产生环。
        /// </summary>
        private static bool CanReach(Dictionary<int, HashSet<int>> adj, int source, int destination)
        {
            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(source);
            visited.Add(source);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (current == destination) return true;
                if (!adj.TryGetValue(current, out var neighbors)) continue;
                foreach (int next in neighbors)
                    if (visited.Add(next))
                        queue.Enqueue(next);
            }
            return false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;

            Widgets.Label(inRect, _statusText);

            if (_failed)
            {
                Rect closeBtnRect = new Rect(inRect.width / 2 - 50f, inRect.height - 30f, 100f, 30f);
                if (Widgets.ButtonText(closeBtnRect, "BMS_AILoading_BtnClose".TranslateSafe(), true, false, true))
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
                Widgets.Label(dotsRect, "BMS_AILoading_Waiting".TranslateSafe() + dotStr);
            }

            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
