using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Verse;
using BetterModSort.Tools;

namespace BetterModSort.AI
{
    /// <summary>
    /// 大语言模型服务客户端（目前兼容类 OpenAI API 接口标准）
    /// 用来将 PromptBuilder 构建的文本发送给服务器并获取纯文本回复。
    /// </summary>
    public static class LLMClient
    {
        // 我们利用静态 HttpClient 防止连接池耗尽
        private static readonly HttpClient _httpClient;

        public static LLMProvider Provider { get; set; } = LLMProvider.OpenAI;
        public static string BaseUrl { get; set; } = ""; 
        public static string ApiKey { get; set; } = "";
        public static string ModelName { get; set; } = "";

        static LLMClient()
        {
            // 为了绕过 Mono 在某些 Windows 机器上初始化 CookieContainer 或 Proxy 时
            // 去调用获取 Win32 网络接口（get_DomainName），因遇到中文或特殊字符网络环境名
            // 导致的底层 P/Invoke 乱码崩溃（ArgumentException: Illegal byte sequence encounted）
            // 我们手动配置 Handler 彻底禁用 Cookies 和系统代理轮询。
            var handler = new HttpClientHandler
            {
                UseCookies = false,
                UseProxy = false
            };
            _httpClient = new HttpClient(handler);
        }

        /// <summary>
        /// 异步请求大模型聊天接口，返回提取后的文本内容
        /// </summary>
        public static async Task<string> SendChatRequestAsync(string prompt, bool expectJsonFormat = false)
        {
            return await SendChatRequestInternalAsync(prompt, expectJsonFormat, fallbackToPromptInjection: false);
        }

        private static async Task<string> SendChatRequestInternalAsync(string prompt, bool expectJsonFormat, bool fallbackToPromptInjection)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new InvalidOperationException("BMS_LLM_ApiKeyMissing".TranslateSafe());

            bool usePromptInjection = fallbackToPromptInjection;
            // Native structured outputs handle JSON for Anthropic and Gemini

            string actualPrompt = prompt;
            if (usePromptInjection)
                actualPrompt += "\n\n注意：请务必只输出合法的 JSON 数据，不要包含任何 Markdown 格式（如 ```json 等），不要包含 <think> 标签，也不要附带任何解释文本。";

            string activeBaseUrl = BaseUrl;
            if (string.IsNullOrWhiteSpace(activeBaseUrl))
            {
                if (Provider == LLMProvider.OpenAI) activeBaseUrl = "https://api.openai.com/v1/chat/completions";
                else if (Provider == LLMProvider.Anthropic) activeBaseUrl = "https://api.anthropic.com/v1/messages";
                else if (Provider == LLMProvider.Gemini) activeBaseUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:generateContent";
            }
            else if (Provider == LLMProvider.Gemini && !activeBaseUrl.Contains(":generateContent"))
            {
                if (!activeBaseUrl.EndsWith("/")) activeBaseUrl += "/";
                activeBaseUrl += $"models/{ModelName}:generateContent";
            }

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, activeBaseUrl);
            object? requestBody = null;
            object? sharedJsonSchema = null;
            if (expectJsonFormat && !usePromptInjection && (Provider == LLMProvider.Anthropic || Provider == LLMProvider.Gemini))
            {
                sharedJsonSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        constraints = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    PackageId = new { type = "string" },
                                    LoadBefore = new { type = "array", items = new { type = "string" } },
                                    LoadAfter = new { type = "array", items = new { type = "string" } },
                                    IncompatibleWith = new { type = "array", items = new { type = "string" } }
                                },
                                required = new[] { "PackageId" }
                            }
                        }
                    },
                    required = new[] { "constraints" }
                };
            }

            if (Provider == LLMProvider.OpenAI)
            {
                requestBody = new
                {
                    model = ModelName,
                    temperature = 0.4,
                    response_format = (expectJsonFormat && !usePromptInjection) ? new { type = "json_object" } : null,
                    messages = new[] { new { role = "user", content = actualPrompt } }
                };
                requestMessage.Headers.Add("Authorization", $"Bearer {ApiKey}");
            }
            else if (Provider == LLMProvider.Anthropic)
            {
                requestBody = new
                {
                    model = ModelName,
                    max_tokens = 4096 * 4,
                    temperature = 0.4,
                    messages = new[] { new { role = "user", content = actualPrompt } },
                    output_config = (sharedJsonSchema != null) ? new
                    {
                        format = new
                        {
                            type = "json_schema",
                            schema = sharedJsonSchema
                        }
                    } : null
                };
                requestMessage.Headers.Add("x-api-key", ApiKey);
                requestMessage.Headers.Add("anthropic-version", "2023-06-01");
            }
            else if (Provider == LLMProvider.Gemini)
            {
                string url = activeBaseUrl;
                if (!url.Contains("key="))
                {
                    url += (url.Contains("?") ? "&" : "?") + "key=" + ApiKey;
                }
                requestMessage.RequestUri = new Uri(url);
                requestBody = new
                {
                    contents = new[] { new { parts = new[] { new { text = actualPrompt } } } },
                    generationConfig = (sharedJsonSchema != null) ? new 
                    { 
                        responseMimeType = "application/json",
                        responseJsonSchema = sharedJsonSchema
                    } : null
                };
            }

            string payload = JsonConvert.SerializeObject(requestBody, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            requestMessage.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            _httpClient.Timeout = TimeSpan.FromSeconds(BetterModSortMod.Settings.LLMTimeoutSeconds);

            HttpResponseMessage response = await _httpClient.SendAsync(requestMessage);
            string responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                if (expectJsonFormat && !fallbackToPromptInjection && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    Log.Warning("[BetterModSort] " + "BMS_LLM_JSONFallbackRetry".TranslateSafe());
                    return await SendChatRequestInternalAsync(prompt, expectJsonFormat, fallbackToPromptInjection: true);
                }

                DumpSummary(actualPrompt, response.StatusCode.ToString(), null, null, responseString);
                throw new Exception($"LLM 服务请求失败: {response.StatusCode}\n{responseString}");
            }

            try
            {
                var jsonResponse = JObject.Parse(responseString);
                string? content = null;
                JToken? usage = null;

                if (Provider == LLMProvider.OpenAI)
                {
                    content = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString();
                    usage = jsonResponse["usage"];
                }
                else if (Provider == LLMProvider.Anthropic)
                {
                    content = jsonResponse["content"]?[0]?["text"]?.ToString();
                    usage = jsonResponse["usage"];
                }
                else if (Provider == LLMProvider.Gemini)
                {
                    content = jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                    usage = jsonResponse["usageMetadata"];
                }

                DumpSummary(actualPrompt, response.StatusCode.ToString(), usage, content, null);

                if (content != null && expectJsonFormat)
                    content = CleanJsonResponse(content);

                return content ?? responseString;
            }
            catch
            {
                DumpSummary(actualPrompt, response.StatusCode.ToString(), null, null, responseString);
                return responseString;
            }
        }

        /// <summary>
        /// 针对需要 JSON 的场景，对返回文本进行清洗和手动提取
        /// </summary>
        private static string CleanJsonResponse(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse)) return rawResponse;
            string cleaned = rawResponse;

            // 移除 <think> 标签及其内部的思维链内容
            cleaned = Regex.Replace(cleaned, @"(?s)<think>.*?</think>", "").Trim();

            // 移除 Markdown ```json 或 ``` 块的外包装
            var match = Regex.Match(cleaned, @"```(?:json)?\s*(.*?)\s*```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (match.Success)
                cleaned = match.Groups[1].Value.Trim();

            // 最后保险：寻找最外层的大括号或中括号
            int startIdxBrace = cleaned.IndexOf('{');
            int startIdxBracket = cleaned.IndexOf('[');
            
            int startIdx = -1;
            if (startIdxBrace >= 0 && startIdxBracket >= 0)
                startIdx = Math.Min(startIdxBrace, startIdxBracket);
            else if (startIdxBrace >= 0)
                startIdx = startIdxBrace;
            else if (startIdxBracket >= 0)
                startIdx = startIdxBracket;

            if (startIdx >= 0)
            {
                int endIdxBrace = cleaned.LastIndexOf('}');
                int endIdxBracket = cleaned.LastIndexOf(']');
                int endIdx = Math.Max(endIdxBrace, endIdxBracket);

                if (endIdx > startIdx)
                    cleaned = cleaned.Substring(startIdx, endIdx - startIdx + 1);
            }

            return cleaned;
        }

        /// <summary>
        /// Dump 目录路径
        /// </summary>
        public static string DumpDir =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, "BetterModSort", "Dump");

        /// <summary>
        /// 当 Debug Dump 开启时，将关键摘要写入 Dump/ 子目录
        /// </summary>
        private static void DumpSummary(string prompt, string statusCode, JToken? usage, string? content, string? errorBody)
        {
            try
            {
                if (!BetterModSortMod.Settings.EnableDebugDump) return;

                if (!Directory.Exists(DumpDir))
                    Directory.CreateDirectory(DumpDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filePath = Path.Combine(DumpDir, $"LLM_{timestamp}.txt");

                var sb = new StringBuilder();
                sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Model: {ModelName}");
                sb.AppendLine($"Status: {statusCode}");
                sb.AppendLine($"Prompt Length: {prompt?.Length ?? 0} chars");

                if (usage != null)
                {
                    if (Provider == LLMProvider.Anthropic)
                        sb.AppendLine($"Tokens — input: {usage["input_tokens"]}, output: {usage["output_tokens"]}");
                    else if (Provider == LLMProvider.Gemini)
                        sb.AppendLine($"Tokens — prompt: {usage["promptTokenCount"]}, candidates: {usage["candidatesTokenCount"]}, total: {usage["totalTokenCount"]}");
                    else
                        sb.AppendLine($"Tokens — prompt: {usage["prompt_tokens"]}, completion: {usage["completion_tokens"]}, total: {usage["total_tokens"]}");
                }

                sb.AppendLine();
                sb.AppendLine("=== Prompt ===");
                sb.AppendLine(prompt ?? "");
                sb.AppendLine();
                sb.AppendLine();

                if (content != null)
                {
                    sb.AppendLine("=== LLM Response Content ===");
                    sb.AppendLine(content);
                }
                else if (errorBody != null)
                {
                    sb.AppendLine("=== Error Response ===");
                    string truncated = errorBody.Length > 500 ? errorBody[..500] + "..." : errorBody;
                    sb.AppendLine(truncated);
                }

                File.WriteAllText(filePath, sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterModSort] " + "BMS_Log_DebugDumpWriteFailed".TranslateSafe("LLM", ex.Message));
            }
        }
    }
}
