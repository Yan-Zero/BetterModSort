using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Verse;

namespace BetterModSort.AI
{
    /// <summary>
    /// LLM 基础数据配置（用于持久化保存）
    /// </summary>
    public class LLMConfigData : IExposable
    {
        public LLMProvider Provider = LLMProvider.OpenAI;
        public string ApiKey = "";
        public string BaseUrl = "";
        public string ModelName = "gpt-4o";
        public int MaxTokens = 8192;

        public void ExposeData()
        {
            Scribe_Values.Look(ref Provider, "Provider", LLMProvider.OpenAI);
            Scribe_Values.Look(ref ApiKey, "ApiKey", "");
            Scribe_Values.Look(ref BaseUrl, "BaseUrl", "");
            Scribe_Values.Look(ref ModelName, "ModelName", "gpt-4o");
            Scribe_Values.Look(ref MaxTokens, "MaxTokens", 8192);
        }

        public LLMProviderStrategy CreateStrategy()
        {
            switch (Provider)
            {
                case LLMProvider.Anthropic: return new AnthropicStrategy(this);
                case LLMProvider.Gemini: return new GeminiStrategy(this);
                // DeepSeek and SiliconFlow are fully OpenAI compatible
                case LLMProvider.DeepSeek: return new OpenAIStrategy(this, "https://api.deepseek.com/chat/completions");
                case LLMProvider.SiliconFlow: return new OpenAIStrategy(this, "https://api.siliconflow.cn/v1/chat/completions");
                case LLMProvider.OpenAI:
                default:
                    return new OpenAIStrategy(this, "https://api.openai.com/v1/chat/completions");
            }
        }
    }

    /// <summary>
    /// LLM 提供商的具体通信与解析策略
    /// </summary>
    public abstract class LLMProviderStrategy
    {
        public LLMConfigData Config { get; }

        protected LLMProviderStrategy(LLMConfigData config)
        {
            Config = config;
        }

        /// <summary>
        /// 根据提示词、期望 JSON 约束构建带有特定端点头部和格式的 HTTP 请求
        /// </summary>
        public abstract HttpRequestMessage BuildRequest(string prompt, bool expectJsonFormat, bool usePromptInjection, object? sharedJsonSchema, out object? requestBodyObj);

        /// <summary>
        /// 解析提供商返回的 JSON 字符串内容和 Token 消耗情况
        /// </summary>
        public abstract string ExtractContentAndUsage(string responseString, out JToken? usage);
    }

    public class OpenAIStrategy : LLMProviderStrategy
    {
        private readonly string _defaultUrl;

        public OpenAIStrategy(LLMConfigData config, string defaultUrl) : base(config) 
        { 
            _defaultUrl = defaultUrl;
        }

        public override HttpRequestMessage BuildRequest(string prompt, bool expectJsonFormat, bool usePromptInjection, object? sharedJsonSchema, out object? requestBodyObj)
        {
            string url = string.IsNullOrWhiteSpace(Config.BaseUrl) ? _defaultUrl : Config.BaseUrl;
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, url);
            requestMessage.Headers.Add("Authorization", $"Bearer {Config.ApiKey}");

            int? userMaxTokens = Config.MaxTokens > 0 ? Config.MaxTokens : (int?)null;
            var reqObj = new
            {
                model = Config.ModelName,
                temperature = 0.4,
                max_tokens = userMaxTokens,
                response_format = (expectJsonFormat && !usePromptInjection) ? new { type = "json_object" } : null,
                messages = new[] { new { role = "user", content = prompt } }
            };

            requestBodyObj = reqObj;
            string payload = JsonConvert.SerializeObject(reqObj, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            requestMessage.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            return requestMessage;
        }

        public override string ExtractContentAndUsage(string responseString, out JToken? usage)
        {
            var jsonResponse = JObject.Parse(responseString);
            string? content = jsonResponse["choices"]?[0]?["message"]?["content"]?.ToString();
            usage = jsonResponse["usage"];
            return content ?? "";
        }
    }

    public class AnthropicStrategy : LLMProviderStrategy
    {
        public AnthropicStrategy(LLMConfigData config) : base(config) { }

        public override HttpRequestMessage BuildRequest(string prompt, bool expectJsonFormat, bool usePromptInjection, object? sharedJsonSchema, out object? requestBodyObj)
        {
            string url = string.IsNullOrWhiteSpace(Config.BaseUrl) ? "https://api.anthropic.com/v1/messages" : Config.BaseUrl;
            var requestMessage = new HttpRequestMessage(HttpMethod.Post, url);
            requestMessage.Headers.Add("x-api-key", Config.ApiKey);
            requestMessage.Headers.Add("anthropic-version", "2023-06-01");

            int? userMaxTokens = Config.MaxTokens > 0 ? Config.MaxTokens : (int?)null;
            var reqObj = new
            {
                model = Config.ModelName,
                max_tokens = userMaxTokens ?? (4096 * 4),
                temperature = 0.4,
                messages = new[] { new { role = "user", content = prompt } },
                output_config = (expectJsonFormat && !usePromptInjection && sharedJsonSchema != null) ? new
                {
                    format = new
                    {
                        type = "json_schema",
                        schema = sharedJsonSchema
                    }
                } : null
            };

            requestBodyObj = reqObj;
            string payload = JsonConvert.SerializeObject(reqObj, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            requestMessage.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            return requestMessage;
        }

        public override string ExtractContentAndUsage(string responseString, out JToken? usage)
        {
            var jsonResponse = JObject.Parse(responseString);
            string? content = jsonResponse["content"]?[0]?["text"]?.ToString();
            usage = jsonResponse["usage"];
            return content ?? "";
        }
    }

    public class GeminiStrategy : LLMProviderStrategy
    {
        public GeminiStrategy(LLMConfigData config) : base(config) { }

        public override HttpRequestMessage BuildRequest(string prompt, bool expectJsonFormat, bool usePromptInjection, object? sharedJsonSchema, out object? requestBodyObj)
        {
            string url = Config.BaseUrl;
            if (string.IsNullOrWhiteSpace(url))
                url = $"https://generativelanguage.googleapis.com/v1beta/models/{Config.ModelName}:generateContent";
            else if (!url.Contains(":generateContent"))
            {
                if (!url.EndsWith("/")) url += "/";
                url += $"models/{Config.ModelName}:generateContent";
            }

            if (!url.Contains("key="))
                url += (url.Contains("?") ? "&" : "?") + "key=" + Config.ApiKey;

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, url);

            int? userMaxTokens = Config.MaxTokens > 0 ? Config.MaxTokens : (int?)null;
            object? genConfig = null;
            if (expectJsonFormat && !usePromptInjection && sharedJsonSchema != null)
                 genConfig = new 
                 { 
                     maxOutputTokens = userMaxTokens,
                     responseMimeType = "application/json",
                     responseJsonSchema = sharedJsonSchema
                 };
            else if (userMaxTokens != null)
                 genConfig = new { maxOutputTokens = userMaxTokens };

            var reqObj = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = genConfig
            };

            requestBodyObj = reqObj;
            string payload = JsonConvert.SerializeObject(reqObj, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
            requestMessage.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            return requestMessage;
        }

        public override string ExtractContentAndUsage(string responseString, out JToken? usage)
        {
            var jsonResponse = JObject.Parse(responseString);
            string? content = jsonResponse["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
            usage = jsonResponse["usageMetadata"];
            return content ?? "";
        }
    }
}
