using System.Threading.Tasks;

namespace ArcTrading.MacroAgent.Llm
{
    /// <summary>
    /// DeepSeek (V3/V4) speaks OpenAI's /chat/completions dialect verbatim: same JSON,
    /// same tool_calls protocol, Bearer auth. We just delegate to OpenAiProvider with
    /// the DeepSeek base URL so payload + tool-loop logic stays in one place.
    /// Default base URL: https://api.deepseek.com/v1
    /// Default model:    deepseek-chat (auto-routes to the latest model, currently V4-class)
    /// </summary>
    public class DeepSeekProvider : ILlmProvider
    {
        private readonly OpenAiProvider inner;

        public DeepSeekProvider(string apiKey, string model, string baseUrl)
        {
            string resolvedBase = string.IsNullOrEmpty(baseUrl)
                ? "https://api.deepseek.com/v1"
                : baseUrl;
            string resolvedModel = string.IsNullOrEmpty(model) ? "deepseek-v4-pro" : model;
            inner = new OpenAiProvider(apiKey, resolvedModel, resolvedBase);
        }

        public string Name { get { return "DeepSeek"; } }

        public Task<LlmResponse> RequestAsync(LlmRequest request)
        {
            return inner.RequestAsync(request);
        }
    }
}
