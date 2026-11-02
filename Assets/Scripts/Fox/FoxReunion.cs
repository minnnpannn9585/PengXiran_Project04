using System;
using UnityEngine;

public enum FoxPhase
{
    Waiting, TalkToFox, FollowPart1, WrongColor, FollowPart2,
    Scream, Fleeing, Speechless, FollowPart3, Reunion, Thanks, Complete
}

/// <summary>Stage three. Only the current trail/encounter can advance the story.</summary>
[DisallowMultipleComponent]
public class FoxReunion : MonoBehaviour
{
    [SerializeField] GameManager m_Manager;
    [SerializeField] DialogueInteractable m_Fox;
    [SerializeField] DialogueInteractable[] m_Rabbits = new DialogueInteractable[3];
    [SerializeField] FootprintTrail[] m_Trails = new FootprintTrail[3];
    [SerializeField] QuestAnimalMotion m_FoxMotion;
    [SerializeField] QuestAnimalMotion m_ScaredRabbitMotion;
    [SerializeField] Transform m_FleeDestination;
    [SerializeField] AudioClip m_ScreamSound;
    [SerializeField] DialogueData m_Introduction;
    [SerializeField] DialogueData m_Searching;
    [SerializeField] DialogueData m_Speechless;
    [SerializeField] DialogueData m_Thanks;
    [SerializeField] DialogueData m_Complete;
    [SerializeField, Min(0.5f)] float m_EncounterDistance = 2.6f;
    [SerializeField, Min(0.1f)] float m_ScreamSeconds = 1.5f;
    [SerializeField, Min(0.1f)] float m_FleeSeconds = 2.8f;

    FoxPhase m_Phase;
    float m_Timer;
    AudioSource m_Audio;
    Camera m_Camera;
    WorldPrompt m_Objective;

    public FoxPhase Phase => m_Phase;
    public bool IsComplete => m_Phase == FoxPhase.Complete;
    public event Action Completed;

    void Awake()
    {
        if (m_Manager == null || m_Fox == null || m_FoxMotion == null
            || m_ScaredRabbitMotion == null || m_FleeDestination == null
            || m_Rabbits.Length != 3 || m_Trails.Length != 3
            || Array.Exists(m_Rabbits, rabbit => rabbit == null)
            || Array.Exists(m_Trails, trail => trail == null)
            || m_Introduction == null || m_Searching == null || m_Speechless == null
            || m_Thanks == null || m_Complete == null)
        {
            Debug.LogError("Fox Reunion needs the fox, three rabbits, three trails, motion and dialogue references.", this);
            enabled = false;
            return;
        }

        SetTrail(-1);
        foreach (var rabbit in m_Rabbits)
            rabbit.gameObject.SetActive(false);
        m_Fox.Loop = true;
        m_Objective = WorldPrompt.Create(transform);
        var audioObject = new GameObject("Rabbit Scream Audio");
        audioObject.transform.SetParent(transform, false);
        m_Audio = audioObject.AddComponent<AudioSource>();
        m_Audio.playOnAwake = false;
        m_Audio.spatialBlend = 1f;
        m_Audio.minDistance = 2f;
        m_Audio.maxDistance = 18f;
    }

    void OnEnable()
    {
        if (m_Fox != null)
            m_Fox.Finished += OnFoxFinished;
        foreach (var rabbit in m_Rabbits)
            if (rabbit != null)
                rabbit.Finished += OnRabbitFinished;
    }

    void OnDisable()
    {
        if (m_Fox != null)
            m_Fox.Finished -= OnFoxFinished;
        foreach (var rabbit in m_Rabbits)
            if (rabbit != null)
                rabbit.Finished -= OnRabbitFinished;
        if (m_Objective != null)
            m_Objective.Hide();
    }

    void Update()
    {
        if (IsComplete)
            return;

        if (m_Phase == FoxPhase.Waiting && m_Manager.CanStartFoxQuest)
        {
            m_Phase = FoxPhase.TalkToFox;
            // Replace a previously opened waiting conversation, too.
            m_Fox.ResetConversation(m_Introduction);
            m_Fox.Loop = false;
            InteractionFeedback.Play(FeedbackCue.Focus, m_Fox.transform);
        }

        if (m_Camera == null)
            m_Camera = Camera.main;

        int index = TrailIndex();
        if (m_Camera != null && index >= 0)
        {
            Vector3 offset = m_Camera.transform.position - m_Rabbits[index].transform.position;
            if (new Vector2(offset.x, offset.z).magnitude <= m_EncounterDistance && Mathf.Abs(offset.y) < 3f)
                BeginEncounter(index);
        }

        if (m_Phase == FoxPhase.Scream || m_Phase == FoxPhase.Fleeing)
        {
            m_Timer -= Time.deltaTime;
            if (m_Timer <= 0f)
            {
                if (m_Phase == FoxPhase.Scream)
                    BeginFlee();
                else
                {
                    m_Rabbits[1].gameObject.SetActive(false);
                    m_Phase = FoxPhase.Speechless;
                    m_Fox.Play(m_Speechless);
                }
            }
        }

        bool following = m_Phase >= FoxPhase.FollowPart1 && m_Phase < FoxPhase.Complete;
        m_FoxMotion.FollowTarget = following && m_Camera != null ? m_Camera.transform : null;
        UpdateObjective();
    }

    void OnFoxFinished(DialogueInteractable dialogue)
    {
        if (m_Phase == FoxPhase.TalkToFox && dialogue.Data == m_Introduction)
            BeginTrail(0);
        else if (m_Phase == FoxPhase.Speechless && dialogue.Data == m_Speechless)
            BeginTrail(2);
        else if (m_Phase == FoxPhase.Thanks && dialogue.Data == m_Thanks)
        {
            m_Phase = FoxPhase.Complete;
            SetTrail(-1);
            m_FoxMotion.FollowTarget = null;
            m_Fox.ResetConversation(m_Complete);
            m_Fox.SetPrompt(null);
            m_Fox.enabled = false;
            foreach (var rabbit in m_Rabbits)
                if (rabbit != null)
                    rabbit.enabled = false;
            if (m_Objective != null)
                m_Objective.Hide();
            GameCompletionPanel.Show();
            Completed?.Invoke();
        }
    }

    void OnRabbitFinished(DialogueInteractable dialogue)
    {
        if (m_Phase == FoxPhase.WrongColor && dialogue == m_Rabbits[0])
        {
            dialogue.enabled = false;
            BeginTrail(1);
        }
        else if (m_Phase == FoxPhase.Reunion && dialogue == m_Rabbits[2])
        {
            dialogue.enabled = false;
            m_Phase = FoxPhase.Thanks;
            m_Fox.PlayCompletion(m_Thanks, null);
        }
    }

    void BeginTrail(int index)
    {
        m_Phase = index == 0 ? FoxPhase.FollowPart1 : index == 1 ? FoxPhase.FollowPart2 : FoxPhase.FollowPart3;
        m_Fox.ResetConversation(m_Searching);
        m_Fox.Loop = true;
        m_Fox.SetPrompt("Follow the glowing blue rabbit tracks with me.");
        var rabbit = m_Rabbits[index];
        rabbit.gameObject.SetActive(true);
        rabbit.enabled = false;
        SetTrail(index);
        InteractionFeedback.Play(FeedbackCue.Focus, m_Fox.transform);
    }

    void BeginEncounter(int index)
    {
        // Change phase before playing dialogue: repeated proximity/Finished callbacks are harmless.
        m_Phase = index == 0 ? FoxPhase.WrongColor : index == 1 ? FoxPhase.Scream : FoxPhase.Reunion;
        SetTrail(-1);
        m_Fox.ResetConversation(m_Searching);
        m_Fox.Loop = false;
        m_Fox.SetPrompt(null);
        var rabbit = m_Rabbits[index];
        rabbit.enabled = true;
        rabbit.Loop = false;
        // Encounter stingers own the sound here; do not layer a dialogue tick over the scream/reunion.
        rabbit.Play(rabbit.Data, index == 0);
        if (index == 1)
        {
            m_Timer = m_ScreamSeconds;
            m_ScaredRabbitMotion.PlayFear();
            if (m_ScreamSound != null)
            {
                InteractionFeedback.PlayVisual(FeedbackCue.Surprise, rabbit.transform);
                m_Audio.transform.position = rabbit.transform.position;
                m_Audio.PlayOneShot(m_ScreamSound, 0.65f);
            }
            else
                InteractionFeedback.Play(FeedbackCue.Surprise, rabbit.transform);
        }
        else if (index == 2)
        {
            InteractionFeedback.Play(FeedbackCue.Reunion, rabbit.transform);
            InteractionFeedback.PlayVisual(FeedbackCue.Reunion, m_Fox.transform);
        }
    }

    void BeginFlee()
    {
        m_Phase = FoxPhase.Fleeing;
        m_Timer = m_FleeSeconds;
        m_Rabbits[1].enabled = false;
        m_ScaredRabbitMotion.RunTo(m_FleeDestination.position);
    }

    int TrailIndex()
    {
        return m_Phase == FoxPhase.FollowPart1 ? 0 : m_Phase == FoxPhase.FollowPart2 ? 1
            : m_Phase == FoxPhase.FollowPart3 ? 2 : -1;
    }

    void SetTrail(int index)
    {
        for (int i = 0; i < m_Trails.Length; i++)
            if (m_Trails[i] != null)
                m_Trails[i].gameObject.SetActive(i == index);
    }

    void UpdateObjective()
    {
        if (m_Objective == null || m_Camera == null)
            return;
        int index = TrailIndex();
        bool nearby = Vector3.Distance(m_Camera.transform.position, m_Fox.transform.position) < 12f;
        if ((m_Phase != FoxPhase.TalkToFox && index < 0) || !nearby)
        {
            m_Objective.Hide();
            return;
        }
        m_Objective.SetText(index < 0 ? "A FOX NEEDS YOUR HELP\nTalk to the fox" : $"FIND THE RABBIT  {index + 1}/3\nFollow the blue tracks");
        m_Objective.Place(m_Fox.transform.position + Vector3.up * 2.3f, 0.0015f);
    }
}
