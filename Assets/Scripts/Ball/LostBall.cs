using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// The wolf's ball. Grip or trigger picks it up in a headset. In the editor, click it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(XRGrabInteractable))]
[AddComponentMenu("Ball/Lost Ball")]
public class LostBall : MonoBehaviour
{
    [SerializeField] float m_PromptDistance = 6f;
    [SerializeField] float m_HoldDistance = 0.55f;
    [SerializeField] float m_ThrowSpeed = 11f;
    [SerializeField] float m_ThrowLift = 1.8f;
    [SerializeField] float m_RecallSpeed = 9f;

    XRGrabInteractable m_Grab;
    Rigidbody m_Body;
    XRBaseInputInteractor m_LastInteractor;
    readonly List<XRBaseInputInteractor> m_Hovering = new List<XRBaseInputInteractor>();

    GameObject m_Prompt;
    TextMeshProUGUI m_PromptText;
    Transform m_PendingAnchor;
    bool m_DesktopHeld;
    bool m_Accepted;
    bool m_Snapped;
    bool m_Recalling;
    int m_SnapFrames;

    public bool IsHeld => !m_Accepted && !m_Recalling && (m_DesktopHeld || (m_Grab != null && m_Grab.isSelected));

    public bool IsReturned => m_Accepted;

    public bool IsRecalling => m_Recalling;

    public bool ThrowOnRelease { get; set; }

    public bool IsResting
    {
        get
        {
            if (IsHeld || m_Recalling || m_Body == null)
                return false;
            if (m_Body.isKinematic)
                return true;
            return m_Body.velocity.magnitude < 0.45f && m_Body.angularVelocity.magnitude < 2f;
        }
    }

    public event Action<LostBall> Dropped;

    void Awake()
    {
        m_Grab = GetComponent<XRGrabInteractable>();
        m_Body = GetComponent<Rigidbody>();
        m_Body.mass = 0.2f;
        m_Body.drag = 0.15f;
        m_Body.angularDrag = 0.25f;
        m_Body.useGravity = true;
        m_Body.interpolation = RigidbodyInterpolation.Interpolate;
        m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var collider = GetComponent<SphereCollider>();
        collider.isTrigger = false;
        collider.material = new PhysicMaterial("Lost Ball")
        {
            bounciness = 0.5f,
            dynamicFriction = 0.35f,
            staticFriction = 0.35f,
            bounceCombine = PhysicMaterialCombine.Maximum,
            frictionCombine = PhysicMaterialCombine.Average
        };

        m_Grab.interactionLayers = -1;
        m_Grab.throwOnDetach = true;
        m_Grab.forceGravityOnDetach = true;
        m_Grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
        m_Grab.farAttachMode = InteractableFarAttachMode.Far;
        BuildPrompt();
    }

    void OnEnable()
    {
        if (m_Grab == null)
            m_Grab = GetComponent<XRGrabInteractable>();
        if (m_Grab == null)
            return;

        m_Grab.hoverEntered.AddListener(OnHoverEntered);
        m_Grab.hoverExited.AddListener(OnHoverExited);
        m_Grab.selectEntered.AddListener(OnSelectEntered);
        m_Grab.selectExited.AddListener(OnSelectExited);
    }

    void OnDisable()
    {
        if (m_Grab == null)
            return;

        m_Grab.hoverEntered.RemoveListener(OnHoverEntered);
        m_Grab.hoverExited.RemoveListener(OnHoverExited);
        m_Grab.selectEntered.RemoveListener(OnSelectEntered);
        m_Grab.selectExited.RemoveListener(OnSelectExited);
    }

    void Update()
    {
        if (m_Accepted || m_Recalling)
            return;

        if (!XRSettings.isDeviceActive)
            PollDesktopPickup();

        PollTriggerGrab();
    }

    void LateUpdate()
    {
        if (m_SnapFrames > 0)
        {
            SnapToAnchor();
            m_SnapFrames--;
        }

        if (m_Recalling)
        {
            UpdateRecall();
            return;
        }

        if (!m_Accepted && m_DesktopHeld)
        {
            HoldInFront();
            PollDesktopDrop();
        }

        PlacePrompt();
    }

    public void PrepareForPlay()
    {
        m_Accepted = false;
        m_Snapped = false;
        m_SnapFrames = 0;
        m_Recalling = false;
        if (m_Grab != null)
            m_Grab.enabled = true;

        var collider = GetComponent<Collider>();
        if (collider != null)
            collider.enabled = true;

        if (IsHeld || m_Body == null)
            return;

        if (transform.parent != null)
        {
            Vector3 scale = transform.lossyScale;
            transform.SetParent(null, true);
            transform.localScale = scale;
        }

        m_Body.isKinematic = false;
        m_Body.useGravity = true;
    }

    public void RecallToHolder()
    {
        if (m_Accepted || m_Recalling || IsHeld)
            return;

        m_Recalling = true;
        ReleaseGrab();
        if (transform.parent != null)
        {
            Vector3 scale = transform.lossyScale;
            transform.SetParent(null, true);
            transform.localScale = scale;
        }

        if (m_Body != null)
        {
            m_Body.velocity = Vector3.zero;
            m_Body.angularVelocity = Vector3.zero;
            m_Body.isKinematic = true;
        }

        if (m_Prompt != null)
            m_Prompt.SetActive(false);
    }

    public void AcceptAt(Transform anchor)
    {
        if (m_Accepted)
            return;

        m_Accepted = true;
        m_DesktopHeld = false;
        m_PendingAnchor = anchor;
        m_SnapFrames = 3;
        ReleaseGrab();
        if (m_Grab != null)
            m_Grab.enabled = false;
        if (m_Prompt != null)
            m_Prompt.SetActive(false);
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
        m_DesktopHeld = false;
        gameObject.layer = 0;
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (args.interactorObject is XRBaseInputInteractor interactor)
            m_LastInteractor = interactor;

        if (m_Accepted || m_Recalling)
            return;

        Dropped?.Invoke(this);
    }

    void PollTriggerGrab()
    {
        if (m_Accepted || m_DesktopHeld || m_Grab == null || m_Grab.isSelected)
            return;

        var manager = m_Grab.interactionManager;
        if (manager == null)
            return;

        for (int i = m_Hovering.Count - 1; i >= 0; i--)
        {
            var input = m_Hovering[i];
            if (input == null)
            {
                m_Hovering.RemoveAt(i);
                continue;
            }

            if (input.activateInput == null || !input.activateInput.ReadWasPerformedThisFrame())
                continue;

            if (input is IXRSelectInteractor selectInteractor)
                manager.SelectEnter(selectInteractor, m_Grab);
            return;
        }
    }

    void PollDesktopPickup()
    {
        if (m_DesktopHeld || m_Accepted)
            return;

        var mouse = Mouse.current;
        var camera = Camera.main;
        if (mouse == null || camera == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out var hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        if (hit.transform != transform && !hit.transform.IsChildOf(transform))
            return;

        m_DesktopHeld = true;
        gameObject.layer = 2;
        HoldInFront();
    }

    void PollDesktopDrop()
    {
        var mouse = Mouse.current;
        var camera = Camera.main;
        if (mouse == null || camera == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out var hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.transform.GetComponentInParent<DialogueInteractable>() != null)
            return;

        m_DesktopHeld = false;
        gameObject.layer = 0;
        if (m_Body != null)
        {
            m_Body.isKinematic = false;
            m_Body.angularVelocity = Vector3.zero;
            Vector3 velocity = camera.transform.forward * (ThrowOnRelease ? m_ThrowSpeed : 1.1f);
            if (ThrowOnRelease)
                velocity += Vector3.up * m_ThrowLift;
            m_Body.velocity = velocity;
        }

        Dropped?.Invoke(this);
    }

    void HoldInFront()
    {
        var camera = Camera.main;
        if (camera == null || m_Body == null)
            return;

        if (!m_Body.isKinematic)
        {
            m_Body.velocity = Vector3.zero;
            m_Body.angularVelocity = Vector3.zero;
            m_Body.isKinematic = true;
        }

        transform.position = camera.transform.position + camera.transform.forward * m_HoldDistance - camera.transform.up * 0.18f;
        transform.rotation = Quaternion.LookRotation(camera.transform.forward, Vector3.up);
    }

    void ReleaseGrab()
    {
        if (m_Grab == null || !m_Grab.isSelected || m_Grab.interactionManager == null)
            return;

        var selecting = new List<IXRSelectInteractor>(m_Grab.interactorsSelecting);
        for (int i = 0; i < selecting.Count; i++)
            m_Grab.interactionManager.SelectExit(selecting[i], m_Grab);
    }

    void SnapToAnchor()
    {
        if (m_Body != null)
        {
            if (!m_Body.isKinematic)
            {
                m_Body.velocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }

            m_Body.isKinematic = true;
        }

        var collider = GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;

        gameObject.layer = 0;
        if (m_PendingAnchor == null || m_Snapped)
            return;

        Vector3 scale = transform.localScale;
        transform.SetParent(m_PendingAnchor, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = scale;
        m_Snapped = true;
    }

    void UpdateRecall()
    {
        Vector3 target = HolderPosition();
        transform.position = Vector3.MoveTowards(transform.position, target, m_RecallSpeed * Time.deltaTime);
        if (Vector3.Distance(transform.position, target) > 0.08f)
            return;

        FinishRecall();
    }

    void FinishRecall()
    {
        m_Recalling = false;
        transform.position = HolderPosition();
        XRBaseInputInteractor interactor = ActiveInteractor();
        if (interactor != null && m_Grab != null && m_Grab.interactionManager != null && XRSettings.isDeviceActive)
        {
            if (m_Body != null)
            {
                m_Body.velocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
                m_Body.isKinematic = false;
            }

            m_Grab.enabled = true;
            gameObject.layer = 0;
            m_Grab.interactionManager.SelectEnter((IXRSelectInteractor)interactor, (IXRSelectInteractable)m_Grab);
            return;
        }

        m_DesktopHeld = true;
        gameObject.layer = 2;
        HoldInFront();
    }

    XRBaseInputInteractor ActiveInteractor()
    {
        if (m_LastInteractor != null)
            return m_LastInteractor;

        if (!XRSettings.isDeviceActive)
            return null;

        var found = FindObjectsOfType<XRBaseInputInteractor>();
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null && found[i].isActiveAndEnabled)
                return found[i];
        }

        return null;
    }

    Vector3 HolderPosition()
    {
        if (XRSettings.isDeviceActive && m_LastInteractor != null)
            return m_LastInteractor.transform.position;

        var camera = Camera.main;
        if (camera == null)
            return transform.position;

        return camera.transform.position + camera.transform.forward * m_HoldDistance - camera.transform.up * 0.18f;
    }

    float DistanceToCamera()
    {
        var camera = Camera.main;
        if (camera == null)
            return float.MaxValue;

        return Vector3.Distance(camera.transform.position, transform.position);
    }

    void PlacePrompt()
    {
        if (m_Prompt == null)
            return;

        bool show = !m_Accepted && !m_Recalling && !IsHeld && (m_Hovering.Count > 0 || DistanceToCamera() <= m_PromptDistance);
        if (m_Prompt.activeSelf != show)
            m_Prompt.SetActive(show);
        if (!show)
            return;

        string label = XRSettings.isDeviceActive
            ? "Ball\nGrip or trigger to pick up"
            : "Ball\nClick to pick up";
        if (m_PromptText.text != label)
            m_PromptText.text = label;

        var camera = Camera.main;
        Vector3 anchor = transform.position + Vector3.up * 0.22f;
        m_Prompt.transform.position = anchor;
        float parentScale = Mathf.Max(0.0001f, transform.lossyScale.x);
        m_Prompt.transform.localScale = Vector3.one * (0.0016f / parentScale);
        if (camera == null)
            return;

        Vector3 forward = anchor - camera.transform.position;
        if (forward.sqrMagnitude < 0.0001f)
            return;

        m_Prompt.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    void BuildPrompt()
    {
        var font = TMP_Settings.defaultFontAsset;
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null)
            return;

        m_Prompt = new GameObject("PickupPrompt");
        m_Prompt.layer = 2;
        m_Prompt.transform.SetParent(transform, false);

        var canvas = m_Prompt.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 60;
        canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;

        var rect = m_Prompt.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(460f, 140f);
        rect.pivot = new Vector2(0.5f, 0f);

        var group = m_Prompt.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var textObject = new GameObject("Label");
        textObject.layer = 2;
        textObject.transform.SetParent(m_Prompt.transform, false);
        m_PromptText = textObject.AddComponent<TextMeshProUGUI>();
        m_PromptText.font = font;
        m_PromptText.fontSize = 36f;
        m_PromptText.fontStyle = FontStyles.Bold;
        m_PromptText.alignment = TextAlignmentOptions.Center;
        m_PromptText.color = new Color(0.98f, 0.94f, 0.88f);
        m_PromptText.raycastTarget = false;
        m_PromptText.enableWordWrapping = true;
        var textRect = m_PromptText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        m_Prompt.SetActive(false);
    }
}
