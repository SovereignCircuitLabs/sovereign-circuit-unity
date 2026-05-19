using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ArcTrading.MacroAgent.Llm;
using ArcTrading.MacroAgent.Mcp;
using Newtonsoft.Json;
using UnityEngine;

namespace ArcTrading.MacroAgent
{
    [DisallowMultipleComponent]
    public class MacroEconomyAgent : Singleton<MacroEconomyAgent>
    {
        [SerializeField] private bool persistAcrossScenes = true;
        [SerializeField] private MacroAgentSettings inspectorSettings = new MacroAgentSettings();

        private MacroAgentSettings runtimeSettings;
        private EconomySnapshotCollector collector;
        private McpToolRegistry toolRegistry;
        private MacroPolicyApplier applier;

        private float nextRunGameTime;
        private bool running;
        private bool requestInFlight;
        private readonly List<MacroPolicy> history = new List<MacroPolicy>();
        private string lastError;
        private string lastRawResponse;
        private string lastSnapshotJson;

        public event Action<MacroPolicy> PolicyApplied;
        public event Action<string> AgentError;
        public event Action AgentStateChanged;

        public bool IsRunning { get { return running; } }
        public bool IsRequestInFlight { get { return requestInFlight; } }
        public string LastError { get { return lastError; } }
        public string LastRawResponse { get { return lastRawResponse; } }
        public string LastSnapshotJson { get { return lastSnapshotJson; } }
        public IReadOnlyList<MacroPolicy> History { get { return history; } }
        public MacroAgentSettings Settings { get { return runtimeSettings; } }
        public MacroPolicyApplier Applier { get { return applier; } }
        public McpToolRegistry ToolRegistry { get { return toolRegistry; } }
        public EconomySnapshotCollector Collector { get { return collector; } }

        protected override void Awake()
        {
            base.Awake();
            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
            }
            
            LoadRuntimeSettings();
            collector = new EconomySnapshotCollector();
            toolRegistry = new McpToolRegistry(collector);
            applier = new MacroPolicyApplier();
        }

        private void Start()
        {
            if (runtimeSettings.autoStart)
            {
                StartAgent();
            }
        }

        public void LoadRuntimeSettings()
        {
            MacroAgentSettings stored = MacroAgentSettings.LoadOrDefault();
            runtimeSettings = MergeWithInspector(stored, inspectorSettings);
        }

        public void UpdateSettings(MacroAgentSettings newSettings, bool saveToDisk)
        {
            if (newSettings == null)
            {
                return;
            }

            runtimeSettings = newSettings.Clone();
            if (saveToDisk)
            {
                runtimeSettings.Save();
            }

            AgentStateChanged?.Invoke();
        }

        public void StartAgent()
        {
            if (running)
            {
                return;
            }

            running = true;
            nextRunGameTime = Time.time;
            AgentStateChanged?.Invoke();
        }

        public void StopAgent()
        {
            if (!running)
            {
                return;
            }

            running = false;
            AgentStateChanged?.Invoke();
        }

        public async void RunOnce()
        {
            await RunOnceAsync();
        }

        private void Update()
        {
            if (!running || requestInFlight)
            {
                return;
            }

            if (Time.time < nextRunGameTime)
            {
                return;
            }

            nextRunGameTime = Time.time + Mathf.Max(2f, runtimeSettings.pollIntervalSeconds);
            _ = RunOnceAsync();
        }

        public async Task<MacroPolicyValidationResult> RunOnceAsync()
        {
            if (requestInFlight)
            {
                return MacroPolicyValidationResult.Fail("Request already in flight.");
            }

            requestInFlight = true;
            AgentStateChanged?.Invoke();

            try
            {
                if (string.IsNullOrWhiteSpace(runtimeSettings.GetActiveApiKey()))
                {
                    return ReportError("No API key configured for provider " + runtimeSettings.provider + ".");
                }

                ILlmProvider provider = BuildProvider(runtimeSettings);
                EconomySnapshot snapshot = collector.Collect();
                lastSnapshotJson = JsonConvert.SerializeObject(snapshot, Formatting.Indented);

                LlmRequest request = BuildRequest(snapshot, runtimeSettings);
                LlmResponse response = await RunWithToolLoopAsync(provider, request, runtimeSettings.maxToolCallIterations);
                lastRawResponse = response?.rawResponse;

                if (response == null || !response.requestSucceeded)
                {
                    string err = response?.errorMessage ?? "Unknown LLM failure.";
                    return ReportError("LLM request failed: " + err);
                }

                if (string.IsNullOrWhiteSpace(response.assistantText))
                {
                    return ReportError("LLM returned empty assistant text after tool loop.");
                }

                MacroPolicyValidationResult validation = MacroPolicyValidator.Validate(response.assistantText);
                if (!validation.isValid)
                {
                    return ReportError("Policy validation failed: " + validation.error);
                }

                applier.Apply(validation.policy);
                history.Add(validation.policy);
                if (history.Count > 50)
                {
                    history.RemoveAt(0);
                }

                lastError = null;
                PolicyApplied?.Invoke(validation.policy);
                return validation;
            }
            catch (Exception ex)
            {
                Debug.LogError("[MacroAgent] Unhandled exception: " + ex);
                return ReportError("Unhandled exception: " + ex.Message);
            }
            finally
            {
                requestInFlight = false;
                AgentStateChanged?.Invoke();
            }
        }

        private MacroPolicyValidationResult ReportError(string message)
        {
            lastError = message;
            Debug.LogWarning("[MacroAgent] " + message);
            AgentError?.Invoke(message);
            return MacroPolicyValidationResult.Fail(message);
        }

        private async Task<LlmResponse> RunWithToolLoopAsync(ILlmProvider provider, LlmRequest request, int maxIterations)
        {
            LlmResponse response = null;
            int iter = 0;

            while (iter <= maxIterations)
            {
                response = await provider.RequestAsync(request);
                if (response == null || !response.requestSucceeded)
                {
                    return response;
                }

                if (response.toolCalls == null || response.toolCalls.Count == 0)
                {
                    return response;
                }

                // Append assistant tool-call message
                request.messages.Add(new LlmMessage
                {
                    role = LlmRole.Assistant,
                    content = response.assistantText,
                    toolCalls = response.toolCalls
                });

                for (int i = 0; i < response.toolCalls.Count; i++)
                {
                    LlmToolCall call = response.toolCalls[i];
                    string result = await toolRegistry.InvokeAsync(call.name, call.argumentsJson);

                    request.messages.Add(new LlmMessage
                    {
                        role = LlmRole.Tool,
                        toolCallId = call.id,
                        toolName = call.name,
                        content = result
                    });
                }

                iter++;
            }

            // Force final answer without tools
            request.tools = null;
            request.messages.Add(new LlmMessage
            {
                role = LlmRole.User,
                content = "Tool call budget exhausted. Emit the FINAL JSON policy now per the schema, with NO tool calls."
            });

            return await provider.RequestAsync(request);
        }

        private LlmRequest BuildRequest(EconomySnapshot snapshot, MacroAgentSettings settings)
        {
            LlmRequest req = new LlmRequest
            {
                model = settings.GetActiveModel(),
                temperature = 0.4f,
                maxTokens = 1024,
                timeoutSeconds = settings.requestTimeoutSeconds
            };

            string systemPrompt = string.IsNullOrWhiteSpace(settings.systemPromptOverride)
                ? BuildDefaultSystemPrompt()
                : settings.systemPromptOverride;

            req.messages.Add(new LlmMessage { role = LlmRole.System, content = systemPrompt });
            req.messages.Add(new LlmMessage
            {
                role = LlmRole.User,
                content = "Current EconomySnapshot:\n```json\n" +
                          JsonConvert.SerializeObject(snapshot, Formatting.None) +
                          "\n```\n\nReturn ONLY the macro policy JSON object."
            });

            if (settings.enableMcpTools)
            {
                req.tools = new List<McpTool>(toolRegistry.Tools);
            }

            return req;
        }

        public static string BuildDefaultSystemPrompt()
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.AppendLine("# Role: AI Macro Economy Director (Arc-Chain Central Bank)");
            sb.AppendLine("You are the autonomous \"God Brain\" of an Arc-Chain on-chain trading game economy. Each cycle you receive an EconomySnapshot (NPC portfolios, on-chain balances, recent activities, market metrics, active world events, chain transaction status) and must output a single macro policy to stabilize the ecosystem or inject controlled volatility.");
            sb.AppendLine();
            sb.AppendLine("# Strict Capability Boundaries");
            sb.AppendLine("- You MAY: activate exactly one of [EnergyShortage, Inflation, MarketBoom, LiquidityCrunch, Normal] and tune nine multipliers within [0.5, 2.0].");
            sb.AppendLine("- You MUST NOT: generate executable code, request/produce private keys, sign transactions, initiate or describe direct asset transfers, or address any NPC/player wallet for transfers.");
            sb.AppendLine("- All NPC portfolio rebalancing, on-chain deposit/withdraw, reward payouts and contract calls remain controlled by deterministic C# logic — you only nudge the WorldEvent multipliers it consumes.");
            sb.AppendLine();
            sb.AppendLine("# Decision Heuristics (multipliers stack multiplicatively on top of base config)");
            sb.AppendLine("- Vault concentration high + low recent trading flow → MarketBoom (raise tradingWeightMultiplier, lower chainActionCooldownMultiplier).");
            sb.AppendLine("- Many failed chain actions / many NPCs blocked by cooldown → LiquidityCrunch (raise reserveWeightMultiplier and chainActionCooldownMultiplier, lower maxTradeMultiplier).");
            sb.AppendLine("- Living budget drain rising / repeated supply purchases → EnergyShortage (raise livingNeedsWeightMultiplier, raise minimumLivingBudgetMultiplier).");
            sb.AppendLine("- Wallet balances inflating across NPCs → Inflation (raise minimumLivingBudgetMultiplier and minTrade/maxTradeMultiplier).");
            sb.AppendLine("- Balanced metrics, low std-dev → Normal (all multipliers near 1.0).");
            sb.AppendLine();
            sb.AppendLine("# Tool Use");
            sb.AppendLine("You may call provided MCP tools (list_npcs, get_npc_detail, get_active_world_events, get_market_summary, get_chain_status) to drill into state. Every tool is read-only. After enough information, emit a SINGLE final JSON object — no prose, no markdown fences.");
            sb.AppendLine();
            sb.AppendLine("# Strict JSON Output Schema");
            sb.AppendLine("{");
            sb.AppendLine("  \"reasoning\": \"macro economic analysis within 60 words for UI typewriter display\",");
            sb.AppendLine("  \"target_event\": \"EnergyShortage | Inflation | MarketBoom | LiquidityCrunch | Normal\",");
            sb.AppendLine("  \"modifiers\": {");
            sb.AppendLine("    \"livingNeedsWeightMultiplier\":   <float 0.5-2.0>,");
            sb.AppendLine("    \"reserveWeightMultiplier\":       <float 0.5-2.0>,");
            sb.AppendLine("    \"tradingWeightMultiplier\":       <float 0.5-2.0>,");
            sb.AppendLine("    \"minimumLivingBudgetMultiplier\": <float 0.5-2.0>,");
            sb.AppendLine("    \"minimumReserveBudgetMultiplier\":<float 0.5-2.0>,");
            sb.AppendLine("    \"rebalanceIntervalMultiplier\":   <float 0.5-2.0>,");
            sb.AppendLine("    \"chainActionCooldownMultiplier\": <float 0.5-2.0>,");
            sb.AppendLine("    \"minTradeMultiplier\":            <float 0.5-2.0>,");
            sb.AppendLine("    \"maxTradeMultiplier\":            <float 0.5-2.0>");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("Output ONLY this JSON object — no markdown, no commentary, no code fences.");
            return sb.ToString();
        }

        private static ILlmProvider BuildProvider(MacroAgentSettings s)
        {
            switch (s.provider)
            {
                case LlmProviderKind.OpenAI:
                    return new OpenAiProvider(s.openAiApiKey, s.openAiModel, s.openAiBaseUrl);
                case LlmProviderKind.Gemini:
                    return new GeminiProvider(s.geminiApiKey, s.geminiModel, s.geminiBaseUrl);
                case LlmProviderKind.Claude:
                    return new ClaudeProvider(s.claudeApiKey, s.claudeModel, s.claudeBaseUrl, s.claudeVersion);
                case LlmProviderKind.DeepSeek:
                    return new DeepSeekProvider(s.deepseekApiKey, s.deepseekModel, s.deepseekBaseUrl);
                default:
                    throw new InvalidOperationException("Unsupported provider: " + s.provider);
            }
        }

        private static MacroAgentSettings MergeWithInspector(MacroAgentSettings stored, MacroAgentSettings inspector)
        {
            // Stored (PlayerPrefs) takes priority; inspector fills any blanks (e.g., system prompt override).
            if (string.IsNullOrEmpty(stored.systemPromptOverride) && !string.IsNullOrEmpty(inspector.systemPromptOverride))
            {
                stored.systemPromptOverride = inspector.systemPromptOverride;
            }
            
            stored.provider = inspector.provider;
            
            stored.openAiApiKey = inspector.openAiApiKey;
            stored.openAiModel = inspector.openAiModel;
            stored.openAiBaseUrl = inspector.openAiBaseUrl;
            
            stored.claudeApiKey = inspector.claudeApiKey;
            stored.claudeModel = inspector.claudeModel;
            stored.claudeBaseUrl = inspector.claudeBaseUrl;
            stored.claudeVersion = inspector.claudeVersion;
            
            stored.geminiApiKey = inspector.geminiApiKey;
            stored.geminiModel = inspector.geminiModel;
            stored.geminiBaseUrl = inspector.geminiBaseUrl;
            
            stored.deepseekApiKey = inspector.deepseekApiKey;
            stored.deepseekModel = inspector.deepseekModel;
            stored.deepseekBaseUrl = inspector.deepseekBaseUrl;

            if (stored.pollIntervalSeconds <= 0f)
            {
                stored.pollIntervalSeconds = Mathf.Max(5f, inspector.pollIntervalSeconds);
            }

            stored.autoStart = inspector.autoStart || stored.autoStart;
            return stored;
        }
    }
}
