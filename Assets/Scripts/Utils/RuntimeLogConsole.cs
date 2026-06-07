using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class RuntimeLogConsole : MonoBehaviour
{
    private const int MaxEntries = 150;
    private const int MaxRenderedCharacters = 22000;
    private const int MaxMessageLength = 320;

    private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.68f);
    private static readonly Color HandleColor = new Color(1f, 1f, 1f, 0.22f);
    private static readonly Color DefaultColor = new Color(0.86f, 0.9f, 0.96f);
    private static readonly Color WarningColor = new Color(1f, 0.78f, 0.32f);
    private static readonly Color ErrorColor = new Color(1f, 0.32f, 0.32f);
    private static readonly Color PaymentBindColor = new Color(1f, 0.82f, 0.45f);    // gold — payment wallet binding
    private static readonly Color NanopaymentColor = new Color(0.55f, 0.85f, 1f);    // cyan — x402 / nanopayment
    private static readonly Color SellTradeColor = new Color(1f, 0.55f, 0.85f);      // pink — SellNFT / sellItem
    private static readonly Color BuyTradeColor = new Color(0.55f, 1f, 0.78f);       // mint — BuyNFT / mintRandom
    private static readonly Color MacroAgentColor = new Color(0.85f, 0.75f, 1f);     // lavender — MacroAgent
    private static readonly Color WorldEventColor = new Color(1f, 0.65f, 0.55f);     // salmon — world events
    private static readonly Color WalletLoginColor = new Color(0.55f, 0.95f, 0.55f); // emerald — browser wallet auth
    private static readonly Color OwnedNpcSpawnColor = new Color(1f, 0.92f, 0.62f);  // pale yellow — owned NPC spawn

    private readonly List<LogEntry> entries = new List<LogEntry>();
    private readonly StringBuilder builder = new StringBuilder(4096);

    private Text logText;
    private Text copyButtonText;
    private RectTransform contentRect;
    private RectTransform viewportRect;
    private ScrollRect scrollRect;

    private struct LogEntry
    {
        public string Message;
        public Color Color;
    }
    
    [RuntimeInitializeOnLoadMethod]
    private static void Init()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "MainScene")
            return;

        if (FindObjectOfType<RuntimeLogConsole>() != null)
            return;

        GameObject root = new GameObject("Runtime Log Console");
        DontDestroyOnLoad(root);
        root.AddComponent<RuntimeLogConsole>();
    }

    private void Awake()
    {
        BuildUi();
    }

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void HandleLog(string condition, string stackTrace, LogType type)
    {
        if (!IsImportant(type, condition))
        {
            return;
        }

        entries.Add(new LogEntry
        {
            Message = Shorten(condition, MaxMessageLength),
            Color = ResolveColor(condition, type)
        });

        PruneEntries();
        Render();
    }

    private void BuildUi()
    {
        EnsureEventSystem();

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();

        GameObject panelObject = new GameObject("Panel");
        panelObject.transform.SetParent(transform, false);

        Image panel = panelObject.AddComponent<Image>();
        panel.color = PanelColor;
        panelObject.AddComponent<RuntimeLogConsoleWindowDrag>();

        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-5f, -3.5f);
        panelRect.sizeDelta = new Vector2(620f, 250f);

        GameObject viewportObject = new GameObject("Viewport");
        viewportObject.transform.SetParent(panelObject.transform, false);

        Image viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);

        Mask mask = viewportObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        viewportRect = viewportObject.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(14f, 14f);
        viewportRect.offsetMax = new Vector2(-14f, -38f);

        GameObject contentObject = new GameObject("Content");
        contentObject.transform.SetParent(viewportObject.transform, false);

        contentRect = contentObject.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        GameObject textObject = new GameObject("Text");
        textObject.transform.SetParent(contentObject.transform, false);

        logText = textObject.AddComponent<Text>();
        logText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        logText.fontSize = 13;
        logText.lineSpacing = 1.05f;
        logText.alignment = TextAnchor.UpperLeft;
        logText.horizontalOverflow = HorizontalWrapMode.Wrap;
        logText.verticalOverflow = VerticalWrapMode.Overflow;
        logText.raycastTarget = false;
        logText.supportRichText = true;
        logText.text = string.Empty;

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        scrollRect = panelObject.AddComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 28f;

        GameObject resizeObject = new GameObject("Resize Handle");
        resizeObject.transform.SetParent(panelObject.transform, false);

        Image resizeImage = resizeObject.AddComponent<Image>();
        resizeImage.color = HandleColor;
        resizeObject.AddComponent<RuntimeLogConsoleResizeHandle>();

        RectTransform resizeRect = resizeObject.GetComponent<RectTransform>();
        resizeRect.anchorMin = Vector2.zero;
        resizeRect.anchorMax = Vector2.zero;
        resizeRect.pivot = Vector2.zero;
        resizeRect.anchoredPosition = new Vector2(6f, 6f);
        resizeRect.sizeDelta = new Vector2(18f, 18f);

        CreateCopyButton(panelObject.transform);
    }

    private void Render()
    {
        if (logText == null)
        {
            return;
        }

        bool shouldStickToBottom = scrollRect == null || scrollRect.verticalNormalizedPosition <= 0.02f;

        builder.Clear();
        int renderedCharacters = 0;
        int skippedEntries = 0;

        for (int i = entries.Count - 1; i >= 0; i--)
        {
            LogEntry entry = entries[i];
            int entryCharacterCost = entry.Message.Length + 32;
            if (renderedCharacters + entryCharacterCost > MaxRenderedCharacters)
            {
                skippedEntries = i + 1;
                break;
            }

            renderedCharacters += entryCharacterCost;
        }

        if (skippedEntries > 0)
        {
            builder.Append("<color=#");
            builder.Append(ColorUtility.ToHtmlStringRGB(WarningColor));
            builder.Append(">");
            builder.Append(skippedEntries);
            builder.AppendLine(" older log entries hidden to keep UI mesh size safe.</color>");
        }

        for (int i = skippedEntries; i < entries.Count; i++)
        {
            LogEntry entry = entries[i];
            builder.Append("<color=#");
            builder.Append(ColorUtility.ToHtmlStringRGB(entry.Color));
            builder.Append(">");
            builder.Append(EscapeRichText(entry.Message));
            builder.AppendLine("</color>");
        }

        logText.text = builder.ToString();
        Canvas.ForceUpdateCanvases();

        float contentHeight = Mathf.Max(viewportRect.rect.height, logText.preferredHeight);
        contentRect.sizeDelta = new Vector2(0f, contentHeight);

        if (shouldStickToBottom)
        {
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }

    private void Update()
    {
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.C))
        {
            CopyAllLogs();
        }
    }

    private void CreateCopyButton(Transform parent)
    {
        GameObject buttonObject = new GameObject("Copy Button");
        buttonObject.transform.SetParent(parent, false);

        Image buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = new Color(1f, 1f, 1f, 0.16f);

        Button button = buttonObject.AddComponent<Button>();
        button.onClick.AddListener(CopyAllLogs);

        ColorBlock colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.16f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.26f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.36f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(1f, 0f);
        buttonRect.anchorMax = new Vector2(1f, 0f);
        buttonRect.pivot = new Vector2(1f, 0f);
        buttonRect.anchoredPosition = new Vector2(-10f, 8f);
        buttonRect.sizeDelta = new Vector2(76f, 24f);

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);

        copyButtonText = labelObject.AddComponent<Text>();
        copyButtonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        copyButtonText.fontSize = 12;
        copyButtonText.alignment = TextAnchor.MiddleCenter;
        copyButtonText.color = DefaultColor;
        copyButtonText.raycastTarget = false;
        copyButtonText.text = "Copy";

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    private void CopyAllLogs()
    {
        builder.Clear();
        foreach (LogEntry entry in entries)
        {
            builder.AppendLine(entry.Message);
        }

        GUIUtility.systemCopyBuffer = builder.ToString();

        if (copyButtonText != null)
        {
            copyButtonText.text = "Copied";
            CancelInvoke(nameof(ResetCopyButtonText));
            Invoke(nameof(ResetCopyButtonText), 1.2f);
        }
    }

    private void PruneEntries()
    {
        int overflow = entries.Count - MaxEntries;
        if (overflow > 0)
        {
            entries.RemoveRange(0, overflow);
        }
    }

    private void ResetCopyButtonText()
    {
        if (copyButtonText != null)
        {
            copyButtonText.text = "Copy";
        }
    }

    private static bool IsImportant(LogType type, string message)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert || type == LogType.Warning)
        {
            return true;
        }

        // Trader portfolio lifecycle
        if (message.Contains("initialized USDC portfolio")
            || message.Contains("rebalanced portfolio")
            || message.Contains("bought living supplies")
            || message.Contains("holds:")
            || message.Contains("tx=")
            || message.Contains("tx:"))
        {
            return true;
        }

        // Trade intents (TradingNpcActor logs "{intent}{modeTag} {amount} USDC ...")
        if (message.Contains("BuyNFT") || message.Contains("SellNFT")
            || message.Contains("Deposit") || message.Contains("Withdraw")
            || message.Contains("nanopayment"))
        {
            return true;
        }

        // Payment wallet binding (x402 operator wallet on NPC NFT)
        if (message.Contains("payment wallet")
            || message.Contains("Payment wallet")
            || message.Contains("[NpcPaymentWalletService]")
            || message.Contains("[NpcPaymentKeyVault]"))
        {
            return true;
        }

        // x402 / nanopayment runtime
        if (message.Contains("[ArcNanopayment]"))
        {
            return true;
        }

        // ArcTrading contract client lifecycle (TBA, capital top-up, bound trader wallet)
        if (message.Contains("Resolved TBA address")
            || message.Contains("useBoundWalletAsTrader")
            || message.Contains("initial capital")
            || message.Contains("capital top-up")
            || message.Contains("trader wallet"))
        {
            return true;
        }

        // Macro economy agent + world events
        if (message.Contains("[MacroAgent]") || message.Contains("World event"))
        {
            return true;
        }

        // Browser wallet login flow (local HTTP bridge + SIWE session)
        if (message.Contains("[WalletLoginServer]")
            || message.Contains("[WalletLoginService]")
            || message.Contains("[WalletSession]"))
        {
            return true;
        }

        // Player-owned NPC NFT discovery + spawn
        if (message.Contains("[OwnedNpcSpawner]"))
        {
            return true;
        }

        return false;
    }

    private static Color ResolveColor(string message, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) return ErrorColor;
        if (type == LogType.Warning) return WarningColor;

        // Payment / nanopayment infra — surface these distinctly regardless of which NPC emits them
        if (message.Contains("payment wallet") || message.Contains("Payment wallet")
            || message.Contains("[NpcPaymentWalletService]") || message.Contains("[NpcPaymentKeyVault]"))
        {
            return PaymentBindColor;
        }
        if (message.Contains("[ArcNanopayment]") || message.Contains("nanopayment"))
        {
            return NanopaymentColor;
        }
        if (message.Contains("SellNFT") || message.Contains("Withdraw")) return SellTradeColor;
        if (message.Contains("BuyNFT") || message.Contains("Deposit")) return BuyTradeColor;
        if (message.Contains("[MacroAgent]")) return MacroAgentColor;
        if (message.Contains("World event")) return WorldEventColor;

        // Browser wallet login + owned NPC spawn — surface the startup auth flow distinctly
        if (message.Contains("[WalletLoginServer]")
            || message.Contains("[WalletLoginService]")
            || message.Contains("[WalletSession]"))
        {
            return WalletLoginColor;
        }
        if (message.Contains("[OwnedNpcSpawner]")) return OwnedNpcSpawnColor;

        if (message.Contains("Aggressive")) return new Color(1f, 0.38f, 0.36f);
        if (message.Contains("Balanced")) return new Color(0.25f, 0.78f, 1f);
        if (message.Contains("Conservative")) return new Color(0.45f, 0.95f, 0.55f);
        if (message.Contains("Player")) return new Color(0.55f, 0.68f, 1f);
        if (message.Contains("Monster")) return new Color(0.85f, 0.55f, 1f);
        if (message.Contains("Actor")) return new Color(0.72f, 0.76f, 0.84f);

        return ColorForPrefix(message);
    }

    private static Color ColorForPrefix(string message)
    {
        string prefix = message.Split(' ')[0];
        int hash = Mathf.Abs(prefix.GetHashCode());
        float hue = (hash % 360) / 360f;
        return Color.HSVToRGB(hue, 0.55f, 1f);
    }

    private static string Shorten(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength - 3) + "...";
    }

    private static string EscapeRichText(string value)
    {
        return value.Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem");
        DontDestroyOnLoad(eventSystem);
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();
    }
}

public class RuntimeLogConsoleWindowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    private RectTransform rectTransform;
    private Canvas canvas;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        transform.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (rectTransform == null || canvas == null)
        {
            return;
        }

        rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
    }
}

public class RuntimeLogConsoleResizeHandle : MonoBehaviour, IDragHandler
{
    private const float MinWidth = 260f;
    private const float MinHeight = 120f;
    private const float MaxWidth = 1100f;
    private const float MaxHeight = 760f;

    private RectTransform panelRect;
    private Canvas canvas;

    private void Awake()
    {
        panelRect = transform.parent as RectTransform;
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (panelRect == null || canvas == null)
        {
            return;
        }

        Vector2 delta = eventData.delta / canvas.scaleFactor;
        Vector2 size = panelRect.sizeDelta;
        size.x = Mathf.Clamp(size.x - delta.x, MinWidth, MaxWidth);
        size.y = Mathf.Clamp(size.y - delta.y, MinHeight, MaxHeight);
        panelRect.sizeDelta = size;
    }
}
