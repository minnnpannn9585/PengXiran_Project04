using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// A bush the player harvests by hand. The sprig is what they carry to the stone table.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(XRSimpleInteractable))]
[AddComponentMenu("Deer/Herb Plant")]
public class HerbPlant : MonoBehaviour
{
    [SerializeField] CarryItem m_Sprig;
    [SerializeField] string m_Label = "Purple flower";

    XRSimpleInteractable m_Interactable;
    readonly List<XRBaseInputInteractor> m_Hovering = new List<XRBaseInputInteractor>();
    WorldPrompt m_Prompt;
    bool m_Harvested;
    bool m_CloseInteraction;

    public bool IsHarvested => m_Harvested;

    void Awake()
    {
        m_Interactable = GetComponent<XRSimpleInteractable>();
        if (m_Interactable != null)
            m_Interactable.interactionLayers = -1;

        var box = GetComponent<BoxCollider>();
        if (box != null)
        {
            Vector3 size = box.size;
            box.size = new Vector3(Mathf.Min(size.x, 1.1f), Mathf.Min(size.y, 1.2f), Mathf.Min(size.z, 1.1f));
        }

        m_Prompt = WorldPrompt.Create(transform);
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

    void Update()
    {
        if (m_Harvested)
            return;

        if (!XRSettings.isDeviceActive)
            PollDesktop();

        PollTrigger();
    }

    void LateUpdate()
    {
        if (m_CloseInteraction)
        {
            m_CloseInteraction = false;
            var collider = GetComponent<Collider>();
            if (collider != null)
                collider.enabled = false;
            if (m_Interactable != null)
                m_Interactable.enabled = false;
        }

        PlacePrompt();
    }

    public void Harvest(XRBaseInputInteractor hand)
    {
        if (m_Harvested || m_Sprig == null)
            return;
        if (GameManager.Instance == null || !GameManager.Instance.CanGatherHerbs)
            return;

        m_Sprig.PickUp(hand);
    }

    public void MarkHarvested()
    {
        if (m_Harvested)
            return;

        m_Harvested = true;
        m_CloseInteraction = true;
        if (m_Prompt != null)
            m_Prompt.Hide();
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
        Harvest(args.interactorObject as XRBaseInputInteractor);
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
                Harvest(input);
                return;
            }
        }
    }

    void PollDesktop()
    {
        var mouse = Mouse.current;
        var camera = Camera.main;
        if (mouse == null || camera == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        var ray = camera.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out var hit, 40f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        if (hit.transform != transform && !hit.transform.IsChildOf(transform))
            return;

        Harvest(null);
    }

    void PlacePrompt()
    {
        if (m_Prompt == null || m_Harvested)
            return;

        bool allowed = GameManager.Instance != null && GameManager.Instance.CanGatherHerbs;
        bool show = allowed && (m_Hovering.Count > 0 || DistanceToCamera() <= 5f);
        if (!show)
        {
            m_Prompt.Hide();
            return;
        }

        string action = XRSettings.isDeviceActive ? "Grip or trigger to pick" : "Click to pick";
        m_Prompt.SetText(m_Label + "\n" + action);
        var renderer = GetComponentInChildren<Renderer>();
        float top = renderer != null ? renderer.bounds.max.y : transform.position.y + 0.6f;
        m_Prompt.Place(new Vector3(transform.position.x, top + 0.15f, transform.position.z), 0.0018f);
    }

    float DistanceToCamera()
    {
        var camera = Camera.main;
        if (camera == null)
            return float.MaxValue;

        return Vector3.Distance(camera.transform.position, transform.position);
    }
}
