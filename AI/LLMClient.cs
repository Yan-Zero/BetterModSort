using System;
using System.Net.Http;
using System.Text;
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

            _httpClient = new HttpClient(handler)
            {
                // 设定一个合理的超时，避免过长时间卡死 
                Timeout = TimeSpan.FromSeconds(3600)
            };
        }

        /// <summary>
        /// 异步请求大模型聊天接口，返回提取后的文本内容
        /// </summary>
        public static async Task<string> SendChatRequestAsync(string prompt, bool expectJsonFormat = false)
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new InvalidOperationException("BMS_LLM_ApiKeyMissing".TranslateSafe());

            var requestBody = new
            {
                model = ModelName,
                temperature = 0.4,
                response_format = expectJsonFormat ? new { type = "json_object" } : null,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            // 使用 NullValueHandling.Ignore 忽略掉未设置的 response_format
            string payload = JsonConvert.SerializeObject(requestBody, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            requestMessage.Headers.Add("Authorization", $"Bearer {ApiKey}");
            requestMessage.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            _httpClient.Timeout = TimeSpan.FromSeconds(BetterModSortMod.Settings.LLMTimeoutSeconds);

            HttpResponseMessage response = await _httpClient.SendAsync(requestMessage);
            string responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                DumpSummary(prompt, response.StatusCode.ToString(), null, null, responseString);
                throw new Exception($"LLM 服务请求失败: {response.StatusCode}\n{responseString}");
            }

            try
            {
                var jsonResponse = JObject.Parse(responseString);
                var content = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString();
                var usage = jsonResponse["usage"];

                DumpSummary(prompt, response.StatusCode.ToString(), usage, content, null);

                return content ?? responseString;
            }
            catch
            {
                DumpSummary(prompt, response.StatusCode.ToString(), null, null, responseString);
                return responseString;
            }
        }

        /// <summary>
        /// Dump 目录路径
        /// </summary>
        public static string DumpDir =>
            System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "BetterModSort", "Dump");

        /// <summary>
        /// 当 Debug Dump 开启时，将关键摘要写入 Dump/ 子目录
        /// </summary>
        private static void DumpSummary(string prompt, string statusCode, JToken? usage, string? content, string? errorBody)
        {
            try
            {
                if (!BetterModSortMod.Settings.EnableDebugDump) return;

                if (!System.IO.Directory.Exists(DumpDir))
                    System.IO.Directory.CreateDirectory(DumpDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filePath = System.IO.Path.Combine(DumpDir, $"LLM_{timestamp}.txt");

                var sb = new StringBuilder();
                sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"Model: {ModelName}");
                sb.AppendLine($"Status: {statusCode}");
                sb.AppendLine($"Prompt Length: {prompt?.Length ?? 0} chars");

                if (usage != null)
                {
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

                System.IO.File.WriteAllText(filePath, sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Warning("[BetterModSort] " + "BMS_Log_DebugDumpWriteFailed".TranslateSafe("LLM", ex.Message));
            }
        }
    }
}
