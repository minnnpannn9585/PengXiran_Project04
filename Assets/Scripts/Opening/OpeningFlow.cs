using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>
/// Main menu and the opening subtitle sequence. Loads the forest scene when the story ends.
/// </summary>
public class OpeningFlow : MonoBehaviour
{
    const string GameSceneName = "DemoScene_01";

    static readonly string[] Story =
    {
        "You are an elf of this forest.",
        "Little animals hide in the leaves, the grass, and the quiet hollows.",
        "Some of them are lost or hurt. Find them, and see what they need.",
        "A wolf is searching for his ball. A fawn cannot walk on a wounded leg.",
        "Help them. When they are safe, they remain in a place of their own.",
        "In time, a grateful animal may greet you with a wild fruit, or a flower.",
        "Step softly. The grove is awake."
    };

    CanvasGroup m_MenuGroup;
    CanvasGroup m_StoryGroup;
    SceneFade m_Fade;
    TextMeshProUGUI m_StoryText;
    TextMeshProUGUI m_StoryHint;
    TextMeshProUGUI m_StoryCount;
    Button m_ContinueButton;

    int m_Line = -1;
    int m_ShownLine = int.MinValue;
    float m_VisibleCharacters;
    bool m_LineComplete = true;
    bool m_StoryActive;
    bool m_Loading;
    bool m_TriggerWasDown;
    float m_NextInputTime;

    void Awake()
    {
        var camera = Camera.main;
        if (camera == null)
            camera = CreateFallbackCamera();

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.06f, 0.045f);
        EnsureEventSystem();
        BuildUi(camera);
        BuildFade();
    }

    void Update()
    {
        if (!m_StoryActive || m_Loading)
            return;

        TickTypewriter();
        bool pressed = ReadAdvance();
        if (pressed && Time.unscaledTime >= m_NextInputTime)
            Advance();
    }

    public void Begin()
    {
        if (m_StoryActive || m_Loading)
            return;

        m_MenuGroup.alpha = 0f;
        m_MenuGroup.interactable = false;
        m_MenuGroup.blocksRaycasts = false;
        m_StoryGroup.alpha = 1f;
        m_StoryGroup.interactable = true;
        m_StoryGroup.blocksRaycasts = true;
        m_StoryActive = true;
        m_Line = -1;
        m_ShownLine = int.MinValue;
        m_NextInputTime = 0f;
        Advance();
        m_NextInputTime = Time.unscaledTime + 0.35f;
        if (m_ContinueButton != null)
            m_ContinueButton.Select();
    }

    public void Leave()
    {
        if (m_Loading)
            return;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void Skip()
    {
        if (!m_StoryActive || m_Loading)
            return;

        EnterGame();
    }

    public void Advance()
    {
        if (!m_StoryActive || m_Loading || Time.unscaledTime < m_NextInputTime)
            return;

        m_NextInputTime = Time.unscaledTime + 0.25f;
        if (m_Line >= 0 && !m_LineComplete)
        {
            CompleteLine();
            return;
        }

        m_Line++;
        if (m_Line >= Story.Length)
        {
            EnterGame();
            return;
        }

        ShowLine(m_Line);
    }

    void ShowLine(int index)
    {
        m_ShownLine = index;
        m_StoryText.text = Story[index];
        m_StoryText.ForceMeshUpdate();
        m_VisibleCharacters = 0f;
        int count = m_StoryText.textInfo.characterCount;
        m_LineComplete = count == 0;
        m_StoryText.maxVisibleCharacters = m_LineComplete ? 99999 : 0;
        m_StoryCount.text = (index + 1) + "  /  " + Story.Length;
        m_StoryHint.text = ContinueHint(index == Story.Length - 1);
    }

    void TickTypewriter()
    {
        if (m_LineComplete || m_StoryText == null || m_ShownLine != m_Line)
            return;

        m_VisibleCharacters += 38f * Time.unscaledDeltaTime;
        int count = m_StoryText.textInfo.characterCount;
        int shown = Mathf.Clamp(Mathf.FloorToInt(m_VisibleCharacters), 0, count);
        m_StoryText.maxVisibleCharacters = shown;
        if (shown >= count)
            m_LineComplete = true;
    }

    void CompleteLine()
    {
        m_LineComplete = true;
        if (m_StoryText != null)
            m_StoryText.maxVisibleCharacters = 99999;
    }

    void EnterGame()
    {
        if (m_Loading)
            return;

        m_Loading = true;
        m_StoryActive = false;
        if (m_StoryGroup != null)
        {
            m_StoryGroup.interactable = false;
            m_StoryGroup.blocksRaycasts = false;
        }

        if (m_Fade != null)
            m_Fade.Load(GameSceneName);
        else
            SceneManager.LoadScene(GameSceneName);
    }

    bool ReadAdvance()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.eKey.wasPressedThisFrame))
            return true;

        if (!XRSettings.isDeviceActive)
        {
            m_TriggerWasDown = false;
            return false;
        }

        bool down = ButtonDown(XRNode.RightHand) || ButtonDown(XRNode.LeftHand);
        bool pressed = down && !m_TriggerWasDown;
        m_TriggerWasDown = down;
        return pressed;
    }

    static bool ButtonDown(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;

        bool trigger;
        bool grip;
        bool triggerDown = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out trigger) && trigger;
        bool gripDown = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out grip) && grip;
        return triggerDown || gripDown;
    }

    string ContinueHint(bool last)
    {
        if (XRSettings.isDeviceActive)
            return last ? "Grip or trigger to enter the grove" : "Grip or trigger to continue";

        return last ? "Click or press Space to enter the grove" : "Click or press Space to continue";
    }

    static Camera CreateFallbackCamera()
    {
        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        return camera;
    }

    static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
            return;

        var eventObject = new GameObject("EventSystem");
        eventObject.AddComponent<EventSystem>();
        var module = eventObject.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    void BuildUi(Camera camera)
    {
        var root = new GameObject("Opening Canvas");
        root.layer = 5;
        root.transform.SetParent(camera.transform, false);
        root.transform.localPosition = new Vector3(0f, 0f, 1.55f);
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * 0.00115f;

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        canvas.sortingOrder = 100;
        canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<TrackedDeviceGraphicRaycaster>();

        var rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1680f, 980f);

        Sprite sprite = null;
        var font = LoadFont();

        var backdrop = GroveUi.CreateSurface("Backdrop", rect, GroveUi.Surface, 40f);
        Stretch(backdrop.rectTransform);

        m_MenuGroup = CreateGroup("Menu", rect);
        var menu = m_MenuGroup.GetComponent<RectTransform>();
        Stretch(menu);

        var rule = GroveUi.CreateSurface("Rule", menu, GroveUi.Accent, 2f);
        rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rule.rectTransform.sizeDelta = new Vector2(72f, 4f);
        rule.rectTransform.anchoredPosition = new Vector2(0f, 150f);

        var eyebrow = CreateText("Eyebrow", menu, font, 22f, FontStyles.Normal, TextAlignmentOptions.Center, GroveUi.Accent);
        eyebrow.text = "A QUIET FOREST ADVENTURE";
        eyebrow.characterSpacing = 5f;
        eyebrow.rectTransform.sizeDelta = new Vector2(1000f, 40f);
        eyebrow.rectTransform.anchoredPosition = new Vector2(0f, 340f);

        var title = CreateText("Title", menu, font, 88f, FontStyles.Bold, TextAlignmentOptions.Center, GroveUi.Text);
        title.text = "Grove Elf";
        title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        title.rectTransform.sizeDelta = new Vector2(1400f, 120f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 230f);

        var subtitle = CreateText("Subtitle", menu, font, 30f, FontStyles.Normal, TextAlignmentOptions.Center, GroveUi.Muted);
        subtitle.text = "An elf among the small souls of the wood";
        subtitle.rectTransform.anchorMin = subtitle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        subtitle.rectTransform.sizeDelta = new Vector2(1400f, 70f);
        subtitle.rectTransform.anchoredPosition = new Vector2(0f, 80f);

        var begin = CreateChoice(menu, font, "Begin", new Vector2(0f, -48f), Begin, true);
        CreateChoice(menu, font, "Leave", new Vector2(0f, -160f), Leave);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(begin.gameObject);

        m_StoryGroup = CreateGroup("Story", rect);
        var story = m_StoryGroup.GetComponent<RectTransform>();
        Stretch(story);
        m_StoryGroup.alpha = 0f;
        m_StoryGroup.interactable = false;
        m_StoryGroup.blocksRaycasts = false;

        var veil = GroveUi.CreateSurface("Veil", story, GroveUi.Surface, 40f);
        Stretch(veil.rectTransform);

        m_ContinueButton = CreateButton("Continue", story, sprite, new Color(0f, 0f, 0f, 0f), Advance);
        Stretch(m_ContinueButton.GetComponent<RectTransform>());
        m_ContinueButton.transition = Selectable.Transition.None;

        m_StoryText = CreateText("Line", story, font, 48f, FontStyles.Normal, TextAlignmentOptions.Center, GroveUi.Text);
        m_StoryText.lineSpacing = 12f;
        m_StoryText.text = string.Empty;
        m_StoryText.rectTransform.anchorMin = m_StoryText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        m_StoryText.rectTransform.sizeDelta = new Vector2(1200f, 360f);
        m_StoryText.rectTransform.anchoredPosition = new Vector2(0f, 20f);

        m_StoryCount = CreateText("Count", story, font, 24f, FontStyles.Normal, TextAlignmentOptions.Center, GroveUi.Accent);
        m_StoryCount.characterSpacing = 4f;
        m_StoryCount.rectTransform.anchorMin = m_StoryCount.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        m_StoryCount.rectTransform.sizeDelta = new Vector2(400f, 40f);
        m_StoryCount.rectTransform.anchoredPosition = new Vector2(0f, 150f);

        m_StoryHint = CreateText("Hint", story, font, 26f, FontStyles.Normal, TextAlignmentOptions.Center, GroveUi.Muted);
        m_StoryHint.rectTransform.anchorMin = m_StoryHint.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        m_StoryHint.rectTransform.sizeDelta = new Vector2(1200f, 50f);
        m_StoryHint.rectTransform.anchoredPosition = new Vector2(0f, 90f);

        var skip = CreateChoice(story, font, "Skip", new Vector2(620f, 400f), Skip);
        var skipRect = skip.GetComponent<RectTransform>();
        skipRect.sizeDelta = new Vector2(220f, 64f);
    }

    void BuildFade()
    {
        m_Fade = SceneFade.Create(null);
    }

    Button CreateChoice(Transform parent, TMP_FontAsset font, string label, Vector2 position, UnityEngine.Events.UnityAction action, bool primary = false)
    {
        // Let Selectable own the tint. Tinting the Image too multiplies the colors
        // and made the old buttons almost black, including their hover state.
        var image = GroveUi.CreateSurface(label, parent, Color.white, 18f);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        var rect = button.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(460f, 88f);
        rect.anchoredPosition = position;

        var colors = button.colors;
        colors.normalColor = primary ? GroveUi.Sage : new Color(0.15f, 0.22f, 0.18f);
        colors.highlightedColor = primary ? new Color(0.87f, 0.94f, 0.80f) : new Color(0.25f, 0.34f, 0.28f);
        colors.pressedColor = primary ? new Color(0.61f, 0.73f, 0.55f) : new Color(0.11f, 0.17f, 0.14f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.22f, 0.27f, 0.23f, 0.5f);
        colors.fadeDuration = 0.12f;
        button.colors = colors;

        var text = CreateText("Label", button.transform, font, 32f, primary ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.Center, primary ? new Color(0.08f, 0.14f, 0.11f) : GroveUi.Text);
        text.text = label;
        Stretch(text.rectTransform);
        return button;
    }

    static Button CreateButton(string name, Transform parent, Sprite sprite, Color color, UnityEngine.Events.UnityAction action)
    {
        var image = CreateImage(name, parent, sprite, color);
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        return button;
    }

    static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = true;
        return image;
    }

    static TextMeshProUGUI CreateText(string name, Transform parent, TMP_FontAsset font, float size, FontStyles style, TextAlignmentOptions alignment, Color color)
    {
        var go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        text.extraPadding = true;
        return text;
    }

    static CanvasGroup CreateGroup(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go.AddComponent<CanvasGroup>();
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static TMP_FontAsset LoadFont()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

}

/// <summary>
/// Black cover for the scene change. It lives on its own so the menu scene can unload without leaving it up.
/// </summary>
public class SceneFade : MonoBehaviour
{
    CanvasGroup m_Group;

    public static SceneFade Create(Sprite sprite)
    {
        var root = new GameObject("Scene Fade");
        DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        root.AddComponent<CanvasScaler>();

        var go = new GameObject("Black");
        go.transform.SetParent(root.transform, false);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.black;
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var fade = root.AddComponent<SceneFade>();
        fade.m_Group = group;
        return fade;
    }

    public void Load(string sceneName)
    {
        StartCoroutine(LoadRoutine(sceneName));
    }

    IEnumerator LoadRoutine(string sceneName)
    {
        yield return Fade(1f);
        var load = SceneManager.LoadSceneAsync(sceneName);
        if (load == null)
        {
            Debug.LogError("Opening could not load " + sceneName + ". Add it to the build settings.");
            yield break;
        }

        while (!load.isDone)
            yield return null;

        var canvas = GetComponent<Canvas>();
        if (canvas != null)
            canvas.enabled = false;
        Destroy(gameObject);
    }

    IEnumerator Fade(float target)
    {
        if (m_Group == null)
            yield break;

        float start = m_Group.alpha;
        float time = 0f;
        const float duration = 0.45f;
        while (time < duration)
        {
            time += Time.unscaledDeltaTime;
            m_Group.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(time / duration));
            yield return null;
        }

        m_Group.alpha = target;
        m_Group.blocksRaycasts = false;
    }
}
