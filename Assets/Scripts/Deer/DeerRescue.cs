using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// The injured fawn. It asks for medicine, then gives a bone necklace once treated.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DialogueInteractable))]
[AddComponentMenu("Deer/Deer Rescue")]
public class DeerRescue : MonoBehaviour
{
    [SerializeField] DialogueInteractable m_Dialogue;
    [SerializeField] DialogueData m_Thanks;
    [SerializeField] GameObject m_Wound;
    [SerializeField] GameObject m_Bandage;
    [SerializeField] CarryItem m_Necklace;

    XRBaseInputInteractor m_LastHand;

    void Awake()
    {
        var interactable = GetComponent<XRSimpleInteractable>();
        if (interactable != null)
            interactable.selectEntered.AddListener(CacheHand);
    }

    void OnDestroy()
    {
        var interactable = GetComponent<XRSimpleInteractable>();
        if (interactable != null)
            interactable.selectEntered.RemoveListener(CacheHand);
    }

    void OnEnable()
    {
        if (!Application.isPlaying)
            return;

        if (m_Dialogue == null)
            m_Dialogue = GetComponent<DialogueInteractable>();
        if (m_Dialogue != null)
            m_Dialogue.InterceptInteraction += OnInteract;
    }

    void OnDisable()
    {
        if (m_Dialogue != null)
            m_Dialogue.InterceptInteraction -= OnInteract;
    }

    void Update()
    {
        if (!Application.isPlaying || m_Dialogue == null || GameManager.Instance == null)
            return;

        string prompt = null;
        if (GameManager.Instance.DeerQuest != DeerPhase.Healed && CarryItem.FindHeld<MedicineBottle>() != null)
        {
            prompt = XRSettings.isDeviceActive
                ? "Point at me and press the grip or trigger to give the medicine."
                : "Press E, or click me, to give the medicine.";
        }

        m_Dialogue.SetPrompt(prompt);
    }

    public bool TryGiveMedicine()
    {
        if (GameManager.Instance != null && GameManager.Instance.DeerQuest == DeerPhase.Healed)
            return false;
        if (CarryItem.FindHeld<MedicineBottle>() == null)
            return false;

        Heal();
        return true;
    }

    void CacheHand(SelectEnterEventArgs args)
    {
        m_LastHand = args.interactorObject as XRBaseInputInteractor;
    }

    bool OnInteract()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.NoteDeerProgress();

        if (!TryGiveMedicine())
            return false;

        return true;
    }

    void Heal()
    {
        var bottle = CarryItem.FindHeld<MedicineBottle>();
        if (bottle != null)
        {
            var carry = bottle.GetComponent<CarryItem>();
            if (carry != null)
                carry.PlaceAt(transform, Vector3.zero, 0.01f);
            bottle.gameObject.SetActive(false);
        }

        if (m_Wound != null)
            m_Wound.SetActive(false);
        if (m_Bandage != null)
            m_Bandage.SetActive(true);

        if (GameManager.Instance != null)
            GameManager.Instance.OnDeerHealed();

        if (m_Dialogue != null)
        {
            m_Dialogue.SetPrompt(null);
            m_Dialogue.Loop = true;
            if (m_Thanks != null)
                m_Dialogue.Play(m_Thanks);
        }

        if (m_Necklace != null)
        {
            m_Necklace.gameObject.SetActive(true);
            m_Necklace.PickUp(m_LastHand);
        }
    }
}
