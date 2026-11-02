using System.Collections.Generic;
using UnityEngine;
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
    bool m_Healed;
    bool m_CleanedUp;
    MedicineBottle m_UsedBottle;

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
            prompt = "Give me the medicine.";
        }

        m_Dialogue.SetPrompt(prompt);
    }

    public bool TryGiveMedicine()
    {
        if (m_Healed || GameManager.Instance != null && GameManager.Instance.DeerQuest == DeerPhase.Healed)
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
        m_Healed = true;
        m_UsedBottle = CarryItem.FindHeld<MedicineBottle>();
        if (m_UsedBottle != null)
        {
            var carry = m_UsedBottle.GetComponent<CarryItem>();
            if (carry != null)
                carry.FinishInteraction();
            // Keep the used medicine visible for its completion burst.
            m_UsedBottle.transform.SetParent(transform, true);
            m_UsedBottle.transform.position = transform.position + transform.right * 0.55f + Vector3.up * 0.4f;
        }

        if (m_Wound != null)
            m_Wound.SetActive(false);
        if (m_Bandage != null)
            m_Bandage.SetActive(true);
        InteractionFeedback.Play(FeedbackCue.Heal,
            m_Bandage != null ? m_Bandage.transform.position : transform.position + Vector3.up * 0.5f);

        if (GameManager.Instance != null)
            GameManager.Instance.OnDeerHealed();

        if (m_Necklace != null)
        {
            // The gift belongs to the player and must survive the fawn disappearing.
            m_Necklace.transform.SetParent(null, true);
            m_Necklace.gameObject.SetActive(true);
            if (m_Necklace.PickUp(m_LastHand, false))
                InteractionFeedback.Play(FeedbackCue.Reward, m_Necklace.transform.position);
        }

        QuestCompletionEffects.Begin(this, m_Dialogue, m_Thanks, FinishQuest);
    }

    void FinishQuest()
    {
        if (m_CleanedUp)
            return;

        m_CleanedUp = true;
        var targets = new List<GameObject>();
        if (m_UsedBottle != null)
            targets.Add(m_UsedBottle.gameObject);
        AddQuestObjects<Herb>(targets);
        AddQuestObjects<HerbPlant>(targets);
        AddQuestObjects<MedicineBottle>(targets);
        AddQuestObjects<MedicineTable>(targets);
        targets.Add(gameObject);
        QuestCompletionEffects.BurstAndHide(targets.ToArray());
    }

    void AddQuestObjects<T>(List<GameObject> targets) where T : Component
    {
        // Carried flowers leave their original hierarchy, so collect by quest component.
        // Limit cleanup to this scene; additive scenes and ordinary vegetation stay intact.
        foreach (var item in FindObjectsOfType<T>(true))
        {
            if (item.gameObject.scene == gameObject.scene && !targets.Contains(item.gameObject))
                targets.Add(item.gameObject);
        }
    }
}
