using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>The final thank-you card, shown once after the fox's reunion.</summary>
[DisallowMultipleComponent]
public class GameCompletionPanel : MonoBehaviour
{
    CanvasGroup m_Group;
    Button m_MenuButton;
    RectTransform m_Card;
    float m_ShownAt;
    bool m_Loading;

    public static void Show()
    {
        if (FindObjectOfType<GameCompletionPanel>() != null)
            return;

        var root = new GameObject("Game Completion Canvas", typeof(RectTransform));
        root.layer = 5;
        root.AddComponent<GameCompletionPanel>().Build();
    }

    void Build()
    {
        var camera = Camera.main;
        bool inVr = XRSettings.isDeviceActive && camera != null;
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = inVr ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = camera;
        canvas.sortingOrder = 200;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
            | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;

        var root = (RectTransform)transform;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        if (inVr)
        {
            root.sizeDelta = new Vector2(1120f, 720f);
            root.localScale = Vector3.one * 0.00135f;
            root.position = camera.transform.position + camera.transform.forward * 2f;
            root.rotation = camera.transform.rotation;
            scaler.dynamicPixelsPerUnit = 10f;
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        }
        else
        {
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            var dimmer = GroveUi.CreateSurface("Dimmer", root, new Color(0.015f, 0.035f, 0.025f, 0.72f));
            GroveUi.Stretch(dimmer.rectTransform);
            dimmer.raycastTarget = true;
        }
        gameObject.AddComponent<GraphicRaycaster>();
        EnsureEventSystem();

        m_Group = gameObject.AddComponent<CanvasGroup>();
        m_Group.alpha = 0f;
        m_Group.blocksRaycasts = true;
        m_ShownAt = Time.unscaledTime;

        var font = TMP_Settings.defaultFontAsset;
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        var panel = GroveUi.CreateSurface("Completion Card", root, GroveUi.Surface, 32f);
        panel.raycastTarget = true;
        SetRect(panel.rectTransform, new Vector2(1040f, 660f), Vector2.zero);
        m_Card = panel.rectTransform;
        m_Card.localScale = Vector3.one * 0.92f;

        var accent = GroveUi.CreateSurface("Accent", panel.transform, GroveUi.Accent, 2f);
        SetRect(accent.rectTransform, new Vector2(72f, 4f), new Vector2(0f, 194f));

        AddText(panel.transform, font, "Progress", "3 / 3 TASKS COMPLETE", 24f,
            GroveUi.Accent, new Vector2(900f, 42f), new Vector2(0f, 252f));
        AddText(panel.transform, font, "Title", "Thank you, forest friend!", 54f,
            GroveUi.Text, new Vector2(940f, 90f), new Vector2(0f, 124f), true);
        AddText(panel.transform, font, "Message",
            "You completed every task and helped the little animals of the grove.\n\nA happy wolf. A healed fawn. Two old friends reunited.",
            29f, GroveUi.Muted, new Vector2(870f, 174f), new Vector2(0f, -24f));
        AddText(panel.transform, font, "Ending", "Your adventure is complete.", 30f,
            GroveUi.Sage, new Vector2(900f, 48f), new Vector2(0f, -154f), true);

        var image = GroveUi.CreateSurface("Return to menu", panel.transform, Color.white, 18f);
        image.raycastTarget = true;
        SetRect(image.rectTransform, new Vector2(420f, 76f), new Vector2(0f, -246f));
        m_MenuButton = image.gameObject.AddComponent<Button>();
        m_MenuButton.targetGraphic = image;
        m_MenuButton.interactable = false;
        image.gameObject.AddComponent<FeedbackButton>();
        m_MenuButton.onClick.AddListener(ReturnToMenu);
        var colors = m_MenuButton.colors;
        colors.normalColor = GroveUi.Sage;
        colors.highlightedColor = new Color(0.87f, 0.94f, 0.80f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.61f, 0.73f, 0.55f);
        colors.disabledColor = GroveUi.Sage;
        m_MenuButton.colors = colors;
        var label = AddText(image.transform, font, "Label", "Return to menu", 29f,
            new Color(0.08f, 0.14f, 0.11f), Vector2.zero, Vector2.zero, true);
        GroveUi.Stretch(label.rectTransform);
        InteractionFeedback.PlayUI(FeedbackCue.GameComplete);
    }

    void Update()
    {
        if (m_Group == null || m_Loading)
            return;

        float elapsed = Time.unscaledTime - m_ShownAt;
        m_Group.alpha = Mathf.Clamp01(elapsed / 0.4f);
        float progress = Mathf.Clamp01(elapsed / 0.55f);
        float settle = 1f - Mathf.Pow(1f - progress, 3f);
        m_Card.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, settle);
        // The input that finishes the dialogue must not also leave the ending screen.
        if (elapsed >= 0.75f && !m_MenuButton.interactable)
            m_MenuButton.interactable = true;
    }

    void ReturnToMenu()
    {
        if (m_Loading || Time.unscaledTime - m_ShownAt < 0.75f)
            return;

        m_Loading = true;
        InteractionFeedback.PlayUI(FeedbackCue.UIClick);
        m_MenuButton.interactable = false;
        SceneManager.LoadScene("MainMenu");
    }

    static void EnsureEventSystem()
    {
        var current = FindObjectOfType<EventSystem>();
        if (current == null)
        {
            var eventObject = new GameObject("Completion EventSystem");
            current = eventObject.AddComponent<EventSystem>();
        }
        // The XR module accepts both desktop pointers and tracked-controller rays.
        if (current.GetComponent<BaseInputModule>() == null)
            current.gameObject.AddComponent<XRUIInputModule>();
    }

    static void SetRect(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    static TextMeshProUGUI AddText(Transform parent, TMP_FontAsset font, string name, string value,
        float size, Color color, Vector2 dimensions, Vector2 position, bool bold = false)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.enableWordWrapping = true;
        text.extraPadding = true;
        text.raycastTarget = false;
        SetRect(text.rectTransform, dimensions, position);
        return text;
    }
}
