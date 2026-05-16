using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class NpcDemoScrollViewPopup : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private NpcDemoUiDataSource dataSource;
    [SerializeField] private float refreshInterval = 0.5f;

    [Header("Auto Build UI")]
    [SerializeField] private bool buildUiOnAwake = true;
    [SerializeField] private Font uiFont;

    [Header("NPC ScrollView")]
    [SerializeField] private ScrollRect npcScrollRect;
    [SerializeField] private RectTransform npcButtonContainer;
    [SerializeField] private Button npcButtonPrefab;

    [Header("Popup")]
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private Text popupTitleText;
    [SerializeField] private Text popupSummaryText;
    [SerializeField] private Text popupFieldText;
    [SerializeField] private Text popupActivityText;
    [SerializeField] private Button closePopupButton;
    [SerializeField] private Button followButton;
    [SerializeField] private Button focusButton;

    private readonly List<GameObject> spawnedNpcButtons = new List<GameObject>();
    private readonly StringBuilder builder = new StringBuilder(4096);
    private float nextRefreshTime;
    private bool followEnabled;

    private void Awake()
    {
        if (uiFont == null)
        {
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        if (dataSource == null)
        {
            dataSource = FindObjectOfType<NpcDemoUiDataSource>();
        }

        if (dataSource == null)
        {
            dataSource = gameObject.AddComponent<NpcDemoUiDataSource>();
        }

        if (buildUiOnAwake)
        {
            EnsureRuntimeUi();
        }

        BindStaticButtons();
        HidePopup();
    }

    private void OnEnable()
    {
        if (dataSource != null)
        {
            dataSource.NpcListChanged += RebuildNpcButtons;
            dataSource.SelectedNpcChanged += ShowSnapshotPopup;
            dataSource.SelectedNpcSnapshotRefreshed += RenderSnapshot;
        }

        RefreshNpcList();
    }

    private void OnDisable()
    {
        if (dataSource != null)
        {
            dataSource.NpcListChanged -= RebuildNpcButtons;
            dataSource.SelectedNpcChanged -= ShowSnapshotPopup;
            dataSource.SelectedNpcSnapshotRefreshed -= RenderSnapshot;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
        RefreshNpcList();

        if (popupRoot != null && popupRoot.activeSelf)
        {
            dataSource.RefreshSelectedNpcDetails();
        }
    }

    public void RefreshNpcList()
    {
        if (dataSource != null)
        {
            dataSource.RefreshNpcList();
        }
    }

    private void RebuildNpcButtons(List<NpcListEntry> entries)
    {
        if (npcButtonContainer == null)
        {
            return;
        }

        for (int i = 0; i < spawnedNpcButtons.Count; i++)
        {
            if (spawnedNpcButtons[i] != null)
            {
                Destroy(spawnedNpcButtons[i]);
            }
        }

        spawnedNpcButtons.Clear();

        for (int i = 0; i < entries.Count; i++)
        {
            NpcListEntry entry = entries[i];
            Button button = CreateNpcButton(entry, i);
            spawnedNpcButtons.Add(button.gameObject);
        }
    }

    private Button CreateNpcButton(NpcListEntry entry, int index)
    {
        Button button;
        if (npcButtonPrefab != null)
        {
            button = Instantiate(npcButtonPrefab, npcButtonContainer);
            button.gameObject.SetActive(true);
        }
        else
        {
            button = CreateDefaultNpcButton(npcButtonContainer);
        }

        button.name = $"NpcButton_{index}_{entry.displayName}";

        Text label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.text = BuildNpcButtonLabel(entry);
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => OnNpcButtonClicked(entry));
        return button;
    }

    private string BuildNpcButtonLabel(NpcListEntry entry)
    {
        string address = string.IsNullOrEmpty(entry.walletAddress) ? "wallet initializing" : ShortAddress(entry.walletAddress);
        return $"{entry.displayName}\n{entry.archetype} | {entry.totalUSDC:0.####} USDC\n{address}\n{entry.currentActivity}";
    }

    private void OnNpcButtonClicked(NpcListEntry entry)
    {
        if (dataSource == null || entry == null)
        {
            return;
        }

        TradingNpcSnapshot snapshot = dataSource.SelectNpc(entry.npc);
        ShowSnapshotPopup(snapshot);
    }

    private void ShowSnapshotPopup(TradingNpcSnapshot snapshot)
    {
        if (popupRoot != null)
        {
            popupRoot.SetActive(true);
        }

        RenderSnapshot(snapshot);
    }

    private void RenderSnapshot(TradingNpcSnapshot snapshot)
    {
        if (snapshot == null)
        {
            HidePopup();
            return;
        }

        if (popupTitleText != null)
        {
            popupTitleText.text = $"{snapshot.displayName} ({snapshot.archetype})";
        }

        if (popupSummaryText != null)
        {
            popupSummaryText.text = BuildSummaryText(snapshot);
        }

        if (popupFieldText != null)
        {
            popupFieldText.text = BuildFieldText(snapshot);
        }

        if (popupActivityText != null)
        {
            popupActivityText.text = BuildActivityText(snapshot);
        }

        if (followButton != null)
        {
            Text followText = followButton.GetComponentInChildren<Text>(true);
            if (followText != null)
            {
                followText.text = followEnabled ? "Stop Follow" : "Follow NPC";
            }
        }
    }

    private string BuildSummaryText(TradingNpcSnapshot snapshot)
    {
        builder.Length = 0;
        builder.AppendLine($"Wallet: {NullText(snapshot.walletAddress)}");
        builder.AppendLine($"Position: {snapshot.worldPosition.x:0.##}, {snapshot.worldPosition.y:0.##}, {snapshot.worldPosition.z:0.##}");
        builder.AppendLine($"Current: {NullText(snapshot.currentActivity)}");
        builder.AppendLine($"Chain Action Running: {snapshot.isRunningChainAction}");
        return builder.ToString();
    }

    private string BuildFieldText(TradingNpcSnapshot snapshot)
    {
        builder.Length = 0;
        string currentGroup = null;

        for (int i = 0; i < snapshot.fields.Count; i++)
        {
            NpcFieldDisplayInfo field = snapshot.fields[i];
            if (field.group != currentGroup)
            {
                currentGroup = field.group;
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine($"[{currentGroup}]");
            }

            builder.AppendLine($"{field.displayName}: {field.value}");
            builder.AppendLine($"  {field.comment}");
        }

        return builder.ToString();
    }

    private string BuildActivityText(TradingNpcSnapshot snapshot)
    {
        builder.Length = 0;

        if (snapshot.recentActivities.Count == 0)
        {
            builder.AppendLine("No activity yet.");
            return builder.ToString();
        }

        for (int i = 0; i < snapshot.recentActivities.Count; i++)
        {
            TradingNpcActivityRecord activity = snapshot.recentActivities[i];
            builder.AppendLine($"[{activity.gameTime:0.0}s] {activity.type} - {activity.title}");

            if (!string.IsNullOrEmpty(activity.details))
            {
                builder.AppendLine($"  {activity.details}");
            }

            if (activity.amountUSDC > 0f)
            {
                builder.AppendLine($"  Amount: {activity.amountUSDC:0.####} USDC");
            }

            if (!string.IsNullOrEmpty(activity.txHash))
            {
                builder.AppendLine($"  Tx: {activity.txHash}");
            }

            builder.AppendLine($"  Pos: {activity.worldPosition.x:0.##}, {activity.worldPosition.y:0.##}, {activity.worldPosition.z:0.##}");
        }

        return builder.ToString();
    }

    private void BindStaticButtons()
    {
        if (closePopupButton != null)
        {
            closePopupButton.onClick.RemoveAllListeners();
            closePopupButton.onClick.AddListener(HidePopup);
        }

        if (followButton != null)
        {
            followButton.onClick.RemoveAllListeners();
            followButton.onClick.AddListener(ToggleFollowSelectedNpc);
        }

        if (focusButton != null)
        {
            focusButton.onClick.RemoveAllListeners();
            focusButton.onClick.AddListener(() => dataSource.FocusSelectedNpcOnce());
        }
    }

    private void ToggleFollowSelectedNpc()
    {
        followEnabled = !followEnabled;
        dataSource.SetFollowSelectedNpc(followEnabled);
        dataSource.FocusSelectedNpcOnce();
        RenderSnapshot(dataSource.SelectedSnapshot);
    }

    private void HidePopup()
    {
        if (popupRoot != null)
        {
            popupRoot.SetActive(false);
        }
    }

    private void EnsureRuntimeUi()
    {
        EnsureEventSystem();

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("Npc Demo UI Canvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.AddComponent<GraphicRaycaster>();
            transform.SetParent(canvasObject.transform, false);
        }

        if (npcScrollRect == null || npcButtonContainer == null)
        {
            CreateNpcScrollView(canvas.transform);
        }

        if (popupRoot == null)
        {
            CreatePopup(canvas.transform);
        }
    }

    private void CreateNpcScrollView(Transform parent)
    {
        GameObject panel = CreatePanel("NPC ScrollView Panel", parent, new Color(0.05f, 0.06f, 0.07f, 0.88f));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        SetAnchors(panelRect, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(16f, 16f), new Vector2(336f, -16f));

        Text title = CreateText("Title", panel.transform, "NPC List", 18, FontStyle.Bold, TextAnchor.MiddleLeft);
        RectTransform titleRect = title.rectTransform;
        SetAnchors(titleRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -42f), new Vector2(-12f, -10f));

        GameObject scrollObject = new GameObject("NPC ScrollView");
        scrollObject.transform.SetParent(panel.transform, false);
        npcScrollRect = scrollObject.AddComponent<ScrollRect>();
        Image scrollImage = scrollObject.AddComponent<Image>();
        scrollImage.color = new Color(0f, 0f, 0f, 0.2f);
        RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
        SetAnchors(scrollRectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(10f, 10f), new Vector2(-10f, -52f));

        GameObject viewportObject = new GameObject("Viewport");
        viewportObject.transform.SetParent(scrollObject.transform, false);
        Image viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.05f);
        Mask mask = viewportObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        SetFullStretch(viewportRect);

        GameObject contentObject = new GameObject("Content");
        contentObject.transform.SetParent(viewportObject.transform, false);
        npcButtonContainer = contentObject.AddComponent<RectTransform>();
        npcButtonContainer.anchorMin = new Vector2(0f, 1f);
        npcButtonContainer.anchorMax = new Vector2(1f, 1f);
        npcButtonContainer.pivot = new Vector2(0.5f, 1f);
        npcButtonContainer.anchoredPosition = Vector2.zero;
        npcButtonContainer.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        npcScrollRect.viewport = viewportRect;
        npcScrollRect.content = npcButtonContainer;
        npcScrollRect.horizontal = false;
        npcScrollRect.vertical = true;
        npcScrollRect.movementType = ScrollRect.MovementType.Clamped;
    }

    private Button CreateDefaultNpcButton(Transform parent)
    {
        GameObject buttonObject = CreatePanel("NPC Button", parent, new Color(0.12f, 0.16f, 0.18f, 0.96f));
        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 94f;
        layout.minHeight = 86f;

        Image image = buttonObject.GetComponent<Image>();
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.12f, 0.16f, 0.18f, 0.96f);
        colors.highlightedColor = new Color(0.18f, 0.25f, 0.28f, 1f);
        colors.pressedColor = new Color(0.08f, 0.12f, 0.14f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        Text label = CreateText("Label", buttonObject.transform, "", 13, FontStyle.Normal, TextAnchor.MiddleLeft);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        SetFullStretch(label.rectTransform, 10f, 8f, -10f, -8f);
        return button;
    }

    private void CreatePopup(Transform parent)
    {
        popupRoot = CreatePanel("NPC Detail Popup", parent, new Color(0.02f, 0.025f, 0.03f, 0.94f));
        RectTransform popupRect = popupRoot.GetComponent<RectTransform>();
        SetAnchors(popupRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-330f, -290f), new Vector2(330f, 290f));

        popupTitleText = CreateText("Title", popupRoot.transform, "NPC", 20, FontStyle.Bold, TextAnchor.MiddleLeft);
        SetAnchors(popupTitleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -48f), new Vector2(-156f, -12f));

        closePopupButton = CreateSmallButton("Close Button", popupRoot.transform, "Close");
        SetAnchors(closePopupButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-82f, -44f), new Vector2(-18f, -12f));

        popupSummaryText = CreateText("Summary", popupRoot.transform, "", 13, FontStyle.Normal, TextAnchor.UpperLeft);
        SetAnchors(popupSummaryText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -134f), new Vector2(-18f, -56f));

        followButton = CreateSmallButton("Follow Button", popupRoot.transform, "Follow NPC");
        SetAnchors(followButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -174f), new Vector2(128f, -140f));

        focusButton = CreateSmallButton("Focus Button", popupRoot.transform, "Focus Once");
        SetAnchors(focusButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(138f, -174f), new Vector2(248f, -140f));

        GameObject fieldScroll = CreateTextScrollArea(
            "Fields Scroll",
            popupRoot.transform,
            new Vector2(0f, 0f),
            new Vector2(0.5f, 1f),
            new Vector2(18f, 18f),
            new Vector2(-8f, -188f),
            out popupFieldText);
        fieldScroll.name = "Config State Scroll";

        GameObject activityScroll = CreateTextScrollArea(
            "Activity Scroll",
            popupRoot.transform,
            new Vector2(0.5f, 0f),
            new Vector2(1f, 1f),
            new Vector2(8f, 18f),
            new Vector2(-18f, -188f),
            out popupActivityText);
        activityScroll.name = "Activity Feed Scroll";

        BindStaticButtons();
    }

    private GameObject CreateTextScrollArea(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        out Text text)
    {
        GameObject scrollObject = CreatePanel(name, parent, new Color(0f, 0f, 0f, 0.24f));
        RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
        SetAnchors(scrollRectTransform, anchorMin, anchorMax, offsetMin, offsetMax);

        ScrollRect scrollRect = scrollObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        GameObject viewportObject = new GameObject("Viewport");
        viewportObject.transform.SetParent(scrollObject.transform, false);
        viewportObject.AddComponent<RectTransform>();
        Mask mask = viewportObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        Image viewportImage = viewportObject.AddComponent<Image>();
        viewportImage.color = Color.clear;
        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        SetFullStretch(viewportRect, 8f, 8f, -8f, -8f);

        GameObject contentObject = new GameObject("Content");
        contentObject.transform.SetParent(viewportObject.transform, false);
        RectTransform contentRect = contentObject.AddComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        text = CreateText("Text", contentObject.transform, "", 12, FontStyle.Normal, TextAnchor.UpperLeft);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        LayoutElement textLayout = text.gameObject.AddComponent<LayoutElement>();
        textLayout.minHeight = 200f;

        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        return scrollObject;
    }

    private Button CreateSmallButton(string name, Transform parent, string labelText)
    {
        GameObject buttonObject = CreatePanel(name, parent, new Color(0.16f, 0.2f, 0.22f, 1f));
        Image image = buttonObject.GetComponent<Image>();
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;

        Text label = CreateText("Label", buttonObject.transform, labelText, 12, FontStyle.Bold, TextAnchor.MiddleCenter);
        label.raycastTarget = false;
        SetFullStretch(label.rectTransform);
        return button;
    }

    private GameObject CreatePanel(string name, Transform parent, Color color)
    {
        GameObject panelObject = new GameObject(name);
        panelObject.transform.SetParent(parent, false);
        RectTransform rect = panelObject.AddComponent<RectTransform>();
        rect.localScale = Vector3.one;

        Image image = panelObject.AddComponent<Image>();
        image.color = color;
        return panelObject;
    }

    private Text CreateText(string name, Transform parent, string textValue, int fontSize, FontStyle style, TextAnchor anchor)
    {
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.AddComponent<RectTransform>();
        rect.localScale = Vector3.one;

        Text text = textObject.AddComponent<Text>();
        text.font = uiFont;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = anchor;
        text.color = new Color(0.92f, 0.95f, 0.96f, 1f);
        text.text = textValue;
        return text;
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<StandaloneInputModule>();
    }

    private static string ShortAddress(string address)
    {
        if (string.IsNullOrEmpty(address) || address.Length <= 12)
        {
            return address;
        }

        return $"{address.Substring(0, 6)}...{address.Substring(address.Length - 4)}";
    }

    private static string NullText(string value)
    {
        return string.IsNullOrEmpty(value) ? "-" : value;
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

    private static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
