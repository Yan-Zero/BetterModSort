using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using BetterModSort.Hooks;

namespace BetterModSort.AI
{
    /// <summary>
    /// 构建向大模型发送的各种提问（Prompt）的生成器
    /// </summary>
    public static class PromptBuilder
    {
        /// <summary>
        /// 生成用于诊断单个或近期错误的提示词
        /// </summary>
        public static string BuildErrorDiagnosisPrompt(CapturedErrorInfo errorInfo)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是一个 RimWorld（边缘世界）的资深 MOD 冲突诊断专家。请帮我分析下面这个游戏内的报错。");
            sb.AppendLine();
            
            sb.AppendLine("【报错详情】");
            sb.AppendLine($"时间: {errorInfo.CapturedTime:yyyy-MM-dd HH:mm:ss}");
            // 系统已经内置了强大的追踪机制（比如 XmlSource, TextureSource 等），所以错误文本中已经有极具价值的标识
            sb.AppendLine($"错误文本:\n{errorInfo.ErrorMessage}");
            sb.AppendLine();

            sb.AppendLine("【系统追踪到的疑似相关 MOD】");
            if (errorInfo.RelatedMods != null && errorInfo.RelatedMods.Any())
            {
                var relatedIds = errorInfo.RelatedMods.Select(m => $"[{m.PackageId}] {m.ModName}");
                sb.AppendLine(string.Join("\n", relatedIds));
            }
            else
            {
                sb.AppendLine("（无，系统未能根据上下文匹配到特定的民间 MOD，请根据文本自身的标识分析）");
            }
            sb.AppendLine();

            sb.AppendLine("【要求】");
            sb.AppendLine("请你用简洁易懂的自然语言回答：");
            sb.AppendLine("1. 该错误的核心原因是什么？");
            sb.AppendLine("2. 根据以上堆栈和提供的疑似 MOD 列表，最可能是哪个 MOD 导致的此问题？");
            sb.AppendLine("3. 提出具体的修复建议（例如：哪个 MOD 不兼容、需要哪个前置依赖，或者修改加载顺序等）。");

            return sb.ToString();
        }

        /// <summary>
        /// 生成用于整体智能排序（软约束）的提示词
        /// </summary>
        public static string BuildSortingSoftConstraintsPrompt(List<ModMetaData> mods, string errorLogContent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是一个 RimWorld（边缘世界）的专家级 MOD 排序引擎。");
            sb.AppendLine("我需要你基于以下玩家当前激活的 MOD 列表，以及最近捕捉到可能因为排序不当引起的冲突错误日志，输出一套『临时排序软约束条件』。");
            sb.AppendLine();

            sb.AppendLine("【当前的 MOD 加载列表（按当前顺序自上而下）】");
            for (int i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                sb.AppendLine($"{i + 1}. [{mod.PackageId}] {mod.Name}");
                if (mod.Dependencies != null && mod.Dependencies.Any())
                {
                    var deps = string.Join(", ", mod.Dependencies.Select(d => d.packageId));
                    sb.AppendLine($"   -> 明确依赖 (Dependencies): {deps}");
                }
                if (mod.LoadBefore != null && mod.LoadBefore.Any())
                {
                    sb.AppendLine($"   -> 必须在以下之前 (LoadBefore): {string.Join(", ", mod.LoadBefore)}");
                }
                if (mod.LoadAfter != null && mod.LoadAfter.Any())
                {
                    sb.AppendLine($"   -> 必须在以下之后 (LoadAfter): {string.Join(", ", mod.LoadAfter)}");
                }
            }
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(errorLogContent))
            {
                sb.AppendLine("【近期的致命报错（可能暗示潜在冲突）】");
                
                string safeErr = errorLogContent;
                // 防止传入过长的整个报错文件超出 LLM 上下文限制，适度截短并只保留最新的部分（文本日志末尾为最新）
                if (safeErr.Length > 4000)
                {
                    int maxLen = 4000;
                    int startIndex = safeErr.Length - maxLen;
                    // 防止把 Emoji 或 Surrogate Pair 切开
                    if (char.IsLowSurrogate(safeErr[startIndex]))
                    {
                        startIndex++;
                        maxLen--;
                    }
                    safeErr = "...(截断前面日志)...\n" + safeErr.Substring(startIndex, maxLen);
                }
                sb.AppendLine(safeErr);
                sb.AppendLine();
            }

            sb.AppendLine("【任务说明与输出格式要求】");
            sb.AppendLine("请不要返回一大段长篇大论或修改原列表。只需返回一个符合下述格式的 JSON 数组。");
            sb.AppendLine("数组中的每个对象代表给某一个特定 MOD 追加的『临时 LoadBefore / LoadAfter 软约束』。");
            sb.AppendLine();
            sb.AppendLine("请注意：");
            sb.AppendLine("1. 仅针对真正需要调整顺序、有已知冲突风险或明确依赖（但未在上述清单中体现）的民间 MOD 给定软约束。");
            sb.AppendLine("2. 不必为所有 MOD 生成规则，只输出必须改变其当前排位的规则！");
            sb.AppendLine("3. 你的输出【只能包含合法的 JSON 数组结构】，不需要包含任何解释。");
            sb.AppendLine();
            sb.AppendLine("JSON 示例格式：");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"constraints\": [");
            sb.AppendLine("    {");
            sb.AppendLine("      \"PackageId\": \"authorA.modA\",");
            sb.AppendLine("      \"LoadBefore\": [\"authorB.modB\", \"authorC.modC\"],");
            sb.AppendLine("      \"LoadAfter\": []");
            sb.AppendLine("    }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine("现在请给出你的 JSON 分析结果：");

            return sb.ToString();
        }
    }
}
