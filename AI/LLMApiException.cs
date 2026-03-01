using System;

namespace BetterModSort.AI
{
    public class LLMApiException : Exception
    {
        public int StatusCode { get; }
        public string ResponseBody { get; }

        public LLMApiException(int statusCode, string responseBody)
            : base($"LLM API Request Failed with status {statusCode}")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }
    }
}
