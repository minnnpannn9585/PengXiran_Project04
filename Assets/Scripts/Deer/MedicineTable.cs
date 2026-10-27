using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Stone table. Place one red flower and one purple flower, then grind them into a medicine bottle.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(XRSimpleInteractable))]
[AddComponentMenu("Deer/Medicine Table")]
public class MedicineTable : MonoBehaviour
{
    [SerializeField] Transform m_RedSlot;
    [SerializeField] Transform m_PurpleSlot;
    [SerializeField] Transform m_BottlePoint;
    [SerializeField] MedicineBottle m_Bottle;
    [SerializeField] float m_HerbScale = 0.2f;

    XRSimpleInteractable m_Interactable;
    readonly List<XRBaseInputInteractor> m_Hovering = new List<XRBaseInputInteractor>();
    WorldPrompt m_Prompt;
    bool m_HasRed;
    bool m_HasPurple;
    bool m_Brewed;

    public bool HasBothHerbs => m_HasRed && m_HasPurple;

    public bool IsBrewed => m_Brewed;

    void Awake()
    {
        m_Interactable = GetComponent<XRSimpleInteractable>();
        if (m_Interactable != null)
            m_Interactable.interactionLayers = -1;
        m_Prompt = WorldPrompt.Create(transform);
        if (m_Bottle != null)
            m_Bottle.gameObject.SetActive(false);
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
        if (!XRSettings.isDeviceActive)
            PollDesktop();
    }

    void LateUpdate()
    {
        PlacePrompt();
    }

    public bool TryPlaceHeld()
    {
        if (m_Brewed)
            return false;

        var herb = CarryItem.FindHeld<Herb>();
        if (herb == null)
            return false;

        return PlaceHerb(herb);
    }

    public bool TryAcceptLoose(Collider other)
    {
        if (m_Brewed || other == null)
            return false;

        var carry = other.GetComponentInParent<CarryItem>();
        if (carry == null || carry.IsHeld || carry.IsPlaced)
            return false;

        var herb = carry.GetComponent<Herb>();
        if (herb == null)
            return false;

        return PlaceHerb(herb);
    }

    public bool TryGrind()
    {
        if (m_Brewed || !m_HasRed || !m_HasPurple)
            return false;

        m_Brewed = true;
        HideSlot(m_RedSlot);
        HideSlot(m_PurpleSlot);
        if (m_Bottle != null)
        {
            m_Bottle.gameObject.SetActive(true);
            if (m_BottlePoint != null)
            {
                m_Bottle.transform.SetParent(m_BottlePoint, false);
                m_Bottle.transform.localPosition = Vector3.zero;
                m_Bottle.transform.localRotation = Quaternion.identity;
            }
        }

        if (GameManager.Instance != null)
            GameManager.Instance.OnMedicineBrewed();

        return true;
    }

    bool PlaceHerb(Herb herb)
    {
        if (herb == null || herb.IsOnTable)
            return false;

        Transform slot = herb.Kind == HerbKind.RedFlower ? m_RedSlot : m_PurpleSlot;
        bool already = herb.Kind == HerbKind.RedFlower ? m_HasRed : m_HasPurple;
        if (slot == null || already)
            return false;

        var carry = herb.GetComponent<CarryItem>();
        if (carry == null)
            return false;

        carry.PlaceAt(slot, Vector3.zero, m_HerbScale);
        if (herb.Kind == HerbKind.RedFlower)
            m_HasRed = true;
        else
            m_HasPurple = true;

        if (GameManager.Instance != null)
            GameManager.Instance.NoteDeerProgress();

        return true;
    }

    static void HideSlot(Transform slot)
    {
        if (slot == null)
            return;

        for (int i = 0; i < slot.childCount; i++)
            slot.GetChild(i).gameObject.SetActive(false);
    }

    void UseTable()
    {
        if (TryPlaceHeld())
            return;

        TryGrind();
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
        UseTable();
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

        UseTable();
    }

    void PlacePrompt()
    {
        if (m_Prompt == null)
            return;

        var camera = Camera.main;
        if (camera == null || Vector3.Distance(camera.transform.position, transform.position) > 6f && m_Hovering.Count == 0)
        {
            m_Prompt.Hide();
            return;
        }

        string text;
        if (m_Brewed)
            text = XRSettings.isDeviceActive ? "Medicine\nGrip or trigger to pick up" : "Medicine\nClick the bottle";
        else if (m_HasRed && m_HasPurple)
            text = XRSettings.isDeviceActive ? "Grip or trigger to grind" : "Click to grind";
        else if (m_HasRed)
            text = "Place the purple flower";
        else if (m_HasPurple)
            text = "Place the red flower";
        else
            text = "Place a red flower and a purple flower";

        m_Prompt.SetText(text);
        m_Prompt.Place(transform.position + Vector3.up * 0.85f, 0.0018f);
    }
}

/// <summary>
/// Trigger above the stone. A dropped herb is pulled onto its slot.
/// </summary>
[RequireComponent(typeof(Collider))]
public class MedicineDropZone : MonoBehaviour
{
    void OnTriggerStay(Collider other)
    {
        var table = GetComponentInParent<MedicineTable>();
        if (table != null)
            table.TryAcceptLoose(other);
    }
}
