using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    [SerializeField] private MarineAgentManager agentManager;

    private Canvas canvas;
    private TMP_Text statusLabel;
    private TMP_Text transcriptText;
    private TMP_Text responseText;
    private TMP_Text errorText;
    private Button talkButton;
    private Button retryButton;
    private Button englishButton;
    private Button germanButton;
    private TMP_Text subtitleText;
    private readonly List<RectTransform> animatedDots = new List<RectTransform>();
    private readonly List<Vector3> dotBaseScales = new List<Vector3>();
    private AgentState currentState = AgentState.Idle;
    private float pulseTime;

    private static readonly Color DeepNavy = ParseColor("#090A0F");     // deep obsidian
    private static readonly Color Ink = ParseColor("#0E1117");          // studio dark
    private static readonly Color Card = new Color(0.1f, 0.12f, 0.16f, 0.5f); // subtle glass
    private static readonly Color CardInner = new Color(0f, 0f, 0f, 0.3f);    // deep inset
    private static readonly Color Teal = ParseColor("#00E5FF");         // vivid neon cyan
    private static readonly Color White = ParseColor("#F0F6FC");        // crisp white

    private static Sprite whiteSprite;
    private static Sprite roundedButtonSprite;
    private static Sprite roundedCardSprite;

    private void Awake()
    {
        EnsureEventSystem();
    }

    private void Start()
    {
        if (agentManager == null)
        {
            agentManager = FindFirstObjectByType<MarineAgentManager>();
        }

        BuildUiIfNeeded();

        if (agentManager != null)
        {
            BindManager(agentManager);
        }
    }

    private void Update()
    {
        if (currentState != AgentState.Listening)
        {
            ResetDots();
            return;
        }

        pulseTime += Time.unscaledDeltaTime * 6f;
        for (int i = 0; i < animatedDots.Count; i++)
        {
            float offset = i * 0.35f;
            float scale = 1f + Mathf.Sin(pulseTime + offset) * 0.18f;
            animatedDots[i].localScale = dotBaseScales[i] * scale;
        }
    }

    public void BindManager(MarineAgentManager manager)
    {
        agentManager = manager;

        if (talkButton != null)
        {
            talkButton.onClick.RemoveAllListeners();
            talkButton.onClick.AddListener(() =>
            {
                if (agentManager != null)
                {
                    agentManager.StartPipeline();
                }
            });
        }

        if (retryButton != null)
        {
            retryButton.onClick.RemoveAllListeners();
            retryButton.onClick.AddListener(() =>
            {
                ClearError();
                agentManager.StartPipeline();
            });
        }

        if (englishButton != null)
        {
            englishButton.onClick.RemoveAllListeners();
            englishButton.onClick.AddListener(() =>
            {
                agentManager.SetLanguage("en");
                UpdateLanguageButtons("en");
            });
        }

        if (germanButton != null)
        {
            germanButton.onClick.RemoveAllListeners();
            germanButton.onClick.AddListener(() =>
            {
                agentManager.SetLanguage("de");
                UpdateLanguageButtons("de");
            });
        }

        agentManager.OnStateChanged.RemoveAllListeners();
        agentManager.OnStateChanged.AddListener(SetState);

        agentManager.OnTranscriptChanged.RemoveAllListeners();
        agentManager.OnTranscriptChanged.AddListener(SetTranscriptText);

        agentManager.OnResponseChanged.RemoveAllListeners();
        agentManager.OnResponseChanged.AddListener(SetResponseText);

        agentManager.OnError.RemoveAllListeners();
        agentManager.OnError.AddListener(ShowGeminiError);

        UpdateLanguageButtons(agentManager.CurrentLanguageCode);
    }

    public void SetState(AgentState state)
    {
        currentState = state;
        if (statusLabel != null)
        {
            statusLabel.text = state switch
            {
                AgentState.Listening => "Listening...",
                AgentState.Thinking => "Thinking...",
                AgentState.Speaking => "Speaking...",
                _ => "Ready"
            };
        }

        if (state != AgentState.Listening)
        {
            ResetDots();
        }
    }

    public void SetTranscriptText(string text)
    {
        if (transcriptText != null)
        {
            transcriptText.text = string.IsNullOrWhiteSpace(text) ? "Transcript will appear here." : text;
        }
    }

    public void SetResponseText(string text)
    {
        if (responseText != null)
        {
            responseText.text = string.IsNullOrWhiteSpace(text) ? "Gemini response will appear here." : text;
        }
    }

    public void ShowTranscriptHint(string message)
    {
        ClearError();
        SetTranscriptText(message);
        SetState(AgentState.Idle);
    }

    public void ShowGeminiError(string errorMessage)
    {
        if (errorText != null)
        {
            errorText.gameObject.SetActive(true);
            errorText.text = errorMessage;
        }

        if (retryButton != null)
        {
            retryButton.gameObject.SetActive(true);
        }

        if (talkButton != null)
        {
            talkButton.gameObject.SetActive(false);
        }
    }

    public void ShowTtsFallback(string response)
    {
        SetResponseText(response);
        ClearError();
    }

    public void ClearError()
    {
        if (errorText != null)
        {
            errorText.gameObject.SetActive(false);
            errorText.text = string.Empty;
        }

        if (retryButton != null)
        {
            retryButton.gameObject.SetActive(false);
        }

        if (talkButton != null)
        {
            talkButton.gameObject.SetActive(true);
        }
    }

    private void BuildUiIfNeeded()
    {
        if (canvas != null)
        {
            return;
        }

        GameObject canvasObject = new GameObject("MarineAI_Canvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasObject.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.anchorMin = Vector2.zero;
        canvasRect.anchorMax = Vector2.one;
        canvasRect.offsetMin = Vector2.zero;
        canvasRect.offsetMax = Vector2.zero;

        GameObject bg = CreatePanel("Background", canvasObject.transform, Ink);
        StretchFullScreen(bg.GetComponent<RectTransform>());

        GameObject oceanBand = CreatePanel("OceanBand", canvasObject.transform, new Color(0f, 0.8f, 0.9f, 0.05f));
        RectTransform oceanBandRect = oceanBand.GetComponent<RectTransform>();
        oceanBandRect.anchorMin = new Vector2(0f, 0.74f);
        oceanBandRect.anchorMax = new Vector2(1f, 1f);
        oceanBandRect.offsetMin = Vector2.zero;
        oceanBandRect.offsetMax = Vector2.zero;

        GameObject shell = CreateRoundedPanel("Shell", canvasObject.transform, new Color(0.06f, 0.08f, 0.12f, 0.85f));
        RectTransform shellRect = shell.GetComponent<RectTransform>();
        shellRect.anchorMin = new Vector2(0.15f, 0.10f);
        shellRect.anchorMax = new Vector2(0.85f, 0.90f);
        shellRect.offsetMin = Vector2.zero;
        shellRect.offsetMax = Vector2.zero;

        CreateHeader(shell.transform);
        CreateLanguageSelector(shell.transform);
        CreateStatusSection(shell.transform);
        CreateTranscriptSection(shell.transform);
        CreateResponseSection(shell.transform);
        CreateControlRow(shell.transform);
    }

    private void CreateHeader(Transform parent)
    {
        GameObject header = CreateText("Title", parent, "MarineAI", 46, White, TextAlignmentOptions.Left);
        TMP_Text headerText = header.GetComponent<TMP_Text>();
        Vector4 currentMargin = headerText.margin;
        headerText.margin = new Vector4(currentMargin.x, currentMargin.y, currentMargin.z, 20f); // Increase bottom padding

        RectTransform rect = header.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.06f, 0.88f);
        rect.anchorMax = new Vector2(0.94f, 0.98f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        GameObject subtitle = CreateText("Subtitle", parent, "Deepgram voice + Gemini", 16, ParseColor("#8B949E"), TextAlignmentOptions.Left);
        subtitleText = subtitle.GetComponent<TMP_Text>();
        subtitleText.margin = new Vector4(10f, 0f, 10f, 0f);
        
        RectTransform subtitleRect = subtitle.GetComponent<RectTransform>();
        subtitleRect.anchorMin = new Vector2(0.06f, 0.83f);
        subtitleRect.anchorMax = new Vector2(0.50f, 0.88f);
        subtitleRect.offsetMin = Vector2.zero;
        subtitleRect.offsetMax = Vector2.zero;
    }

    private void CreateLanguageSelector(Transform parent)
    {
        GameObject selector = CreateRoundedPanel("LanguageSelector", parent, Card);
        RectTransform selectorRect = selector.GetComponent<RectTransform>();
        selectorRect.anchorMin = new Vector2(0.55f, 0.88f);
        selectorRect.anchorMax = new Vector2(0.72f, 0.94f);
        selectorRect.offsetMin = Vector2.zero;
        selectorRect.offsetMax = Vector2.zero;

        englishButton = CreateLanguageButton("EnglishButton", selector.transform, "EN");
        RectTransform englishRect = englishButton.GetComponent<RectTransform>();
        englishRect.anchorMin = new Vector2(0.05f, 0.12f);
        englishRect.anchorMax = new Vector2(0.48f, 0.88f);
        englishRect.offsetMin = Vector2.zero;
        englishRect.offsetMax = Vector2.zero;

        germanButton = CreateLanguageButton("GermanButton", selector.transform, "DE");
        RectTransform germanRect = germanButton.GetComponent<RectTransform>();
        germanRect.anchorMin = new Vector2(0.52f, 0.12f);
        germanRect.anchorMax = new Vector2(0.95f, 0.88f);
        germanRect.offsetMin = Vector2.zero;
        germanRect.offsetMax = Vector2.zero;

        englishButton.onClick.AddListener(() =>
        {
            agentManager?.SetLanguage("en");
            UpdateLanguageButtons("en");
        });
        germanButton.onClick.AddListener(() =>
        {
            agentManager?.SetLanguage("de");
            UpdateLanguageButtons("de");
        });

        UpdateLanguageButtons("de");
    }

    private void CreateStatusSection(Transform parent)
    {
        GameObject statusPill = CreateRoundedPanel("StatusPill", parent, Card);
        RectTransform pillRect = statusPill.GetComponent<RectTransform>();
        pillRect.anchorMin = new Vector2(0.75f, 0.88f);
        pillRect.anchorMax = new Vector2(0.94f, 0.94f);
        pillRect.offsetMin = Vector2.zero;
        pillRect.offsetMax = Vector2.zero;

        GameObject status = CreateText("Status", statusPill.transform, "Ready", 18, White, TextAlignmentOptions.Left);
        statusLabel = status.GetComponent<TMP_Text>();
        statusLabel.enableWordWrapping = false;
        statusLabel.margin = new Vector4(8f, 0f, 0f, 0f);

        RectTransform rect = status.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.05f, 0.05f);
        rect.anchorMax = new Vector2(0.65f, 0.95f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        GameObject dotsContainer = new GameObject("ListeningDots", typeof(RectTransform));
        dotsContainer.transform.SetParent(statusPill.transform, false);
        RectTransform dotsRect = dotsContainer.GetComponent<RectTransform>();
        dotsRect.anchorMin = new Vector2(0.66f, 0.22f);
        dotsRect.anchorMax = new Vector2(0.94f, 0.78f);
        dotsRect.offsetMin = Vector2.zero;
        dotsRect.offsetMax = Vector2.zero;

        for (int i = 0; i < 3; i++)
        {
            GameObject dot = new GameObject($"Dot_{i}", typeof(RectTransform), typeof(Image));
            dot.transform.SetParent(dotsContainer.transform, false);
            Image image = dot.GetComponent<Image>();
            image.sprite = GetWhiteSprite();
            image.color = Teal;
            RectTransform dotRect = dot.GetComponent<RectTransform>();
            dotRect.anchorMin = dotRect.anchorMax = new Vector2(0.2f + i * 0.3f, 0.5f);
            dotRect.sizeDelta = new Vector2(14, 14);
            dotBaseScales.Add(Vector3.one);
            animatedDots.Add(dotRect);
        }
    }

    private void CreateTranscriptSection(Transform parent)
    {
        CreateSectionFrame("TranscriptPanel", parent, new Vector2(0.06f, 0.48f), new Vector2(0.94f, 0.80f), "Transcript", "Transcript will appear here.", out transcriptText);
    }

    private void CreateResponseSection(Transform parent)
    {
        GameObject responsePanel = CreateSectionFrame("ResponsePanel", parent, new Vector2(0.06f, 0.15f), new Vector2(0.94f, 0.46f), "Response", "Gemini response will appear here.", out responseText);

        GameObject errorObject = CreateText("ErrorText", responsePanel.transform, string.Empty, 18, new Color(1f, 0.55f, 0.55f), TextAlignmentOptions.Left);
        RectTransform rect = errorObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.04f, 0.02f);
        rect.anchorMax = new Vector2(0.96f, 0.18f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        errorText = errorObject.GetComponent<TMP_Text>();
        errorText.gameObject.SetActive(false);
    }

    private void CreateControlRow(Transform parent)
    {
        GameObject row = new GameObject("ControlRow", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        RectTransform rowRect = row.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0.35f, 0.04f);
        rowRect.anchorMax = new Vector2(0.65f, 0.12f);
        rowRect.offsetMin = Vector2.zero;
        rowRect.offsetMax = Vector2.zero;

        talkButton = CreateButton("TalkButton", row.transform, "Talk");
        RectTransform talkRect = talkButton.GetComponent<RectTransform>();
        talkRect.anchorMin = new Vector2(0f, 0f);
        talkRect.anchorMax = new Vector2(1f, 1f);
        talkRect.offsetMin = Vector2.zero;
        talkRect.offsetMax = Vector2.zero;
        talkButton.onClick.AddListener(() =>
        {
            if (agentManager != null)
            {
                agentManager.StartPipeline();
            }
        });

        retryButton = CreateButton("RetryButton", row.transform, "Retry");
        RectTransform retryRect = retryButton.GetComponent<RectTransform>();
        retryRect.anchorMin = new Vector2(0f, 0f);
        retryRect.anchorMax = new Vector2(1f, 1f);
        retryRect.offsetMin = Vector2.zero;
        retryRect.offsetMax = Vector2.zero;
        retryButton.gameObject.SetActive(false);
        retryButton.onClick.AddListener(() =>
        {
            ClearError();
            if (agentManager != null)
            {
                agentManager.StartPipeline();
            }
        });
    }

    private GameObject CreateSectionFrame(string name, Transform parent, Vector2 minAnchor, Vector2 maxAnchor, string label, string initialText, out TMP_Text bodyText)
    {
        GameObject frame = CreateRoundedPanel(name, parent, Card);
        RectTransform rect = frame.GetComponent<RectTransform>();
        rect.anchorMin = minAnchor;
        rect.anchorMax = maxAnchor;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        GameObject accent = CreateRoundedPanel($"{name}_Accent", frame.transform, Teal);
        RectTransform accentRect = accent.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0.02f, 0.82f);
        accentRect.anchorMax = new Vector2(0.025f, 0.92f);
        accentRect.offsetMin = Vector2.zero;
        accentRect.offsetMax = Vector2.zero;

        GameObject title = CreateText($"{name}_Label", frame.transform, label, 18, ParseColor("#8B949E"), TextAlignmentOptions.Left);
        TMP_Text tmpTitle = title.GetComponent<TMP_Text>();
        tmpTitle.margin = new Vector4(12f, 0f, 0f, 0f);
        tmpTitle.enableWordWrapping = false;
        
        RectTransform titleRect = title.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.02f, 0.78f);
        titleRect.anchorMax = new Vector2(0.5f, 0.96f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;

        bodyText = CreateScrollView(frame.transform, initialText, out RectTransform scrollRect);
        scrollRect.anchorMin = new Vector2(0.02f, 0.05f);
        scrollRect.anchorMax = new Vector2(0.98f, 0.75f);
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = Vector2.zero;

        return frame;
    }

    private TMP_Text CreateScrollView(Transform parent, string initialText, out RectTransform scrollRectTransform)
    {
        GameObject scrollView = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollView.transform.SetParent(parent, false);
        Image image = scrollView.GetComponent<Image>();
        image.sprite = GetRoundedCardSprite();
        image.type = Image.Type.Sliced;
        image.color = CardInner;
        ScrollRect scrollRect = scrollView.GetComponent<ScrollRect>();

        scrollRectTransform = scrollView.GetComponent<RectTransform>();
        scrollRectTransform.anchorMin = Vector2.zero;
        scrollRectTransform.anchorMax = Vector2.one;
        scrollRectTransform.offsetMin = Vector2.zero;
        scrollRectTransform.offsetMax = Vector2.zero;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollView.transform, false);
        Image viewportImage = viewport.GetComponent<Image>();
        viewportImage.sprite = GetWhiteSprite();
        viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
        Mask mask = viewport.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;

        TMP_Text body = CreateBodyText(viewport.transform, initialText);
        RectTransform textRect = body.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.offsetMin = new Vector2(16f, 0f);
        textRect.offsetMax = new Vector2(-16f, 0f);
        textRect.anchoredPosition = new Vector2(0f, -8f);

        body.alignment = TextAlignmentOptions.TopLeft;
        body.enableWordWrapping = true;
        body.margin = new Vector4(4f, 8f, 4f, 4f);
        body.fontSize = 18;

        scrollRect.viewport = viewportRect;
        scrollRect.content = textRect;
        scrollRect.horizontal = false;
        scrollRect.onValueChanged.AddListener(_ => { });

        return body;
    }

    private TMP_Text CreateBodyText(Transform parent, string text)
    {
        GameObject go = new GameObject("BodyText", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 20;
        tmp.color = White;
        tmp.enableWordWrapping = true;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.margin = new Vector4(16f, 16f, 16f, 16f);
        ContentSizeFitter fitter = go.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return tmp;
    }

    private GameObject CreateText(string name, Transform parent, string text, int size, Color color, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TMP_Text tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = true;
        tmp.margin = new Vector4(10f, 6f, 10f, 6f);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(100, 30);
        return go;
    }

    private GameObject CreatePanel(string name, Transform parent, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        Image image = panel.GetComponent<Image>();
        image.sprite = GetWhiteSprite();
        image.color = color;
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return panel;
    }

    private GameObject CreateRoundedPanel(string name, Transform parent, Color color)
    {
        GameObject panel = new GameObject(name, typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(parent, false);
        Image image = panel.GetComponent<Image>();
        image.sprite = GetRoundedCardSprite();
        image.type = Image.Type.Sliced;
        image.color = color;
        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return panel;
    }

    private Button CreateButton(string name, Transform parent, string label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.sprite = GetRoundedButtonSprite();
        image.type = Image.Type.Sliced;
        image.color = Teal;
        Button button = go.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Teal;
        colors.highlightedColor = ParseColor("#A6FFEC");
        colors.pressedColor = ParseColor("#38B294");
        button.colors = colors;

        GameObject text = CreateText($"{name}_Text", go.transform, label, 22, Ink, TextAlignmentOptions.Center);
        TMP_Text tmpText = text.GetComponent<TMP_Text>();
        tmpText.margin = Vector4.zero;
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }

    private Button CreateLanguageButton(string name, Transform parent, string label)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.sprite = GetRoundedButtonSprite();
        image.type = Image.Type.Sliced;

        Button button = go.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0.05f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.1f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.15f);
        button.colors = colors;

        GameObject text = CreateText($"{name}_Text", go.transform, label, 16, ParseColor("#8892B0"), TextAlignmentOptions.Center);
        TMP_Text tmpText = text.GetComponent<TMP_Text>();
        tmpText.enableWordWrapping = false;
        tmpText.margin = Vector4.zero;
        
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        return button;
    }

    private void UpdateLanguageButtons(string languageCode)
    {
        bool englishSelected = languageCode == "en";
        SetLanguageButtonState(englishButton, englishSelected);
        SetLanguageButtonState(germanButton, !englishSelected);
    }

    private void SetLanguageButtonState(Button button, bool selected)
    {
        if (button == null)
        {
            return;
        }

        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = selected ? Teal : new Color(1f, 1f, 1f, 0.05f);
        }

        TMP_Text text = button.GetComponentInChildren<TMP_Text>();
        if (text != null)
        {
            text.color = selected ? Ink : ParseColor("#8892B0");
        }
    }

    private void StretchFullScreen(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void ResetDots()
    {
        for (int i = 0; i < animatedDots.Count; i++)
        {
            animatedDots[i].localScale = dotBaseScales[i];
        }
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<StandaloneInputModule>();
    }

    private static Color ParseColor(string html)
    {
        ColorUtility.TryParseHtmlString(html, out Color color);
        return color;
    }

    private static Sprite GetWhiteSprite()
    {
        if (whiteSprite == null)
        {
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
        }

        return whiteSprite;
    }

    private static Sprite GetRoundedButtonSprite()
    {
        if (roundedButtonSprite != null)
        {
            return roundedButtonSprite;
        }

        const int size = 128;
        const int radius = 28;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inside =
                    (x >= radius && x < size - radius) ||
                    (y >= radius && y < size - radius) ||
                    Vector2.Distance(new Vector2(x, y), new Vector2(radius, radius)) <= radius ||
                    Vector2.Distance(new Vector2(x, y), new Vector2(size - radius - 1, radius)) <= radius ||
                    Vector2.Distance(new Vector2(x, y), new Vector2(radius, size - radius - 1)) <= radius ||
                    Vector2.Distance(new Vector2(x, y), new Vector2(size - radius - 1, size - radius - 1)) <= radius;

                texture.SetPixel(x, y, inside ? Color.white : new Color(1f, 1f, 1f, 0f));
            }
        }

        texture.Apply();
        roundedButtonSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return roundedButtonSprite;
    }

    private static Sprite GetRoundedCardSprite()
    {
        if (roundedCardSprite != null)
        {
            return roundedCardSprite;
        }

        const int size = 128;
        const int radius = 14;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool inside =
                    (x >= radius && x < size - radius) ||
                    (y >= radius && y < size - radius) ||
                    Vector2.Distance(new Vector2(x, y), new Vector2(radius, radius)) <= radius ||
                    Vector2.Distance(new Vector2(x, y), new Vector2(size - radius - 1, radius)) <= radius ||
                    Vector2.Distance(new Vector2(x, y), new Vector2(radius, size - radius - 1)) <= radius ||
                    Vector2.Distance(new Vector2(x, y), new Vector2(size - radius - 1, size - radius - 1)) <= radius;

                texture.SetPixel(x, y, inside ? Color.white : new Color(1f, 1f, 1f, 0f));
            }
        }

        texture.Apply();
        roundedCardSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        return roundedCardSprite;
    }
}
