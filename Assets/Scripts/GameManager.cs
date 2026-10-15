using UnityEngine;
using UnityEngine.XR;

public enum GamePhase
{
    TalkToWolf,
    FindBall,
    PlayBall,
    Complete
}

/// <summary>
/// Phase 1 is the wolf's request. Phase 2 is bringing the ball back. Phase 3 is throwing it onto the marker.
/// </summary>
public class GameManager : MonoBehaviour
{
    [SerializeField] GamePhase m_Phase = GamePhase.TalkToWolf;
    [SerializeField] DialogueInteractable m_Wolf;
    [SerializeField] DialogueData m_SearchingDialogue;
    [SerializeField] DialogueData m_PlayDialogue;
    [SerializeField] DialogueData m_ScoredDialogue;
    [SerializeField] LostBall m_Ball;
    [SerializeField] BallReturn m_ReturnVolume;
    [SerializeField] BallTarget m_Target;

    bool m_ThrowInFlight;
    float m_FlightTime;
    float m_RestTimer;
    Vector3 m_ThrowOrigin;
    Vector3 m_ThrowPrevious;

    public GamePhase Phase => m_Phase;

    void OnEnable()
    {
        if (!Application.isPlaying)
            return;

        if (m_Wolf != null)
        {
            m_Wolf.Finished += OnWolfFinished;
            m_Wolf.InterceptInteraction += OnWolfInteraction;
            if (m_Phase == GamePhase.TalkToWolf)
                m_Wolf.Loop = false;
        }

        if (m_Ball != null)
            m_Ball.Dropped += OnBallDropped;

        if (m_ReturnVolume != null)
            m_ReturnVolume.Arrived += OnBallArrived;

        if (m_Phase == GamePhase.PlayBall)
        {
            if (m_Target != null)
                m_Target.gameObject.SetActive(true);
            if (m_Ball != null)
                m_Ball.ThrowOnRelease = true;
        }

        if (m_Wolf == null || m_Ball == null || m_ReturnVolume == null || m_Target == null
            || m_SearchingDialogue == null || m_PlayDialogue == null || m_ScoredDialogue == null)
            Debug.LogError("Game Manager is missing the wolf, ball, target, or dialogue data.", this);
    }

    void OnDisable()
    {
        if (m_Wolf != null)
        {
            m_Wolf.Finished -= OnWolfFinished;
            m_Wolf.InterceptInteraction -= OnWolfInteraction;
        }

        if (m_Ball != null)
            m_Ball.Dropped -= OnBallDropped;

        if (m_ReturnVolume != null)
            m_ReturnVolume.Arrived -= OnBallArrived;
    }

    void Update()
    {
        if (!Application.isPlaying)
            return;

        RefreshPrompt();
        UpdateThrow();
    }

    void RefreshPrompt()
    {
        if (m_Wolf == null)
            return;

        string prompt = null;
        if (m_Phase == GamePhase.FindBall && m_Ball != null && m_Ball.IsHeld)
        {
            prompt = XRSettings.isDeviceActive
                ? "Point at me and press the grip or trigger to give me the ball."
                : "Press E, or click me, to give me the ball.";
        }
        else if (m_Phase == GamePhase.PlayBall && m_Ball != null && m_Ball.IsHeld)
        {
            prompt = XRSettings.isDeviceActive
                ? "Throw the ball onto the marker."
                : "Look at the marker and click to throw.";
        }

        m_Wolf.SetPrompt(prompt);
    }

    void UpdateThrow()
    {
        if (!m_ThrowInFlight || m_Phase != GamePhase.PlayBall || m_Ball == null || m_Target == null)
            return;

        if (m_Ball.IsHeld || m_Ball.IsRecalling)
        {
            m_ThrowInFlight = false;
            m_RestTimer = 0f;
            return;
        }

        m_FlightTime += Time.deltaTime;
        Vector3 position = m_Ball.transform.position;
        bool reached = m_FlightTime > 0.2f
            && Vector3.Distance(m_ThrowOrigin, position) > 1.25f
            && m_Target.ContainsSegment(m_ThrowPrevious, position);
        m_ThrowPrevious = position;
        if (reached)
        {
            ScoreThrow();
            return;
        }

        if (m_FlightTime < 0.45f)
            return;

        if (m_Ball.IsResting)
            m_RestTimer += Time.deltaTime;
        else
            m_RestTimer = 0f;

        if (m_RestTimer < 0.3f && m_FlightTime < 6f)
            return;

        m_ThrowInFlight = false;
        m_RestTimer = 0f;
        m_Ball.RecallToHolder();
    }

    void OnWolfFinished(DialogueInteractable dialogue)
    {
        if (m_Phase != GamePhase.TalkToWolf)
            return;

        m_Phase = GamePhase.FindBall;
        dialogue.Loop = true;
        if (m_SearchingDialogue != null)
            dialogue.Queue(m_SearchingDialogue);

        TryAcceptDroppedBall();
    }

    bool OnWolfInteraction()
    {
        if (m_Phase != GamePhase.FindBall || m_Ball == null || !m_Ball.IsHeld)
            return false;

        BeginPlayBall();
        return true;
    }

    void OnBallDropped(LostBall ball)
    {
        if (ball != m_Ball)
            return;

        if (m_Phase == GamePhase.FindBall)
        {
            TryAcceptDroppedBall();
            return;
        }

        if (m_Phase == GamePhase.PlayBall && !ball.IsHeld && !ball.IsRecalling)
            BeginThrow();
    }

    void OnBallArrived(LostBall ball)
    {
        if (ball == m_Ball)
            TryAcceptDroppedBall();
    }

    void TryAcceptDroppedBall()
    {
        if (m_Phase != GamePhase.FindBall || m_Ball == null || m_Ball.IsHeld || m_Ball.IsReturned)
            return;

        if (m_ReturnVolume == null || !m_ReturnVolume.Contains(m_Ball))
            return;

        BeginPlayBall();
    }

    void BeginPlayBall()
    {
        if (m_Phase != GamePhase.FindBall || m_Ball == null)
            return;

        m_Phase = GamePhase.PlayBall;
        m_ThrowInFlight = false;
        if (m_Target != null)
            m_Target.gameObject.SetActive(true);

        m_Ball.ThrowOnRelease = true;
        m_Ball.PrepareForPlay();
        if (!m_Ball.IsHeld)
            m_Ball.RecallToHolder();

        if (m_Wolf == null)
            return;

        m_Wolf.SetPrompt(null);
        m_Wolf.Loop = true;
        if (m_PlayDialogue != null)
            m_Wolf.Play(m_PlayDialogue);
    }

    void BeginThrow()
    {
        m_ThrowInFlight = true;
        m_FlightTime = 0f;
        m_RestTimer = 0f;
        m_ThrowOrigin = m_Ball.transform.position;
        m_ThrowPrevious = m_ThrowOrigin;
    }

    void ScoreThrow()
    {
        if (m_Phase != GamePhase.PlayBall)
            return;

        m_Phase = GamePhase.Complete;
        m_ThrowInFlight = false;
        if (m_Ball != null)
            m_Ball.ThrowOnRelease = false;

        if (m_Wolf == null)
            return;

        m_Wolf.SetPrompt(null);
        m_Wolf.Loop = true;
        if (m_ScoredDialogue != null)
            m_Wolf.Play(m_ScoredDialogue);
    }
}
