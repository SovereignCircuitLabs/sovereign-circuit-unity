using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ArcTrading.MacroAgent.Internal;
using ArcTrading.MacroAgent.Mcp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArcTrading.MacroAgent.Llm
{
    public class GeminiProvider : ILlmProvider
    {
        private readonly string apiKey;
        private readonly string model;
        private readonly string baseUrl;

        public GeminiProvider(string apiKey, string model, string baseUrl)
        {
            this.apiKey = apiKey;
            this.model = model;
            this.baseUrl = string.IsNullOrEmpty(baseUrl)
                ? "https://generativelanguage.googleapis.com/v1beta"
                : baseUrl.TrimEnd('/');
        }

        public string Name { get { return "Gemini"; } }

        public async Task<LlmResponse> RequestAsync(LlmRequest request)
        {
            string usedModel = string.IsNullOrEmpty(request.model) ? model : request.model;
            string url = baseUrl + "/models/" + usedModel + ":generateContent?key=" + Uri.EscapeDataString(apiKey);

            JObject payload = new JObject();
            string systemInstruction;
            JArray contents = BuildContents(request.messages, out systemInstruction);

            if (!string.IsNullOrEmpty(systemInstruction))
            {
                payload["systemInstruction"] = new JObject
                {
                    ["parts"] = new JArray { new JObject { ["text"] = systemInstruction } }
                };
            }

            payload["contents"] = contents;
            payload["generationConfig"] = new JObject
            {
                ["temperature"] = request.temperature,
                ["maxOutputTokens"] = request.maxTokens
            };

            if (request.tools != null && request.tools.Count > 0)
            {
                payload["tools"] = new JArray
                {
                    new JObject { ["functionDeclarations"] = BuildFunctionDeclarations(request.tools) }
                };
            }

            string body = payload.ToString(Formatting.None);
            WebRequestHelper.HttpResult http = await WebRequestHelper.PostJsonAsync(url, body, request.timeoutSeconds, null);
            return Parse(http);
        }

        private static JArray BuildContents(List<LlmMessage> messages, out string systemInstruction)
        {
            systemInstruction = null;
            JArray arr = new JArray();

            for (int i = 0; i < messages.Count; i++)
            {
                LlmMessage msg = messages[i];
                if (msg.role == LlmRole.System)
                {
                    systemInstruction = string.IsNullOrEmpty(systemInstruction)
                        ? msg.content
                        : systemInstruction + "\n\n" + msg.content;
                    continue;
                }

                JObject obj = new JObject();
                JArray parts = new JArray();

                if (msg.role == LlmRole.Tool)
                {
                    obj["role"] = "user";
                    JObject response;
                    try
                    {
                        response = string.IsNullOrEmpty(msg.content) ? new JObject() : JObject.Parse(msg.content);
                    }
                    catch
                    {
                        response = new JObject { ["raw"] = msg.content };
                    }

                    parts.Add(new JObject
                    {
                        ["functionResponse"] = new JObject
                        {
                            ["name"] = msg.toolName ?? string.Empty,
                            ["response"] = response
                        }
                    });
                    obj["parts"] = parts;
                    arr.Add(obj);
                    continue;
                }

                obj["role"] = msg.role == LlmRole.Assistant ? "model" : "user";

                if (!string.IsNullOrEmpty(msg.content))
                {
                    parts.Add(new JObject { ["text"] = msg.content });
                }

                if (msg.role == LlmRole.Assistant && msg.toolCalls != null)
                {
                    for (int j = 0; j < msg.toolCalls.Count; j++)
                    {
                        LlmToolCall c = msg.toolCalls[j];
                        JObject args;
                        try
                        {
                            args = string.IsNullOrEmpty(c.argumentsJson)
                                ? new JObject()
                                : JObject.Parse(c.argumentsJson);
                        }
                        catch
                        {
                            args = new JObject();
                        }

                        parts.Add(new JObject
                        {
                            ["functionCall"] = new JObject
                            {
                                ["name"] = c.name,
                                ["args"] = args
                            }
                        });
                    }
                }

                obj["parts"] = parts;
                arr.Add(obj);
            }

            return arr;
        }

        private static JArray BuildFunctionDeclarations(List<McpTool> tools)
        {
            JArray arr = new JArray();
            for (int i = 0; i < tools.Count; i++)
            {
                McpTool t = tools[i];
                arr.Add(new JObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = t.JsonSchema ?? new JObject()
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
                JToken candidates = root["candidates"];
                if (candidates == null || !candidates.HasValues)
                {
                    resp.errorMessage = "No candidates returned.";
                    resp.requestSucceeded = false;
                    return resp;
                }

                JObject candidate = (JObject)candidates[0];
                JObject contentObj = (JObject)candidate["content"];
                JArray parts = (JArray)contentObj?["parts"];
                System.Text.StringBuilder textBuilder = new System.Text.StringBuilder();
                List<LlmToolCall> calls = null;

                if (parts != null)
                {
                    foreach (JToken part in parts)
                    {
                        JToken textVal = part["text"];
                        if (textVal != null)
                        {
                            textBuilder.Append(textVal.Value<string>());
                        }

                        JToken fnCall = part["functionCall"];
                        if (fnCall != null && fnCall.Type == JTokenType.Object)
                        {
                            if (calls == null)
                            {
                                calls = new List<LlmToolCall>();
                            }

                            calls.Add(new LlmToolCall
                            {
                                id = Guid.NewGuid().ToString("N"),
                                name = fnCall.Value<string>("name"),
                                argumentsJson = fnCall["args"] != null ? fnCall["args"].ToString(Formatting.None) : "{}"
                            });
                        }
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
