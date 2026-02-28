using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using BetterModSort.Hooks;
using BetterModSort.Core.ErrorAnalysis;

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
                sb.AppendLine("（无，系统未能根据上下文匹配到特定的民间 MOD，请根据文本自身的标识分析）");
            sb.AppendLine();

            sb.AppendLine("【要求】");
            sb.AppendLine("请你用简洁易懂的自然语言回答：");
            sb.AppendLine("1. 该错误的核心原因是什么？");
            sb.AppendLine("2. 根据以上堆栈和提供的疑似 MOD 列表，最可能是哪个 MOD 导致的此问题？");
            sb.AppendLine("3. 提出具体的修复建议（例如：哪个 MOD 不兼容、需要哪个前置依赖，或者修改加载顺序等）。");

            return sb.ToString();
        }

        /// <summary>
        /// 生成用于让 AI 提炼单个 MOD 的短描述的提示词
        /// </summary>
        public static string BuildShortDescPrompt(string packageId, string modName, string rawDescription)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是一个 RimWorld (边缘世界) 的资深 MOD 冲突诊断专家。");
            sb.AppendLine("请你通过阅读下面这篇某个 MOD 的完整描述（可能是作者手写的原版或 XML），为其提炼出一段极度精简的【性质简述】。");
            sb.AppendLine();
            sb.AppendLine($"【MOD 信息】\n包名: {packageId}\n名称: {modName}");
            sb.AppendLine();
            
            // 如果描述太长，截断它。保留头部即可，头部往往说明了 MOD 的用途。
            string safeDesc = rawDescription ?? "";
            int descMaxLen = BetterModSortMod.Settings.ShortDescMaxChars;
            if (safeDesc.Length > descMaxLen)
            {
                int maxLen = descMaxLen;
                if (char.IsHighSurrogate(safeDesc[maxLen - 1])) maxLen--;
                safeDesc = safeDesc.Substring(0, maxLen) + "\n...(截断)...";
            }
            sb.AppendLine("【原始描述】");
            sb.AppendLine(safeDesc);
            sb.AppendLine();
            sb.AppendLine("【要求】");
            sb.AppendLine("1. 重点提取出该 MOD 的『类型』(如：核心库、大型外星人种族、UI 修改、底层重写、内容追加等)。");
            sb.AppendLine("2. 重点提取出任何作者提及的『前置依赖』、『冲突说明』、『必须排在前面或后面的排序要求』。");
            sb.AppendLine("3. 不需要复述功能细节（如添加了什么动物），不要使用列表格式，只生成一段连续且紧凑的短文本（字数尽量控制在 150 字以内）。");
            sb.AppendLine("4. 请直接输出文本结果，不包含 json，不包含任何解释，不要带有 Markdown 加粗等装饰，因为结果将被缓存后紧凑注入到主控节点的上下文中。");
            
            return sb.ToString();
        }

        /// <summary>
        /// 生成用于整体智能排序（软约束）的提示词
        /// </summary>
        public static string BuildSortingSoftConstraintsPrompt(List<ModMetaData> mods, string errorLogContent, Dictionary<string, string> suspectShortDescs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("你是一个 RimWorld（边缘世界）的专家级 MOD 排序引擎。");
            sb.AppendLine("我需要你基于以下玩家当前激活的 MOD 列表，近期捕捉到的冲突日志，以及涉事 MOD 的部分性质简述，输出一套『临时排序软约束条件』。");
            sb.AppendLine();

            sb.AppendLine("【当前的 MOD 加载列表（按当前顺序自上而下排列）】");
            sb.AppendLine("由于 MOD 较多，我们约定使用如下高密度格式标识其现有关系：");
            sb.AppendLine("格式示范: 序号. [PackageId] ModName (Dep: 前置依赖列表 | Before: 必须在某某之前 | After: 必须在某某之后)");
            sb.AppendLine("注意：括号内没有说明的项目即为空。");
            sb.AppendLine();

            for (int i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                var details = new List<string>();

                if (mod.Dependencies != null && mod.Dependencies.Any())
                    details.Add($"Dep: {string.Join(",", mod.Dependencies.Select(d => d.packageId))}");
                if (mod.LoadBefore != null && mod.LoadBefore.Any())
                    details.Add($"Before: {string.Join(",", mod.LoadBefore)}");
                if (mod.LoadAfter != null && mod.LoadAfter.Any())
                    details.Add($"After: {string.Join(",", mod.LoadAfter)}");
                if (mod.IncompatibleWith != null && mod.IncompatibleWith.Any())
                    details.Add($"Incompatible: {string.Join(",", mod.IncompatibleWith)}");

                string suffix = details.Count > 0 ? $" ({string.Join(" | ", details)})" : "";
                sb.AppendLine($"{i + 1}. [{mod.PackageId}] {mod.Name}{suffix}");
            }
            sb.AppendLine();

            if (suspectShortDescs != null && suspectShortDescs.Count > 0)
            {
                sb.AppendLine("【近期报错相关嫌疑 MOD 的性质简述】");
                sb.AppendLine("这些是根据此前崩溃、抛错统计关联到的关键 MOD，我专门提取了提炼后的短描述，辅助你判断它们的排序：");
                foreach (var kvp in suspectShortDescs)
                    sb.AppendLine($"- [{kvp.Key}]: {kvp.Value}");
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(errorLogContent))
            {
                sb.AppendLine("【近期的致命报错日志记录（暗示潜在冲突）】");
                
                string safeErr = errorLogContent;
                // 防止传入过长的整个报错文件超出 LLM 上下文限制，适度截短并只保留最新的部分（文本日志末尾为最新）
                int errMaxLen = BetterModSortMod.Settings.ErrorLogMaxChars;
                if (safeErr.Length > errMaxLen)
                {
                    int maxLen = errMaxLen;
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
            sb.AppendLine("数组中的每个对象代表给某一个特定 MOD 追加的『临时 LoadBefore / LoadAfter 软约束』或『不兼容声明 IncompatibleWith』。");
            sb.AppendLine();
            sb.AppendLine("请注意：");
            sb.AppendLine("1. 仅针对真正需要调整顺序、有已知冲突风险或明确依赖（但未在上述清单中体现）的民间 MOD 给定软约束。");
            sb.AppendLine("2. 根据【嫌疑 MOD 简述】和【报错日志】，推算其中由于加载顺序不对导致的问题，制定修复约束！");
            sb.AppendLine("3. 除非 100% 确定两个 MOD 功能互斥（完全不能同时加载），否则不要随意添加 `IncompatibleWith` 标记。该标记会导致原版直接置红并拒绝排序！");
            sb.AppendLine("4. 不必为所有 MOD 生成规则，只输出必须改变其当前排位的规则！");
            sb.AppendLine("5. 你的输出【只能包含合法的 JSON 数组结构】，不需要包含任何解释。");
            sb.AppendLine();
            sb.AppendLine("JSON 示例格式：");
            sb.AppendLine("```json");
            sb.AppendLine("{");
            sb.AppendLine("  \"constraints\": [");
            sb.AppendLine("    {");
            sb.AppendLine("      \"PackageId\": \"authorA.modA\",");
            sb.AppendLine("      \"LoadBefore\": [\"authorB.modB\"],");
            sb.AppendLine("      \"LoadAfter\": [],");
            sb.AppendLine("      \"IncompatibleWith\": [\"authorC.modC\"]");
            sb.AppendLine("    }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine("现在请给出你的 JSON 分析结果：");

            return sb.ToString();
        }
    }
}
