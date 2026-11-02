using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Shows a speech bubble for a <see cref="DialogueData"/> asset on this object.
/// Point at the collider and press the grip or trigger. In the editor, press E or click.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(XRSimpleInteractable))]
[AddComponentMenu("Dialogue/Dialogue Interactable")]
public class DialogueInteractable : MonoBehaviour
{
    const int ShowAllCharacters = 99999;
    static bool s_HasInteracted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetInteractionHint()
    {
        s_HasInteracted = false;
    }

    enum BubbleView
    {
        Hidden,
        Prompt,
        Speaking
    }

    [SerializeField] DialogueData m_Data;
    [SerializeField] Transform m_Anchor;

    [SerializeField] float m_PromptDistance = 4.5f;
    [SerializeField] float m_StayDistance = 6.5f;
    [SerializeField] float m_MessageHoldSeconds = 16f;
    [SerializeField] float m_AdvanceCooldown = 0.4f;
    [SerializeField] float m_CharactersPerSecond = 42f;
    [SerializeField] float m_BubbleScale = 0.0014f;
    [SerializeField] float m_Clearance = 0.06f;
    [SerializeField] bool m_Loop = true;

    XRSimpleInteractable m_Interactable;
    readonly List<XRBaseInputInteractor> m_Hovering = new List<XRBaseInputInteractor>();

    Transform m_Head;
    Renderer[] m_Renderers;
    GameObject m_Bubble;
    CanvasGroup m_CanvasGroup;
    TextMeshProUGUI m_NameText;
    TextMeshProUGUI m_BodyText;
    RectTransform m_PanelRect;
    RectTransform m_ShadowRect;

    BubbleView m_View = BubbleView.Hidden;
    int m_LineIndex = -1;
    int m_ShownLine = int.MinValue;
    float m_VisibleCharacters;
    float m_Pop;
    float m_NextAdvanceTime;
    float m_VisibleUntil;
    bool m_LineComplete = true;
    bool m_Ready;
    DialogueData m_Queued;
    string m_PromptOverride;
    Action m_CompletionCallback;
    float m_CompletionReadTime;
    bool m_AutomaticCompletion;
    bool m_SilentNextLine;

    public DialogueData Data => m_Data;

    public bool Loop
    {
        get => m_Loop;
        set => m_Loop = value;
    }

    public event Action<DialogueInteractable> Finished;

    public event Func<bool> InterceptInteraction;

    public void SetPrompt(string prompt)
    {
        if (m_PromptOverride == prompt)
            return;

        m_PromptOverride = prompt;
    }

    public void Queue(DialogueData data)
    {
        m_Queued = data;
    }

    /// <summary>Replace both an idle prompt and an already-open conversation.</summary>
    public void ResetConversation(DialogueData data)
    {
        if (data == null || data.LineCount == 0)
            return;
        m_AutomaticCompletion = false;
        m_CompletionCallback = null;
        m_SilentNextLine = false;
        m_Data = data;
        m_Queued = null;
        m_LineIndex = -1;
        m_ShownLine = int.MinValue;
        m_LineComplete = true;
        m_VisibleUntil = 0f;
    }

    public void Play(DialogueData data, bool playFeedback = true)
    {
        if (data == null || data.LineCount == 0)
            return;

        m_AutomaticCompletion = false;
        m_CompletionCallback = null;
        m_Queued = null;
        m_Data = data;
        m_LineIndex = 0;
        m_ShownLine = int.MinValue;
        m_VisibleCharacters = 0f;
        m_LineComplete = false;
        m_SilentNextLine = !playFeedback;
        m_VisibleUntil = Time.time + m_MessageHoldSeconds;
        m_NextAdvanceTime = Time.time + m_AdvanceCooldown;
    }

    /// <summary>Read each celebration line, then finish without requiring another interaction.</summary>
    public void PlayCompletion(DialogueData data, Action onFinished)
    {
        if (data == null || data.LineCount == 0 || !isActiveAndEnabled)
        {
            onFinished?.Invoke();
            return;
        }

        Play(data);
        m_Loop = false;
        m_PromptOverride = null;
        m_AutomaticCompletion = true;
        m_CompletionCallback = onFinished;
        m_CompletionReadTime = 2.2f;
    }

    void Awake()
    {
        m_Interactable = GetComponent<XRSimpleInteractable>();
        if (m_Data == null || m_Data.LineCount == 0)
        {
            Debug.LogError("Dialogue Interactable needs Dialogue Data with at least one line.", this);
            return;
        }

        if (m_Interactable == null)
        {
            Debug.LogError("Dialogue Interactable needs an XR Simple Interactable on the same object.", this);
            return;
        }

        if (!HasSolidCollider())
        {
            Debug.LogError("Dialogue Interactable needs a non-trigger collider so the player can point at it.", this);
            return;
        }

        m_Head = m_Anchor != null ? m_Anchor : FindChildNamed("head");
        m_Renderers = GetComponentsInChildren<Renderer>();
        BuildBubble();
        m_Ready = m_Bubble != null;
    }

    void OnEnable()
    {
        if (m_Bubble != null)
            m_Bubble.SetActive(true);
        if (m_Interactable == null)
            m_Interactable = GetComponent<XRSimpleInteractable>();
        if (m_Interactable == null)
            return;

        m_Interactable.hoverEntered.AddListener(OnHoverEntered);
        m_Interactable.hoverExited.AddListener(OnHoverExited);
        m_Interactable.selectEntered.AddListener(OnSelectEntered);
    }

    void OnDisable()
    {
        m_Hovering.Clear();
        if (m_Bubble != null)
            m_Bubble.SetActive(false);
        if (m_Interactable == null)
            return;

        m_Interactable.hoverEntered.RemoveListener(OnHoverEntered);
        m_Interactable.hoverExited.RemoveListener(OnHoverExited);
        m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
    }

    void Update()
    {
        if (!m_Ready)
            return;

        PollTrigger();
        PollDesktopInput();
        RefreshView();
        TickTypewriter();
        TickCompletion();
        m_Pop = Mathf.MoveTowards(m_Pop, DesiredView() == BubbleView.Hidden ? 0f : 1f, Time.deltaTime * 5f);
    }

    void LateUpdate()
    {
        if (!m_Ready)
            return;

        PlaceBubble();
        float shown = Mathf.SmoothStep(0f, 1f, m_Pop);
        m_CanvasGroup.alpha = shown;
        float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
        float scale = m_BubbleScale * Mathf.Lerp(0.9f, 1f, shown) / parentScale;
        m_Bubble.transform.localScale = Vector3.one * scale;
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (args.interactorObject is XRBaseInputInteractor input && !m_Hovering.Contains(input))
        {
            if (m_Hovering.Count == 0)
                InteractionFeedback.Play(FeedbackCue.Focus, AnchorPosition());
            m_Hovering.Add(input);
        }
    }

    void OnHoverExited(HoverExitEventArgs args)
    {
        if (args.interactorObject is XRBaseInputInteractor input)
            m_Hovering.Remove(input);
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        TryAdvance();
    }

    void PollTrigger()
    {
        for (int i = m_Hovering.Count - 1; i >= 0; i--)
        {
            var input = m_Hovering[i];
            if (input == null)
            {
                m_Hovering.RemoveAt(i);
                continue;
            }

            if (input.activateInput != null && input.activateInput.ReadWasPerformedThisFrame())
            {
                TryAdvance();
                return;
            }
        }
    }

    void PollDesktopInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.eKey.wasPressedThisFrame && DesiredView() != BubbleView.Hidden)
            TryAdvance();

        var mouse = Mouse.current;
        var camera = ViewCamera();
        if (mouse == null || camera == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out var hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.transform != null
            && hit.transform.IsChildOf(transform))
        {
            TryAdvance();
        }
    }

    void TryAdvance()
    {
        if (!isActiveAndEnabled || !m_Ready)
            return;
        if (Time.time < m_NextAdvanceTime)
            return;

        if (!m_AutomaticCompletion)
            s_HasInteracted = true;

        if (InterceptInteraction != null)
        {
            Delegate[] handlers = InterceptInteraction.GetInvocationList();
            for (int i = 0; i < handlers.Length; i++)
            {
                if (((Func<bool>)handlers[i])())
                    return;
            }
        }

        if (m_Data == null || m_Data.LineCount == 0)
            return;

        if (m_LineIndex < 0 && m_Queued != null)
        {
            m_Data = m_Queued;
            m_Queued = null;
        }

        m_NextAdvanceTime = Time.time + m_AdvanceCooldown;
        m_VisibleUntil = Time.time + m_MessageHoldSeconds;

        if (m_LineIndex < 0)
        {
            m_LineIndex = 0;
            return;
        }

        if (!m_LineComplete)
        {
            CompleteLine();
            InteractionFeedback.Play(FeedbackCue.Dialogue, AnchorPosition());
            return;
        }

        if (m_LineIndex >= m_Data.LineCount - 1)
        {
            DialogueData data = m_Data;
            int index = m_LineIndex;
            Action completion = m_CompletionCallback;
            m_CompletionCallback = null;
            m_AutomaticCompletion = false;
            Finished?.Invoke(this);
            completion?.Invoke();
            if (m_Data != data || m_LineIndex != index)
                return;

            if (m_Queued != null)
            {
                m_Data = m_Queued;
                m_Queued = null;
                m_LineIndex = -1;
                m_ShownLine = int.MinValue;
                m_LineComplete = true;
                return;
            }

            if (!m_Loop)
                return;

            m_LineIndex = 0;
            return;
        }

        m_LineIndex++;
    }

    void RefreshView()
    {
        var view = DesiredView();
        if (view == BubbleView.Hidden)
        {
            m_View = BubbleView.Hidden;
            return;
        }

        if (view == BubbleView.Prompt)
        {
            m_View = BubbleView.Prompt;
            m_LineComplete = true;
            SetText(m_NameText, m_Data.SpeakerName);
            SetBody(PromptText());
            m_BodyText.maxVisibleCharacters = ShowAllCharacters;
            return;
        }

        m_View = BubbleView.Speaking;
        if (m_ShownLine != m_LineIndex)
        {
            m_ShownLine = m_LineIndex;
            // A single soft response per line, with a small visual settle rather than character chatter.
            m_Pop = Mathf.Min(m_Pop, 0.7f);
            if (!m_SilentNextLine)
                InteractionFeedback.Play(FeedbackCue.Dialogue, AnchorPosition());
            m_SilentNextLine = false;
            m_CompletionReadTime = Mathf.Clamp(m_Data.GetLine(m_LineIndex).Length / 25f, 2.2f, 4f);
            SetText(m_NameText, m_Data.SpeakerName);
            SetBody(m_Data.GetLine(m_LineIndex));
            m_BodyText.ForceMeshUpdate();
            m_VisibleCharacters = 0f;
            int count = m_BodyText.textInfo.characterCount;
            m_LineComplete = count == 0;
            m_BodyText.maxVisibleCharacters = m_LineComplete ? ShowAllCharacters : 0;
        }
    }

    void TickTypewriter()
    {
        if (m_View != BubbleView.Speaking || m_LineComplete)
            return;

        m_VisibleCharacters += m_CharactersPerSecond * Time.deltaTime;
        int count = m_BodyText.textInfo.characterCount;
        int shown = Mathf.Clamp(Mathf.FloorToInt(m_VisibleCharacters), 0, count);
        m_BodyText.maxVisibleCharacters = shown;
        if (shown >= count)
            m_LineComplete = true;
    }

    void CompleteLine()
    {
        m_LineComplete = true;
        m_BodyText.maxVisibleCharacters = ShowAllCharacters;
    }

    void TickCompletion()
    {
        if (!m_AutomaticCompletion || !m_LineComplete || m_View != BubbleView.Speaking)
            return;

        m_CompletionReadTime -= Time.deltaTime;
        if (m_CompletionReadTime <= 0f)
            TryAdvance();
    }

    BubbleView DesiredView()
    {
        if (m_AutomaticCompletion && m_LineIndex >= 0)
            return BubbleView.Speaking;
        float distance = DistanceToViewer();
        bool hovering = m_Hovering.Count > 0;
        if (m_LineIndex >= 0 && (hovering || distance <= m_StayDistance || Time.time < m_VisibleUntil))
            return BubbleView.Speaking;
        if (m_LineIndex < 0 && (hovering || distance <= m_PromptDistance))
            return BubbleView.Prompt;
        return BubbleView.Hidden;
    }

    string PromptText()
    {
        if (!s_HasInteracted)
            return UsingControllers() ? "Grip or trigger to interact" : "Press E or click to interact";

        // Keep nearby characters discoverable by name, without repeating the input tutorial.
        return m_PromptOverride ?? string.Empty;
    }

    static bool UsingControllers()
    {
        return XRSettings.isDeviceActive;
    }

    float DistanceToViewer()
    {
        var camera = ViewCamera();
        if (camera == null)
            return float.MaxValue;
        return Vector3.Distance(camera.transform.position, transform.position);
    }

    static Camera ViewCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        return FindObjectOfType<Camera>();
    }

    void PlaceBubble()
    {
        var camera = ViewCamera();
        Vector3 anchor = AnchorPosition();
        anchor.y += Mathf.Sin(Time.time * 1.7f) * 0.012f * m_Pop;
        m_Bubble.transform.position = anchor;

        if (camera == null)
            return;

        Vector3 forward = anchor - camera.transform.position;
        if (forward.sqrMagnitude < 0.0001f)
            return;

        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(forward.normalized, up)) > 0.98f)
            up = camera.transform.up;

        m_Bubble.transform.rotation = Quaternion.LookRotation(forward, up);
    }

    Vector3 AnchorPosition()
    {
        Bounds bounds = default;
        bool hasBounds = TryGetVisualBounds(out bounds);
        Vector3 anchor = hasBounds ? bounds.center : transform.position;
        float top = hasBounds ? bounds.max.y : anchor.y + 0.8f;

        if (m_Head != null)
        {
            anchor.x = m_Head.position.x;
            anchor.z = m_Head.position.z;
            top = Mathf.Max(top, m_Head.position.y);
        }

        anchor.y = top + m_Clearance;
        return anchor;
    }

    bool TryGetVisualBounds(out Bounds bounds)
    {
        bounds = default;
        bool has = false;
        if (m_Renderers == null)
            return false;

        for (int i = 0; i < m_Renderers.Length; i++)
        {
            var renderer = m_Renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer || !renderer.enabled)
                continue;

            if (!has)
            {
                bounds = renderer.bounds;
                has = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return has;
    }

    bool HasSolidCollider()
    {
        var colliders = GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && colliders[i].enabled && !colliders[i].isTrigger)
                return true;
        }

        return false;
    }

    Transform FindChildNamed(string childName)
    {
        var transforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == childName)
                return transforms[i];
        }

        return null;
    }

    void BuildBubble()
    {
        var font = LoadFont();
        if (font == null)
        {
            Debug.LogError("Dialogue Interactable needs a TextMesh Pro font.", this);
            return;
        }

        m_Bubble = new GameObject("SpeechBubble");
        m_Bubble.transform.SetParent(transform, false);
        m_Bubble.layer = 2;

        var canvas = m_Bubble.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 80;
        canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;

        m_CanvasGroup = m_Bubble.AddComponent<CanvasGroup>();
        m_CanvasGroup.alpha = 0f;
        m_CanvasGroup.blocksRaycasts = false;
        m_CanvasGroup.interactable = false;

        var root = m_Bubble.GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(960f, 540f);
        root.pivot = new Vector2(0.5f, 0f);

        var shadow = GroveUi.CreateSurface("Shadow", root, new Color(0.01f, 0.025f, 0.02f, 0.24f));
        m_ShadowRect = shadow.rectTransform;
        SetBottom(m_ShadowRect, new Vector2(880f, 208f), new Vector2(0f, 18f));

        var panel = GroveUi.CreateSurface("Panel", root, GroveUi.Surface);
        m_PanelRect = panel.rectTransform;
        SetBottom(m_PanelRect, new Vector2(880f, 208f), new Vector2(0f, 26f));

        m_NameText = CreateText("Name", m_PanelRect, font, 28f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, GroveUi.Sage);
        var nameRect = m_NameText.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.offsetMin = new Vector2(36f, -64f);
        nameRect.offsetMax = new Vector2(-36f, -20f);

        m_BodyText = CreateText("Body", m_PanelRect, font, 40f, FontStyles.Normal, TextAlignmentOptions.TopLeft, GroveUi.Text);
        m_BodyText.lineSpacing = 6f;
        var bodyRect = m_BodyText.rectTransform;
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(36f, 24f);
        bodyRect.offsetMax = new Vector2(-36f, -82f);
    }

    void SetBody(string value)
    {
        bool hasBody = !string.IsNullOrEmpty(value);
        if (m_BodyText.text == value && m_BodyText.gameObject.activeSelf == hasBody)
            return;

        m_BodyText.text = value;
        m_BodyText.gameObject.SetActive(hasBody);
        // Measure the complete line before the typewriter starts, so the card never jumps.
        float bodyHeight = m_BodyText.GetPreferredValues(value, 808f, Mathf.Infinity).y;
        float height = hasBody ? Mathf.Max(208f, Mathf.Ceil(bodyHeight) + 106f) : 84f;
        m_PanelRect.sizeDelta = new Vector2(880f, height);
        m_ShadowRect.sizeDelta = m_PanelRect.sizeDelta;
    }

    TextMeshProUGUI CreateText(string objectName, Transform parent, TMP_FontAsset font, float size, FontStyles style, TextAlignmentOptions alignment, Color color)
    {
        var go = new GameObject(objectName);
        go.layer = 2;
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = color;
        text.enableWordWrapping = true;
        text.richText = false;
        text.raycastTarget = false;
        text.extraPadding = true;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    static void SetBottom(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    static void SetText(TMP_Text text, string value)
    {
        if (text.text != value)
            text.text = value;
    }

    static TMP_FontAsset LoadFont()
    {
        if (TMP_Settings.defaultFontAsset != null)
            return TMP_Settings.defaultFontAsset;

        return Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
    }

}
