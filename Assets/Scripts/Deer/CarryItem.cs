using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Something the player can hold. Grip or trigger in a headset. Click in the editor.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(XRGrabInteractable))]
[AddComponentMenu("Deer/Carry Item")]
public class CarryItem : MonoBehaviour
{
    [SerializeField] float m_CarryScale = 0.22f;
    [SerializeField] float m_HoldDistance = 0.5f;
    [SerializeField] string m_Label = "Item";

    XRGrabInteractable m_Grab;
    Rigidbody m_Body;
    readonly List<XRBaseInputInteractor> m_Hovering = new List<XRBaseInputInteractor>();
    WorldPrompt m_Prompt;
    static CarryItem s_DesktopHeld;

    bool m_DesktopHeld;
    bool m_Placed;
    bool m_Scaled;
    bool m_GrabAccepted;
    bool m_SuppressSelectFeedback;
    bool m_HasBeenPickedUp;
    int m_IgnoreDropFrame = -1;

    public bool IsHeld => !m_Placed && (m_DesktopHeld || (m_Grab != null && m_Grab.isSelected));

    public bool IsPlaced => m_Placed;

    public string Label => m_Label;

    void Awake()
    {
        m_Grab = GetComponent<XRGrabInteractable>();
        m_Body = GetComponent<Rigidbody>();
        m_Body.isKinematic = true;
        m_Body.useGravity = true;
        m_Body.interpolation = RigidbodyInterpolation.Interpolate;
        m_Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        m_Grab.interactionLayers = -1;
        m_Grab.movementType = XRBaseInteractable.MovementType.Kinematic;
        m_Grab.throwOnDetach = false;
        m_Grab.forceGravityOnDetach = true;
        m_Grab.farAttachMode = InteractableFarAttachMode.Far;
        m_Prompt = WorldPrompt.Create(transform);
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
        m_GrabAccepted = false;
        m_DesktopHeld = false;
        if (s_DesktopHeld == this)
            s_DesktopHeld = null;
        if (m_Grab == null)
            return;

        m_Grab.hoverEntered.RemoveListener(OnHoverEntered);
        m_Grab.hoverExited.RemoveListener(OnHoverExited);
        m_Grab.selectEntered.RemoveListener(OnSelectEntered);
        m_Grab.selectExited.RemoveListener(OnSelectExited);
    }

    void Update()
    {
        if (m_Placed)
            return;

        if (!XRSettings.isDeviceActive)
            PollDesktopPickup();

        PollTriggerGrab();
    }

    void LateUpdate()
    {
        if (m_Placed)
            return;

        if (m_DesktopHeld)
        {
            HoldInFront();
            PollDesktopDrop();
        }

        PlacePrompt();
    }

    public bool PickUp(XRBaseInputInteractor hand, bool playFeedback = true)
    {
        if (m_Placed)
            return false;
        if (IsHeld)
            return true;
        if (!AllowedToPick())
            return false;

        m_IgnoreDropFrame = Time.frameCount;

        Vector3 pickupPosition = transform.position;
        HerbPlant plant = GetComponentInParent<HerbPlant>();
        bool harvestedNow = plant != null && !plant.IsHarvested;
        if (plant != null)
            plant.MarkHarvested();

        transform.SetParent(null, true);
        ApplyCarryScale();

        if (hand != null && XRSettings.isDeviceActive && m_Grab != null && m_Grab.interactionManager != null)
        {
            m_DesktopHeld = false;
            gameObject.layer = 0;
            if (m_Body != null)
            {
                m_Body.isKinematic = false;
                m_Body.velocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }

            transform.position = hand.transform.position;
            m_Grab.enabled = true;
            // SelectEnter raises the same callback as a direct controller grab.
            m_SuppressSelectFeedback = true;
            try
            {
                m_Grab.interactionManager.SelectEnter((IXRSelectInteractor)hand, (IXRSelectInteractable)m_Grab);
            }
            finally
            {
                m_SuppressSelectFeedback = false;
            }
            if (m_Grab.isSelected)
                PlayPickupFeedback(playFeedback, harvestedNow, pickupPosition);
            return true;
        }

        if (s_DesktopHeld != null && s_DesktopHeld != this)
            s_DesktopHeld.Release();

        m_DesktopHeld = true;
        s_DesktopHeld = this;
        gameObject.layer = 2;
        if (m_Body != null)
        {
            if (!m_Body.isKinematic)
            {
                m_Body.velocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }
            m_Body.isKinematic = true;
        }

        HoldInFront();
        PlayPickupFeedback(playFeedback, harvestedNow, pickupPosition);
        return true;
    }

    public void PlaceAt(Transform parent, Vector3 localPosition, float scale)
    {
        m_Placed = true;
        m_DesktopHeld = false;
        m_GrabAccepted = false;
        if (s_DesktopHeld == this)
            s_DesktopHeld = null;

        ReleaseGrab();
        if (m_Grab != null)
            m_Grab.enabled = false;
        if (m_Prompt != null)
            m_Prompt.Hide();

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
        transform.SetParent(parent, false);
        transform.localPosition = localPosition;
        transform.localRotation = Quaternion.identity;
        Vector3 parentScale = parent.lossyScale;
        transform.localScale = new Vector3(
            scale / Mathf.Max(0.0001f, parentScale.x),
            scale / Mathf.Max(0.0001f, parentScale.y),
            scale / Mathf.Max(0.0001f, parentScale.z));

        var herb = GetComponent<Herb>();
        if (herb != null)
            herb.SetOnTable(true);
    }

    /// <summary>End desktop/XR holding before a quest prop disappears, without moving it.</summary>
    public void FinishInteraction()
    {
        m_Placed = true;
        m_DesktopHeld = false;
        m_GrabAccepted = false;
        if (s_DesktopHeld == this)
            s_DesktopHeld = null;

        ReleaseGrab();
        if (m_Grab != null)
            m_Grab.enabled = false;
        m_Hovering.Clear();
        if (m_Prompt != null)
            m_Prompt.Hide();

        if (m_Body != null)
        {
            if (!m_Body.isKinematic)
            {
                m_Body.velocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
            }
            m_Body.isKinematic = true;
        }

        foreach (var itemCollider in GetComponentsInChildren<Collider>(true))
            itemCollider.enabled = false;
    }

    public static T FindHeld<T>() where T : Component
    {
        var found = FindObjectsOfType<T>();
        for (int i = 0; i < found.Length; i++)
        {
            var carry = found[i].GetComponent<CarryItem>();
            if (carry != null && carry.IsHeld)
                return found[i];
        }

        return null;
    }

    void OnHoverEntered(HoverEnterEventArgs args)
    {
        if (args.interactorObject is XRBaseInputInteractor input && !m_Hovering.Contains(input))
        {
            var plant = GetComponentInParent<HerbPlant>();
            if (m_Hovering.Count == 0 && !IsHeld && AllowedToPick()
                && (plant == null || plant.IsHarvested))
                InteractionFeedback.Play(FeedbackCue.Focus, transform);
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
        if (!AllowedToPick())
        {
            m_GrabAccepted = false;
            ReleaseGrab();
            return;
        }

        bool alreadySelected = m_GrabAccepted;
        m_GrabAccepted = true;
        HerbPlant plant = GetComponentInParent<HerbPlant>();
        bool harvestedNow = plant != null && !plant.IsHarvested;
        if (plant != null)
            plant.MarkHarvested();
        if (!alreadySelected && !m_SuppressSelectFeedback)
            PlayPickupFeedback(true, harvestedNow, transform.position);

        m_DesktopHeld = false;
        if (s_DesktopHeld == this)
            s_DesktopHeld = null;
        gameObject.layer = 0;
        ApplyCarryScale();
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        // Two hands, forced placement and completion cleanup must not sound like a drop.
        if (m_Grab != null && m_Grab.isSelected)
            return;
        bool wasGrabbed = m_GrabAccepted;
        m_GrabAccepted = false;
        if (m_Placed || m_Body == null)
            return;

        m_Body.isKinematic = false;
        m_Body.useGravity = true;
        if (wasGrabbed && isActiveAndEnabled && !args.isCanceled)
            InteractionFeedback.Play(FeedbackCue.Drop, transform.position);
    }

    void PlayPickupFeedback(bool playFeedback, bool harvestedNow, Vector3 position)
    {
        // Red flowers are directly grabbable; purple sprigs also have a harvestable bush.
        bool firstHerbPickup = !m_HasBeenPickedUp && GetComponent<Herb>() != null;
        m_HasBeenPickedUp = true;
        if (playFeedback && !harvestedNow)
            InteractionFeedback.Play(firstHerbPickup ? FeedbackCue.Harvest : FeedbackCue.Pickup, position);
    }

    void PollTriggerGrab()
    {
        if (m_Placed || m_DesktopHeld || m_Grab == null || m_Grab.isSelected)
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

            if (!AllowedToPick())
                return;

            if (input is IXRSelectInteractor selectInteractor)
                manager.SelectEnter(selectInteractor, (IXRSelectInteractable)m_Grab);
            return;
        }
    }

    void PollDesktopPickup()
    {
        if (m_DesktopHeld || m_Placed)
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

        PickUp(null);
    }

    void PollDesktopDrop()
    {
        var mouse = Mouse.current;
        var camera = Camera.main;
        if (mouse == null || camera == null || !mouse.leftButton.wasPressedThisFrame)
            return;
        if (Time.frameCount == m_IgnoreDropFrame)
            return;

        var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out var hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.GetComponentInParent<DialogueInteractable>() != null)
                return;
            if (hit.transform.GetComponentInParent<MedicineTable>() != null)
                return;
            if (hit.transform.GetComponentInParent<HerbPlant>() != null)
                return;
        }

        Release();
    }

    void Release()
    {
        bool wasHeld = IsHeld;
        m_GrabAccepted = false;
        m_DesktopHeld = false;
        if (s_DesktopHeld == this)
            s_DesktopHeld = null;
        gameObject.layer = 0;
        ReleaseGrab();
        if (m_Body != null)
        {
            m_Body.isKinematic = false;
            m_Body.useGravity = true;
        }
        if (wasHeld && !m_Placed && isActiveAndEnabled)
            InteractionFeedback.Play(FeedbackCue.Drop, transform.position);
    }

    void ReleaseGrab()
    {
        if (m_Grab == null || !m_Grab.isSelected || m_Grab.interactionManager == null)
            return;

        var selecting = new List<IXRSelectInteractor>(m_Grab.interactorsSelecting);
        for (int i = 0; i < selecting.Count; i++)
            m_Grab.interactionManager.SelectExit(selecting[i], (IXRSelectInteractable)m_Grab);
    }

    void HoldInFront()
    {
        var camera = Camera.main;
        if (camera == null)
            return;

        transform.position = camera.transform.position + camera.transform.forward * m_HoldDistance - camera.transform.up * 0.15f;
        transform.rotation = Quaternion.LookRotation(camera.transform.forward, Vector3.up);
    }

    void ApplyCarryScale()
    {
        if (m_Scaled || m_CarryScale <= 0f)
            return;

        m_Scaled = true;
        transform.SetParent(null, true);
        transform.localScale = Vector3.one * m_CarryScale;
    }

    bool AllowedToPick()
    {
        if (m_Placed)
            return false;

        var herb = GetComponent<Herb>();
        if (herb == null)
            return true;

        return GameManager.Instance != null && GameManager.Instance.CanGatherHerbs;
    }

    void PlacePrompt()
    {
        if (m_Prompt == null)
            return;

        bool show = !string.IsNullOrEmpty(m_Label)
            && !m_Placed
            && !IsHeld
            && AllowedToPick()
            && (m_Hovering.Count > 0 || DistanceToCamera() <= 4.5f);
        if (!show)
        {
            m_Prompt.Hide();
            return;
        }

        string action = XRSettings.isDeviceActive ? "Grip or trigger to pick up" : "Click to pick up";
        m_Prompt.SetText(m_Label + "\n" + action);
        m_Prompt.Place(transform.position + Vector3.up * 0.25f, 0.0016f);
    }

    float DistanceToCamera()
    {
        var camera = Camera.main;
        if (camera == null)
            return float.MaxValue;

        return Vector3.Distance(camera.transform.position, transform.position);
    }
}
