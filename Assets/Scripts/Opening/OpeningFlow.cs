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
/// Controls the scene-authored menu and opening subtitles without rebuilding their UI.
/// </summary>
public class OpeningFlow : MonoBehaviour
{
    const string GameSceneName = "DemoScene_01";

    [Header("Opening dialogue")]
    [SerializeField, TextArea(2, 4)] string[] m_Story =
    {
        "Welcome home, little elf.",
        "Your forest friends could use a helping hand.",
        "Let's go find them."
    };

    [Header("Baked scene UI")]
    [SerializeField] CanvasGroup m_MenuGroup;
    [SerializeField] CanvasGroup m_StoryGroup;
    [SerializeField] TextMeshProUGUI m_StoryText;
    [SerializeField] TextMeshProUGUI m_StoryHint;
    [SerializeField] TextMeshProUGUI m_StoryCount;
    [SerializeField] Button m_ContinueButton;
    [SerializeField] Button m_BeginButton;
    [SerializeField] RectTransform m_CanvasRect;

    [Header("VR presentation")]
    [SerializeField] Vector3 m_VrCanvasPosition = new Vector3(0f, 0f, 1.55f);
    [SerializeField, Min(0.0001f)] float m_VrCanvasScale = 0.00115f;

    SceneFade m_Fade;
    Vector3 m_StoryTextScale;

    int m_Line = -1;
    int m_ShownLine = int.MinValue;
    float m_VisibleCharacters;
    bool m_LineComplete = true;
    bool m_StoryActive;
    bool m_Loading;
    bool m_TriggerWasDown;
    float m_NextInputTime;
    float m_StoryPulse;

    void Awake()
    {
        if (m_CanvasRect == null || m_MenuGroup == null || m_StoryGroup == null ||
            m_StoryText == null || m_StoryHint == null || m_StoryCount == null ||
            m_BeginButton == null || m_ContinueButton == null)
        {
            Debug.LogError("Opening UI references are missing. Bake the UI using Tools/Grove Elf/Bake Opening UI, or reconnect the scene objects in the Inspector.", this);
            enabled = false;
            return;
        }

        var camera = Camera.main;
        if (camera == null)
            camera = CreateFallbackCamera();

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.035f, 0.06f, 0.045f);
        EnsureEventSystem();
        ConfigureCanvas(camera);
        m_MenuGroup.alpha = 1f;
        m_MenuGroup.interactable = true;
        m_MenuGroup.blocksRaycasts = true;
        m_StoryGroup.alpha = 0f;
        m_StoryGroup.interactable = false;
        m_StoryGroup.blocksRaycasts = false;
        m_StoryTextScale = m_StoryText.rectTransform.localScale;
        m_Fade = SceneFade.Create(null);
    }

    void Start()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(m_BeginButton.gameObject);
    }

    void ConfigureCanvas(Camera camera)
    {
        var canvas = m_CanvasRect.GetComponent<Canvas>();
        canvas.worldCamera = camera;
        bool inVr = XRSettings.isDeviceActive;
        var trackedRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
        if (trackedRaycaster != null)
            trackedRaycaster.enabled = inVr;

        // The same authored objects become the tracked panel in VR. Desktop keeps
        // the Canvas settings, typography, colors and child transforms from the scene.
        if (!inVr)
            return;

        canvas.renderMode = RenderMode.WorldSpace;
        m_CanvasRect.SetParent(camera.transform, false);
        m_CanvasRect.localPosition = m_VrCanvasPosition;
        m_CanvasRect.localRotation = Quaternion.identity;
        m_CanvasRect.localScale = Vector3.one * m_VrCanvasScale;
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler != null)
        {
            m_CanvasRect.sizeDelta = scaler.referenceResolution;
            scaler.dynamicPixelsPerUnit = 10f;
        }
    }

    void Update()
    {
        if (!m_StoryActive || m_Loading)
            return;

        m_StoryPulse = Mathf.MoveTowards(m_StoryPulse, 0f, Time.unscaledDeltaTime * 4f);
        m_StoryText.rectTransform.localScale = m_StoryTextScale * (1f + 0.025f * Mathf.Sin(m_StoryPulse * Mathf.PI));
        TickTypewriter();
        bool pressed = ReadAdvance();
        if (pressed && Time.unscaledTime >= m_NextInputTime)
            Advance();
    }

    public void Begin()
    {
        if (m_StoryActive || m_Loading)
            return;

        InteractionFeedback.PlayUI(FeedbackCue.UIClick);
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

        m_Loading = true;
        InteractionFeedback.PlayUI(FeedbackCue.UIClick);
        StartCoroutine(LeaveAfterFeedback());
    }

    IEnumerator LeaveAfterFeedback()
    {
        yield return new WaitForSecondsRealtime(0.16f);
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

        InteractionFeedback.PlayUI(FeedbackCue.UIClick);
        EnterGame();
    }

    public void Advance()
    {
        if (!m_StoryActive || m_Loading || Time.unscaledTime < m_NextInputTime)
            return;

        m_NextInputTime = Time.unscaledTime + 0.25f;
        m_StoryPulse = 1f;
        if (m_Line >= 0)
            InteractionFeedback.PlayUI(FeedbackCue.Dialogue);
        if (m_Line >= 0 && !m_LineComplete)
        {
            CompleteLine();
            return;
        }

        m_Line++;
        if (m_Story == null || m_Line >= m_Story.Length)
        {
            EnterGame();
            return;
        }

        ShowLine(m_Line);
    }

    void ShowLine(int index)
    {
        m_ShownLine = index;
        m_StoryText.text = m_Story[index];
        m_StoryText.ForceMeshUpdate();
        m_VisibleCharacters = 0f;
        int count = m_StoryText.textInfo.characterCount;
        m_LineComplete = count == 0;
        m_StoryText.maxVisibleCharacters = m_LineComplete ? 99999 : 0;
        m_StoryCount.text = (index + 1) + "  /  " + m_Story.Length;
        m_StoryHint.text = ContinueHint(index == m_Story.Length - 1);
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
