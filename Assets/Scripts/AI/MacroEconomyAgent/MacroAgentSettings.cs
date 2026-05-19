using System;
using UnityEngine;

namespace ArcTrading.MacroAgent
{
    public enum LlmProviderKind
    {
        OpenAI,
        Gemini,
        Claude,
        DeepSeek
    }

    [Serializable]
    public class MacroAgentSettings
    {
        public LlmProviderKind provider = LlmProviderKind.OpenAI;

        public MacroAgentSettings() { }

        public string openAiApiKey = string.Empty;
        public string openAiModel = "gpt-4o-mini";
        public string openAiBaseUrl = "https://api.openai.com/v1";

        public string geminiApiKey = string.Empty;
        public string geminiModel = "gemini-1.5-flash";
        public string geminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

        public string claudeApiKey = string.Empty;
        public string claudeModel = "claude-sonnet-4-6";
        public string claudeBaseUrl = "https://api.anthropic.com/v1";
        public string claudeVersion = "2023-06-01";

        public string deepseekApiKey = string.Empty;
        public string deepseekModel = "deepseek-chat";
        public string deepseekBaseUrl = "https://api.deepseek.com/v1";

        public float pollIntervalSeconds = 30f;
        public int maxToolCallIterations = 4;
        public int requestTimeoutSeconds = 60;
        public bool enableMcpTools = true;
        public bool autoStart = false;

        [TextArea(6, 30)]
        public string systemPromptOverride = string.Empty;

        public MacroAgentSettings Clone()
        {
            return (MacroAgentSettings)MemberwiseClone();
        }

        public string GetActiveApiKey()
        {
            switch (provider)
            {
                case LlmProviderKind.OpenAI: return openAiApiKey;
                case LlmProviderKind.Gemini: return geminiApiKey;
                case LlmProviderKind.Claude: return claudeApiKey;
                case LlmProviderKind.DeepSeek: return deepseekApiKey;
                default: return string.Empty;
            }
        }

        public string GetActiveModel()
        {
            switch (provider)
            {
                case LlmProviderKind.OpenAI: return openAiModel;
                case LlmProviderKind.Gemini: return geminiModel;
                case LlmProviderKind.Claude: return claudeModel;
                case LlmProviderKind.DeepSeek: return deepseekModel;
                default: return string.Empty;
            }
        }

        private const string PlayerPrefsKey = "ArcTrading.MacroAgent.Settings.v1";

        public static MacroAgentSettings LoadOrDefault()
        {
            string json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new MacroAgentSettings();
            }

            try
            {
                MacroAgentSettings parsed = JsonUtility.FromJson<MacroAgentSettings>(json);
                return parsed ?? new MacroAgentSettings();
            }
            catch
            {
                return new MacroAgentSettings();
            }
        }

        public void Save()
        {
            PlayerPrefs.SetString(PlayerPrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }
    }
}
