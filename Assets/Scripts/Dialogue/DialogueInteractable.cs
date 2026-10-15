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
    TextMeshProUGUI m_HintText;
    readonly List<UnityEngine.Object> m_GeneratedAssets = new List<UnityEngine.Object>();

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

    public void Play(DialogueData data)
    {
        if (data == null || data.LineCount == 0)
            return;

        m_Queued = null;
        m_Data = data;
        m_LineIndex = 0;
        m_ShownLine = int.MinValue;
        m_VisibleCharacters = 0f;
        m_LineComplete = false;
        m_VisibleUntil = Time.time + m_MessageHoldSeconds;
        m_NextAdvanceTime = Time.time + m_AdvanceCooldown;
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
        if (m_Interactable == null)
            return;

        m_Interactable.hoverEntered.RemoveListener(OnHoverEntered);
        m_Interactable.hoverExited.RemoveListener(OnHoverExited);
        m_Interactable.selectEntered.RemoveListener(OnSelectEntered);
    }

    void OnDestroy()
    {
        for (int i = 0; i < m_GeneratedAssets.Count; i++)
        {
            if (m_GeneratedAssets[i] != null)
                Destroy(m_GeneratedAssets[i]);
        }
    }

    void Update()
    {
        if (!m_Ready)
            return;

        PollTrigger();
        PollDesktopInput();
        RefreshView();
        TickTypewriter();
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
            m_Hovering.Add(input);
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
        if (Time.time < m_NextAdvanceTime)
            return;

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
            return;
        }

        if (m_LineIndex >= m_Data.LineCount - 1)
        {
            DialogueData data = m_Data;
            int index = m_LineIndex;
            Finished?.Invoke(this);
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
            SetText(m_BodyText, PromptText());
            m_BodyText.maxVisibleCharacters = ShowAllCharacters;
            SetText(m_HintText, string.Empty);
            return;
        }

        m_View = BubbleView.Speaking;
        if (m_ShownLine != m_LineIndex)
        {
            m_ShownLine = m_LineIndex;
            SetText(m_NameText, m_Data.SpeakerName);
            SetText(m_BodyText, m_Data.GetLine(m_LineIndex));
            m_BodyText.ForceMeshUpdate();
            m_VisibleCharacters = 0f;
            int count = m_BodyText.textInfo.characterCount;
            m_LineComplete = count == 0;
            m_BodyText.maxVisibleCharacters = m_LineComplete ? ShowAllCharacters : 0;
        }

        SetText(m_HintText, m_LineComplete ? ContinueHint() : string.Empty);
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

    BubbleView DesiredView()
    {
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
        if (!string.IsNullOrEmpty(m_PromptOverride))
            return m_PromptOverride;

        return UsingControllers()
            ? "Point at me and press the grip or trigger."
            : "Press E, or click me, to talk.";
    }

    string ContinueHint()
    {
        bool repeat = m_Loop && m_LineIndex >= m_Data.LineCount - 1;
        string progress = $"{m_LineIndex + 1}/{m_Data.LineCount}";
        if (UsingControllers())
        {
            return repeat
                ? $"{progress}  ·  Grip or trigger to hear that again"
                : $"{progress}  ·  Grip or trigger to continue";
        }

        return repeat
            ? $"{progress}  ·  Press E to hear that again"
            : $"{progress}  ·  Press E to continue";
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

        var fill = new Color(0.13f, 0.09f, 0.07f, 0.96f);
        var border = new Color(0.86f, 0.62f, 0.34f, 1f);
        var panelSprite = CreateRoundedSprite(128, 32, fill, border, 3.2f);
        var tailSprite = CreateTailSprite(fill);
        var whiteSprite = CreateWhiteSprite();

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

        var shadow = CreateImage("Shadow", root, panelSprite, new Color(0f, 0f, 0f, 0.38f));
        SetBottom(shadow.rectTransform, new Vector2(890f, 300f), new Vector2(8f, 16f));

        var tail = CreateImage("Tail", root, tailSprite, Color.white);
        tail.type = Image.Type.Simple;
        var tailRect = tail.rectTransform;
        tailRect.anchorMin = tailRect.anchorMax = new Vector2(0.5f, 0f);
        tailRect.pivot = new Vector2(0.5f, 0f);
        tailRect.sizeDelta = new Vector2(70f, 42f);
        tailRect.anchoredPosition = new Vector2(0f, 2f);

        var panel = CreateImage("Panel", root, panelSprite, Color.white);
        SetBottom(panel.rectTransform, new Vector2(880f, 292f), new Vector2(0f, 26f));

        var accent = CreateImage("Accent", panel.rectTransform, whiteSprite, new Color(0.93f, 0.66f, 0.36f, 1f));
        accent.type = Image.Type.Simple;
        var accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.offsetMin = new Vector2(52f, -40f);
        accentRect.offsetMax = new Vector2(-52f, -32f);

        m_NameText = CreateText("Name", panel.rectTransform, font, 38f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, new Color(0.98f, 0.82f, 0.58f));
        var nameRect = m_NameText.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 1f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.offsetMin = new Vector2(52f, -96f);
        nameRect.offsetMax = new Vector2(-52f, -42f);

        m_BodyText = CreateText("Body", panel.rectTransform, font, 42f, FontStyles.Normal, TextAlignmentOptions.TopLeft, new Color(0.97f, 0.94f, 0.9f));
        var bodyRect = m_BodyText.rectTransform;
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(52f, 96f);
        bodyRect.offsetMax = new Vector2(-52f, -102f);

        m_HintText = CreateText("Hint", panel.rectTransform, font, 24f, FontStyles.Italic, TextAlignmentOptions.MidlineLeft, new Color(0.86f, 0.78f, 0.66f, 1f));
        var hintRect = m_HintText.rectTransform;
        hintRect.anchorMin = new Vector2(0f, 0f);
        hintRect.anchorMax = new Vector2(1f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.offsetMin = new Vector2(52f, 48f);
        hintRect.offsetMax = new Vector2(-52f, 88f);
    }

    Image CreateImage(string objectName, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(objectName);
        go.layer = 2;
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
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

    Sprite CreateRoundedSprite(int size, int radius, Color fill, Color borderColor, float borderWidth)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        var pixels = new Color[size * size];
        float half = size * 0.5f;
        var box = new Vector2(half - radius, half - radius);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var point = new Vector2(x + 0.5f - half, y + 0.5f - half);
                var q = new Vector2(Mathf.Abs(point.x) - box.x, Mathf.Abs(point.y) - box.y);
                float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
                float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
                float dist = outside + inside - radius;
                float coverage = Mathf.Clamp01(0.75f - dist);
                if (coverage <= 0f)
                    continue;

                var color = dist > -borderWidth ? borderColor : fill;
                color.a *= coverage;
                pixels[y * size + x] = color;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        var sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            1f,
            0,
            SpriteMeshType.FullRect,
            new Vector4(radius, radius, radius, radius));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        m_GeneratedAssets.Add(texture);
        m_GeneratedAssets.Add(sprite);
        return sprite;
    }

    Sprite CreateWhiteSprite()
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.white;
        texture.SetPixels(pixels);
        texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 1f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        m_GeneratedAssets.Add(texture);
        m_GeneratedAssets.Add(sprite);
        return sprite;
    }

    Sprite CreateTailSprite(Color color)
    {
        const int width = 64;
        const int height = 48;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float allowed = ((y + 0.5f) / height) * 0.5f;
            for (int x = 0; x < width; x++)
            {
                float du = Mathf.Abs((x + 0.5f) / width - 0.5f);
                float coverage = Mathf.Clamp01(0.8f - (du - allowed) * width);
                if (coverage <= 0f)
                    continue;

                var pixel = color;
                pixel.a *= coverage;
                pixels[y * width + x] = pixel;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0f), 1f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        m_GeneratedAssets.Add(texture);
        m_GeneratedAssets.Add(sprite);
        return sprite;
    }
}
