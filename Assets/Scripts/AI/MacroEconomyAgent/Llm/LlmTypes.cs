using System;
using System.Collections.Generic;
using ArcTrading.MacroAgent.Mcp;

namespace ArcTrading.MacroAgent.Llm
{
    public enum LlmRole
    {
        System,
        User,
        Assistant,
        Tool
    }

    [Serializable]
    public class LlmMessage
    {
        public LlmRole role;
        public string content;
        public List<LlmToolCall> toolCalls;
        public string toolCallId;
        public string toolName;
    }

    [Serializable]
    public class LlmToolCall
    {
        public string id;
        public string name;
        public string argumentsJson;
    }

    public class LlmRequest
    {
        public string model;
        public List<LlmMessage> messages = new List<LlmMessage>();
        public List<McpTool> tools;
        public float temperature = 0.4f;
        public int maxTokens = 2048;
        public int timeoutSeconds = 60;
    }

    public class LlmResponse
    {
        public string assistantText;
        public List<LlmToolCall> toolCalls;
        public bool requestSucceeded;
        public string errorMessage;
        public string rawResponse;
    }
}
