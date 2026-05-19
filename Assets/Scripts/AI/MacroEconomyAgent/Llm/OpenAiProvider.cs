using System.Collections.Generic;
using System.Threading.Tasks;
using ArcTrading.MacroAgent.Internal;
using ArcTrading.MacroAgent.Mcp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArcTrading.MacroAgent.Llm
{
    public class OpenAiProvider : ILlmProvider
    {
        private readonly string apiKey;
        private readonly string model;
        private readonly string baseUrl;

        public OpenAiProvider(string apiKey, string model, string baseUrl)
        {
            this.apiKey = apiKey;
            this.model = model;
            this.baseUrl = string.IsNullOrEmpty(baseUrl) ? "https://api.openai.com/v1" : baseUrl.TrimEnd('/');
        }

        public string Name { get { return "OpenAI"; } }

        public async Task<LlmResponse> RequestAsync(LlmRequest request)
        {
            JObject payload = new JObject
            {
                ["model"] = string.IsNullOrEmpty(request.model) ? model : request.model,
                ["temperature"] = request.temperature,
                ["max_tokens"] = request.maxTokens,
                ["messages"] = BuildMessages(request.messages)
            };

            if (request.tools != null && request.tools.Count > 0)
            {
                payload["tools"] = BuildTools(request.tools);
                payload["tool_choice"] = "auto";
            }

            string body = payload.ToString(Formatting.None);
            (string key, string value)[] headers =
            {
                ("Authorization", "Bearer " + apiKey)
            };

            WebRequestHelper.HttpResult http = await WebRequestHelper.PostJsonAsync(
                baseUrl + "/chat/completions", body, request.timeoutSeconds, headers);

            return Parse(http);
        }

        private static JArray BuildMessages(List<LlmMessage> messages)
        {
            JArray arr = new JArray();
            for (int i = 0; i < messages.Count; i++)
            {
                LlmMessage msg = messages[i];
                JObject obj = new JObject();
                obj["role"] = RoleToString(msg.role);

                if (msg.role == LlmRole.Tool)
                {
                    obj["tool_call_id"] = msg.toolCallId ?? string.Empty;
                    obj["content"] = msg.content ?? string.Empty;
                    arr.Add(obj);
                    continue;
                }

                if (msg.toolCalls != null && msg.toolCalls.Count > 0)
                {
                    JArray calls = new JArray();
                    for (int j = 0; j < msg.toolCalls.Count; j++)
                    {
                        LlmToolCall c = msg.toolCalls[j];
                        calls.Add(new JObject
                        {
                            ["id"] = c.id,
                            ["type"] = "function",
                            ["function"] = new JObject
                            {
                                ["name"] = c.name,
                                ["arguments"] = c.argumentsJson ?? "{}"
                            }
                        });
                    }

                    obj["tool_calls"] = calls;
                    obj["content"] = msg.content ?? string.Empty;
                }
                else
                {
                    obj["content"] = msg.content ?? string.Empty;
                }

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
                    ["type"] = "function",
                    ["function"] = new JObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = t.JsonSchema ?? new JObject()
                    }
                });
            }

            return arr;
        }

        private static string RoleToString(LlmRole role)
        {
            switch (role)
            {
                case LlmRole.System: return "system";
                case LlmRole.User: return "user";
                case LlmRole.Assistant: return "assistant";
                case LlmRole.Tool: return "tool";
                default: return "user";
            }
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
                JToken choices = root["choices"];
                if (choices == null || choices.Type != JTokenType.Array || !choices.HasValues)
                {
                    resp.errorMessage = "Empty choices in response.";
                    resp.requestSucceeded = false;
                    return resp;
                }

                JObject first = (JObject)choices[0];
                JObject message = (JObject)first["message"];
                resp.assistantText = message?.Value<string>("content");

                JToken calls = message?["tool_calls"];
                if (calls != null && calls.Type == JTokenType.Array && calls.HasValues)
                {
                    resp.toolCalls = new List<LlmToolCall>();
                    foreach (JToken c in calls)
                    {
                        JObject fn = (JObject)c["function"];
                        resp.toolCalls.Add(new LlmToolCall
                        {
                            id = c.Value<string>("id"),
                            name = fn?.Value<string>("name"),
                            argumentsJson = fn?.Value<string>("arguments") ?? "{}"
                        });
                    }
                }
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
