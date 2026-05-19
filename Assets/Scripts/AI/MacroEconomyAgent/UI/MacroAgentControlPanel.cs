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
        [SerializeField] private Font uiFont;

        public MacroEconomyAgent agent;
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
        public float nextRefreshTime;

        private readonly StringBuilder builder = new StringBuilder(2048);

        private void Awake()
        {
            if (uiFont == null)
            {
                uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            agent = MacroEconomyAgent.GetOrCreate();
            // BuildUi();
            BindFromSettings(agent.Settings);
            panelRoot.SetActive(showOnStart);
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

        // ---------- UI Construction ----------

        private void BuildUi()
        {
            EnsureEventSystem();

            if (uiFont == null)
            {
                uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (uiFont == null)
                {
                    uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("Macro Agent Canvas");
                canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;

                canvasObject.AddComponent<GraphicRaycaster>();
                transform.SetParent(canvasObject.transform, false);
            }

            panelRoot = CreatePanel("Macro Agent Panel", canvas.transform, new Color(0.04f, 0.06f, 0.08f, 0.94f));

            RectTransform panelRect = panelRoot.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(1f, 1f);
            panelRect.anchoredPosition = new Vector2(-24f, -72f);
            panelRect.sizeDelta = new Vector2(460f, 760f);
            panelRect.localScale = Vector3.one;

            float y = -16f;
            const float left = 16f;
            const float right = -16f;
            const float rowH = 32f;
            const float gap = 10f;

            CreateText("Title", panelRoot.transform, "Macro Economy Agent", 18, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(left, y - rowH), new Vector2(right, y));
            y -= rowH + gap;

            providerDropdown = CreateDropdown(panelRoot.transform, new[] { "OpenAI", "Gemini", "Claude", "DeepSeek" },
                new Vector2(left, y - rowH), new Vector2(right, y));
            providerDropdown.onValueChanged.AddListener(OnProviderChanged);
            y -= rowH + gap;

            apiKeyInput = CreateInput(panelRoot.transform, "API Key",
                new Vector2(left, y - rowH), new Vector2(right, y));
            apiKeyInput.contentType = InputField.ContentType.Password;
            y -= rowH + gap;

            modelInput = CreateInput(panelRoot.transform, "Model",
                new Vector2(left, y - rowH), new Vector2(right, y));
            y -= rowH + gap;

            intervalInput = CreateInput(panelRoot.transform, "Poll interval seconds",
                new Vector2(left, y - rowH), new Vector2(-190f, y));
            intervalInput.contentType = InputField.ContentType.DecimalNumber;

            enableToolsToggle = CreateToggle(panelRoot.transform, "MCP Tools",
                new Vector2(-180f, y - rowH), new Vector2(-88f, y));

            autoStartToggle = CreateToggle(panelRoot.transform, "Auto",
                new Vector2(-80f, y - rowH), new Vector2(right, y));

            y -= rowH + 12f;

            saveButton = CreateButton(panelRoot.transform, "Save",
                new Vector2(left, y - rowH), new Vector2(105f, y));
            saveButton.onClick.AddListener(OnSaveClicked);

            startButton = CreateButton(panelRoot.transform, "Start",
                new Vector2(113f, y - rowH), new Vector2(202f, y));
            startButton.onClick.AddListener(() =>
            {
                if (agent != null) agent.StartAgent();
            });

            stopButton = CreateButton(panelRoot.transform, "Stop",
                new Vector2(210f, y - rowH), new Vector2(299f, y));
            stopButton.onClick.AddListener(() =>
            {
                if (agent != null) agent.StopAgent();
            });

            runOnceButton = CreateButton(panelRoot.transform, "Run Once",
                new Vector2(307f, y - rowH), new Vector2(right, y));
            runOnceButton.onClick.AddListener(() =>
            {
                if (agent != null) agent.RunOnce();
            });

            y -= rowH + 12f;

            statusText = CreateText("Status", panelRoot.transform, "State: Stopped", 12, FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Vector2(left, y - 24f), new Vector2(right, y));
            y -= 34f;

            CreateText("Reasoning Label", panelRoot.transform, "Latest Reasoning:", 13, FontStyle.Bold,
                TextAnchor.MiddleLeft,
                new Vector2(left, y - 22f), new Vector2(right, y));
            y -= 26f;

            reasoningText = CreateText("Reasoning", panelRoot.transform, "(no policy yet)", 12, FontStyle.Normal,
                TextAnchor.UpperLeft,
                new Vector2(left, y - 72f), new Vector2(right, y));
            reasoningText.horizontalOverflow = HorizontalWrapMode.Wrap;
            reasoningText.verticalOverflow = VerticalWrapMode.Truncate;
            y -= 86f;

            CreateText("Policy Label", panelRoot.transform, "Active Policy:", 13, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(left, y - 22f), new Vector2(right, y));
            y -= 26f;

            policyText = CreateText("Policy", panelRoot.transform, "(none)", 11, FontStyle.Normal, TextAnchor.UpperLeft,
                new Vector2(left, y - 230f), new Vector2(right, y));
            policyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            policyText.verticalOverflow = VerticalWrapMode.Truncate;
            y -= 244f;

            CreateText("History Label", panelRoot.transform, "Recent History:", 13, FontStyle.Bold,
                TextAnchor.MiddleLeft,
                new Vector2(left, y - 22f), new Vector2(right, y));
            y -= 26f;

            historyText = CreateText("History", panelRoot.transform, "(empty)", 11, FontStyle.Normal,
                TextAnchor.UpperLeft,
                new Vector2(left, 16f), new Vector2(right, y));
            historyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            historyText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.localScale = Vector3.one;
            Image img = obj.AddComponent<Image>();
            img.color = color;
            return obj;
        }

        private Text CreateText(string name, Transform parent, string value, int size, FontStyle style, TextAnchor anchor,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);

            RectTransform rect = obj.AddComponent<RectTransform>();
            SetAnchors(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), offsetMin, offsetMax);

            Text t = obj.AddComponent<Text>();

            if (uiFont == null)
            {
                uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (uiFont == null)
                {
                    uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
            }

            t.font = uiFont;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = new Color(0.93f, 0.95f, 0.97f, 1f);
            t.text = value;
            t.raycastTarget = false;

            return t;
        }

        private InputField CreateInput(Transform parent, string placeholder, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject obj = CreatePanel("Input " + placeholder, parent, new Color(0.1f, 0.13f, 0.16f, 1f));
            SetAnchors(obj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), offsetMin,
                offsetMax);

            Text text = CreateText("Text", obj.transform, "", 12, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(6f, 4f), new Vector2(-6f, -4f));
            text.supportRichText = false;

            Text ph = CreateText("Placeholder", obj.transform, placeholder, 12, FontStyle.Italic, TextAnchor.MiddleLeft,
                new Vector2(6f, 4f), new Vector2(-6f, -4f));
            ph.color = new Color(0.6f, 0.65f, 0.7f, 0.7f);

            InputField input = obj.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            input.targetGraphic = obj.GetComponent<Image>();
            return input;
        }

        private Toggle CreateToggle(Transform parent, string label, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject obj = CreatePanel("Toggle " + label, parent, new Color(0.08f, 0.11f, 0.14f, 1f));
            SetAnchors(obj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), offsetMin,
                offsetMax);

            GameObject bg = CreatePanel("Bg", obj.transform, new Color(0.15f, 0.18f, 0.22f, 1f));
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            SetAnchors(bgRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, -8f),
                new Vector2(20f, 8f));

            GameObject check = CreatePanel("Checkmark", bg.transform, new Color(0.6f, 0.85f, 1f, 1f));
            SetFullStretch(check.GetComponent<RectTransform>(), 3f, 3f, -3f, -3f);

            Text txt = CreateText("Label", obj.transform, label, 12, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(28f, -2f), new Vector2(-4f, 2f));

            Toggle toggle = obj.AddComponent<Toggle>();
            toggle.targetGraphic = bg.GetComponent<Image>();
            toggle.graphic = check.GetComponent<Image>();
            return toggle;
        }

        private Button CreateButton(Transform parent, string label, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject obj = CreatePanel("Btn " + label, parent, new Color(0.18f, 0.22f, 0.28f, 1f));
            SetAnchors(obj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), offsetMin,
                offsetMax);
            Text t = CreateText("Label", obj.transform, label, 12, FontStyle.Bold, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.zero);
            SetFullStretch(t.rectTransform);
            t.raycastTarget = false;
            Button btn = obj.AddComponent<Button>();
            btn.targetGraphic = obj.GetComponent<Image>();
            return btn;
        }

        private Dropdown CreateDropdown(Transform parent, string[] options, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject obj = CreatePanel("Dropdown", parent, new Color(0.1f, 0.13f, 0.16f, 1f));
            SetAnchors(obj.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), offsetMin,
                offsetMax);

            Text label = CreateText("Label", obj.transform, options[0], 12, FontStyle.Normal, TextAnchor.MiddleLeft,
                new Vector2(8f, 0f), new Vector2(-24f, 0f));

            GameObject template = CreatePanel("Template", obj.transform, new Color(0.12f, 0.15f, 0.18f, 1f));
            RectTransform templateRect = template.GetComponent<RectTransform>();
            SetAnchors(templateRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, -120f),
                new Vector2(0f, 0f));
            Image templateImage = template.GetComponent<Image>();
            templateImage.raycastTarget = false;
            CanvasGroup templateCanvasGroup = template.AddComponent<CanvasGroup>();
            templateCanvasGroup.alpha = 0f;
            templateCanvasGroup.interactable = false;
            templateCanvasGroup.blocksRaycasts = false;
            template.SetActive(false);

            ScrollRect scroll = template.AddComponent<ScrollRect>();
            scroll.horizontal = false;

            GameObject viewport = CreatePanel("Viewport", template.transform, Color.clear);
            SetFullStretch(viewport.GetComponent<RectTransform>());
            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 120f);

            GameObject item = CreatePanel("Item", content.transform, new Color(0.14f, 0.17f, 0.2f, 1f));
            RectTransform itemRect = item.GetComponent<RectTransform>();
            SetAnchors(itemRect, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -10f),
                new Vector2(0f, 10f));
            Toggle itemToggle = item.AddComponent<Toggle>();
            itemToggle.targetGraphic = item.GetComponent<Image>();
            GameObject itemBg = CreatePanel("ItemBackground", item.transform, new Color(0f, 0f, 0f, 0f));
            SetFullStretch(itemBg.GetComponent<RectTransform>());
            GameObject itemCheck = CreatePanel("ItemCheckmark", item.transform, new Color(0.6f, 0.85f, 1f, 1f));
            SetAnchors(itemCheck.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(4f, -6f), new Vector2(16f, 6f));
            Text itemLabel = CreateText("Item Label", item.transform, "Option", 12, FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Vector2(24f, -8f), new Vector2(-4f, 8f));
            itemToggle.graphic = itemCheck.GetComponent<Image>();

            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;

            Dropdown dropdown = obj.AddComponent<Dropdown>();
            dropdown.targetGraphic = obj.GetComponent<Image>();
            dropdown.captionText = label;
            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
            dropdown.ClearOptions();
            List<string> opts = new List<string>(options);
            dropdown.AddOptions(opts);
            return dropdown;
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            GameObject obj = new GameObject("EventSystem");
            obj.AddComponent<EventSystem>();
            obj.AddComponent<StandaloneInputModule>();
        }

        private static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void SetFullStretch(RectTransform rect)
        {
            SetFullStretch(rect, 0f, 0f, 0f, 0f);
        }

        private static void SetFullStretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }
    }
}