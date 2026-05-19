using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ArcTrading.MacroAgent.UI
{
    /// <summary>
    /// Runtime control panel for the Macro Economy Agent. Auto-builds its own UI like
    /// NpcDemoScrollViewPopup so it can be dropped onto any GameObject in the scene.
    /// Toggle with F8 by default.
    /// </summary>
    public class MacroAgentControlPanel : MonoBehaviour
    {
        [SerializeField] private KeyCode togglePanelKey = KeyCode.Tab;
        [SerializeField] private bool showOnStart = true;

        private MacroEconomyAgent agent;
        private float nextRefreshTime;
        private readonly StringBuilder builder = new StringBuilder(2048);
        
        public GameObject panelRoot;
        public InputField apiKeyInput;
        public InputField modelInput;
        public InputField intervalInput;
        public Dropdown providerDropdown;
        public Toggle enableToolsToggle;
        public Toggle autoStartToggle;
        public Text statusText;
        public Text reasoningText;
        public Text policyText;
        public Text historyText;
        public Button startButton;
        public Button stopButton;
        public Button runOnceButton;
        public Button saveButton;

        private void Awake()
        {
            agent = MacroEconomyAgent.Instance;
            agent.LoadRuntimeSettings();
            BindFromSettings(agent.Settings);
            panelRoot.SetActive(showOnStart);
            InitButtonsAndDropdowns();
        }

        private void OnEnable()
        {
            if (agent != null)
            {
                agent.PolicyApplied += OnPolicyApplied;
                agent.AgentError += OnAgentError;
                agent.AgentStateChanged += RefreshStatus;
            }
        }

        private void OnDisable()
        {
            if (agent != null)
            {
                agent.PolicyApplied -= OnPolicyApplied;
                agent.AgentError -= OnAgentError;
                agent.AgentStateChanged -= RefreshStatus;
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(togglePanelKey) && panelRoot != null)
            {
                agent.LoadRuntimeSettings();
                BindFromSettings(agent.Settings);
                panelRoot.SetActive(!panelRoot.activeSelf);
            }

            if (Time.unscaledTime < nextRefreshTime)
            {
                return;
            }

            nextRefreshTime = Time.unscaledTime + 0.5f;
            RefreshStatus();
        }

        private void OnPolicyApplied(MacroPolicy policy)
        {
            if (policy == null)
            {
                return;
            }

            if (reasoningText != null)
            {
                reasoningText.text = policy.reasoning;
            }

            if (policyText != null)
            {
                policyText.text = BuildPolicyText(policy);
            }

            RefreshStatus();
        }

        private void OnAgentError(string err)
        {
            if (statusText != null)
            {
                statusText.text = "<color=#ff7676>ERROR:</color> " + err;
            }
        }

        private void RefreshStatus()
        {
            if (statusText != null && agent != null)
            {
                string state = agent.IsRequestInFlight ? "Requesting..."
                    : agent.IsRunning ? "Running" : "Stopped";
                statusText.text = "State: " + state +
                                  " | Provider: " + agent.Settings.provider +
                                  " | Model: " + agent.Settings.GetActiveModel() +
                                  " | Interval: " + agent.Settings.pollIntervalSeconds + "s";
            }

            if (historyText != null && agent != null)
            {
                builder.Length = 0;
                IReadOnlyList<MacroPolicy> history = agent.History;
                int start = Mathf.Max(0, history.Count - 6);
                for (int i = history.Count - 1; i >= start; i--)
                {
                    MacroPolicy p = history[i];
                    builder.AppendLine($"[{p.appliedUtc}] {p.target_event}");
                    builder.AppendLine($"  {p.reasoning}");
                }

                if (history.Count == 0)
                {
                    builder.AppendLine("(no policies applied yet)");
                }

                historyText.text = builder.ToString();
            }
        }

        private string BuildPolicyText(MacroPolicy p)
        {
            builder.Length = 0;
            builder.AppendLine($"Target Event : {p.target_event}");
            builder.AppendLine($"Applied UTC  : {p.appliedUtc}");
            builder.AppendLine();
            builder.AppendLine("Modifiers:");
            builder.AppendLine($"  livingNeedsWeight    × {p.modifiers.livingNeedsWeightMultiplier:0.00}");
            builder.AppendLine($"  reserveWeight        × {p.modifiers.reserveWeightMultiplier:0.00}");
            builder.AppendLine($"  tradingWeight        × {p.modifiers.tradingWeightMultiplier:0.00}");
            builder.AppendLine($"  minimumLivingBudget  × {p.modifiers.minimumLivingBudgetMultiplier:0.00}");
            builder.AppendLine($"  minimumReserveBudget × {p.modifiers.minimumReserveBudgetMultiplier:0.00}");
            builder.AppendLine($"  rebalanceInterval    × {p.modifiers.rebalanceIntervalMultiplier:0.00}");
            builder.AppendLine($"  chainActionCooldown  × {p.modifiers.chainActionCooldownMultiplier:0.00}");
            builder.AppendLine($"  minTrade             × {p.modifiers.minTradeMultiplier:0.00}");
            builder.AppendLine($"  maxTrade             × {p.modifiers.maxTradeMultiplier:0.00}");
            return builder.ToString();
        }

        private void BindFromSettings(MacroAgentSettings s)
        {
            if (providerDropdown != null)
            {
                providerDropdown.value = (int)s.provider;
                providerDropdown.RefreshShownValue();
            }

            if (apiKeyInput != null)
            {
                apiKeyInput.text = s.GetActiveApiKey();
            }

            if (modelInput != null)
            {
                modelInput.text = s.GetActiveModel();
            }

            if (intervalInput != null)
            {
                intervalInput.text = s.pollIntervalSeconds.ToString("0.##");
            }

            if (enableToolsToggle != null)
            {
                enableToolsToggle.isOn = s.enableMcpTools;
            }

            if (autoStartToggle != null)
            {
                autoStartToggle.isOn = s.autoStart;
            }
        }

        void InitButtonsAndDropdowns()
        {
            startButton.onClick.AddListener(() =>
            {
                if (agent != null) agent.StartAgent();
            });

            stopButton.onClick.AddListener(() =>
            {
                if (agent != null) agent.StopAgent();
            });

            runOnceButton.onClick.AddListener(() =>
            {
                if (agent != null) agent.RunOnce();
            });

            saveButton.onClick.AddListener(OnSaveClicked);
            
            providerDropdown.onValueChanged.AddListener(OnProviderChanged);
        }

        private void OnSaveClicked()
        {
            MacroAgentSettings current = agent.Settings.Clone();
            if (providerDropdown != null)
            {
                current.provider = (LlmProviderKind)providerDropdown.value;
            }

            string keyValue = apiKeyInput != null ? apiKeyInput.text : string.Empty;
            string modelValue = modelInput != null ? modelInput.text : string.Empty;

            switch (current.provider)
            {
                case LlmProviderKind.OpenAI:
                    current.openAiApiKey = keyValue;
                    current.openAiModel = modelValue;
                    break;
                case LlmProviderKind.Gemini:
                    current.geminiApiKey = keyValue;
                    current.geminiModel = modelValue;
                    break;
                case LlmProviderKind.Claude:
                    current.claudeApiKey = keyValue;
                    current.claudeModel = modelValue;
                    break;
                case LlmProviderKind.DeepSeek:
                    current.deepseekApiKey = keyValue;
                    current.deepseekModel = modelValue;
                    break;
            }

            if (intervalInput != null && float.TryParse(intervalInput.text, out float interval))
            {
                current.pollIntervalSeconds = Mathf.Max(5f, interval);
            }

            if (enableToolsToggle != null)
            {
                current.enableMcpTools = enableToolsToggle.isOn;
            }

            if (autoStartToggle != null)
            {
                current.autoStart = autoStartToggle.isOn;
            }

            agent.UpdateSettings(current, true);
            BindFromSettings(agent.Settings);
            if (statusText != null)
            {
                statusText.text = "Settings saved.";
            }
        }

        private void OnProviderChanged(int _)
        {
            // Repopulate API key/model fields for the newly chosen provider so users can edit them.
            MacroAgentSettings preview = agent.Settings.Clone();
            preview.provider = (LlmProviderKind)providerDropdown.value;
            if (apiKeyInput != null)
            {
                apiKeyInput.text = preview.GetActiveApiKey();
            }

            if (modelInput != null)
            {
                modelInput.text = preview.GetActiveModel();
            }
        }
    }
}