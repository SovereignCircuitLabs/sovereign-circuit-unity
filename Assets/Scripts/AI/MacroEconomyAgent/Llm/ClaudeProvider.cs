using System.Collections.Generic;
using System.Threading.Tasks;
using ArcTrading.MacroAgent.Internal;
using ArcTrading.MacroAgent.Mcp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArcTrading.MacroAgent.Llm
{
    public class ClaudeProvider : ILlmProvider
    {
        private readonly string apiKey;
        private readonly string model;
        private readonly string baseUrl;
        private readonly string anthropicVersion;

        public ClaudeProvider(string apiKey, string model, string baseUrl, string anthropicVersion)
        {
            this.apiKey = apiKey;
            this.model = model;
            this.baseUrl = string.IsNullOrEmpty(baseUrl) ? "https://api.anthropic.com/v1" : baseUrl.TrimEnd('/');
            this.anthropicVersion = string.IsNullOrEmpty(anthropicVersion) ? "2023-06-01" : anthropicVersion;
        }

        public string Name { get { return "Claude"; } }

        public async Task<LlmResponse> RequestAsync(LlmRequest request)
        {
            JObject payload = new JObject
            {
                ["model"] = string.IsNullOrEmpty(request.model) ? model : request.model,
                ["max_tokens"] = request.maxTokens,
                ["temperature"] = request.temperature
            };

            string systemPrompt;
            JArray messages = BuildMessages(request.messages, out systemPrompt);
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                payload["system"] = systemPrompt;
            }

            payload["messages"] = messages;

            if (request.tools != null && request.tools.Count > 0)
            {
                payload["tools"] = BuildTools(request.tools);
            }

            string body = payload.ToString(Formatting.None);
            (string key, string value)[] headers =
            {
                ("x-api-key", apiKey),
                ("anthropic-version", anthropicVersion)
            };

            WebRequestHelper.HttpResult http = await WebRequestHelper.PostJsonAsync(
                baseUrl + "/messages", body, request.timeoutSeconds, headers);

            return Parse(http);
        }

        private static JArray BuildMessages(List<LlmMessage> messages, out string systemPrompt)
        {
            systemPrompt = null;
            JArray arr = new JArray();

            for (int i = 0; i < messages.Count; i++)
            {
                LlmMessage msg = messages[i];
                if (msg.role == LlmRole.System)
                {
                    systemPrompt = string.IsNullOrEmpty(systemPrompt)
                        ? msg.content
                        : systemPrompt + "\n\n" + msg.content;
                    continue;
                }

                JObject obj = new JObject();
                JArray contentArr = new JArray();

                if (msg.role == LlmRole.Tool)
                {
                    obj["role"] = "user";
                    contentArr.Add(new JObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = msg.toolCallId ?? string.Empty,
                        ["content"] = msg.content ?? string.Empty
                    });
                    obj["content"] = contentArr;
                    arr.Add(obj);
                    continue;
                }

                if (msg.role == LlmRole.Assistant)
                {
                    obj["role"] = "assistant";
                    if (!string.IsNullOrEmpty(msg.content))
                    {
                        contentArr.Add(new JObject
                        {
                            ["type"] = "text",
                            ["text"] = msg.content
                        });
                    }

                    if (msg.toolCalls != null)
                    {
                        for (int j = 0; j < msg.toolCalls.Count; j++)
                        {
                            LlmToolCall c = msg.toolCalls[j];
                            JObject input;
                            try
                            {
                                input = string.IsNullOrEmpty(c.argumentsJson)
                                    ? new JObject()
                                    : JObject.Parse(c.argumentsJson);
                            }
                            catch
                            {
                                input = new JObject();
                            }

                            contentArr.Add(new JObject
                            {
                                ["type"] = "tool_use",
                                ["id"] = c.id,
                                ["name"] = c.name,
                                ["input"] = input
                            });
                        }
                    }

                    obj["content"] = contentArr;
                    arr.Add(obj);
                    continue;
                }

                // User
                obj["role"] = "user";
                contentArr.Add(new JObject
                {
                    ["type"] = "text",
                    ["text"] = msg.content ?? string.Empty
                });
                obj["content"] = contentArr;
                arr.Add(obj);
            }

            return arr;
        }

        private static JArray BuildTools(List<McpTool> tools)
        {
            JArray arr = new JArray();
            for (int i = 0; i < tools.Count; i++)
            {
                McpTool t = tools[i];
                arr.Add(new JObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = t.JsonSchema ?? new JObject()
                });
            }

            return arr;
        }

        private static LlmResponse Parse(WebRequestHelper.HttpResult http)
        {
            LlmResponse resp = new LlmResponse
            {
                rawResponse = http.body,
                requestSucceeded = http.success
            };

            if (!http.success)
            {
                resp.errorMessage = "HTTP " + http.statusCode + " " + http.error + " :: " + http.body;
                return resp;
            }

            try
            {
                JObject root = JObject.Parse(http.body);
                JToken content = root["content"];
                if (content == null || content.Type != JTokenType.Array)
                {
                    resp.errorMessage = "No content array.";
                    resp.requestSucceeded = false;
                    return resp;
                }

                System.Text.StringBuilder textBuilder = new System.Text.StringBuilder();
                List<LlmToolCall> calls = null;

                foreach (JToken block in content)
                {
                    string type = block.Value<string>("type");
                    if (type == "text")
                    {
                        textBuilder.Append(block.Value<string>("text") ?? string.Empty);
                    }
                    else if (type == "tool_use")
                    {
                        if (calls == null)
                        {
                            calls = new List<LlmToolCall>();
                        }

                        JToken input = block["input"];
                        calls.Add(new LlmToolCall
                        {
                            id = block.Value<string>("id"),
                            name = block.Value<string>("name"),
                            argumentsJson = input != null ? input.ToString(Formatting.None) : "{}"
                        });
                    }
                }

                resp.assistantText = textBuilder.ToString();
                resp.toolCalls = calls;
            }
            catch (JsonException ex)
            {
                resp.requestSucceeded = false;
                resp.errorMessage = "Parse error: " + ex.Message;
            }

            return resp;
        }
    }
}
