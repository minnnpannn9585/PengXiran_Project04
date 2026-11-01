using UnityEngine;

/// <summary>Grounded companion movement and a short rabbit escape, using the supplied animal animations.</summary>
[DisallowMultipleComponent]
public class QuestAnimalMotion : MonoBehaviour
{
    [SerializeField] Animator m_Animator;
    [SerializeField] string m_IdleState;
    [SerializeField] string m_WalkState;
    [SerializeField] string m_RunState;
    [SerializeField] string m_FearState;
    [SerializeField] float m_WalkSpeed = 2.2f;
    [SerializeField] float m_RunSpeed = 4.5f;
    [SerializeField] float m_FollowDistance = 1.8f;
    [SerializeField] LayerMask m_GroundLayers = ~0;

    Vector3 m_RunTarget;
    bool m_Running;
    bool m_Frightened;
    string m_CurrentAnimation;
    readonly RaycastHit[] m_Hits = new RaycastHit[24];
    public Transform FollowTarget { get; set; }

    void Awake()
    {
        if (m_Animator == null)
            m_Animator = GetComponentInChildren<Animator>();
        if (m_Animator != null)
            m_Animator.applyRootMotion = false;
        Play(m_IdleState);
    }

    public void PlayFear()
    {
        m_Frightened = true;
        Play(m_FearState);
    }

    public void RunTo(Vector3 point)
    {
        m_Frightened = false;
        m_Running = true;
        m_RunTarget = point;
    }

    void Update()
    {
        if (m_Frightened)
            return;
        if (!m_Running && FollowTarget == null)
        {
            Play(m_IdleState);
            return;
        }
        Vector3 target = m_Running ? m_RunTarget : FollowTarget.position;
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        float remaining = direction.magnitude - (m_Running ? 0.1f : m_FollowDistance);
        if (remaining <= 0f)
        {
            if (m_Running)
                m_Running = false;
            Play(m_IdleState);
            return;
        }

        direction.Normalize();
        float step = Mathf.Min(remaining, (m_Running ? m_RunSpeed : m_WalkSpeed) * Time.deltaTime);
        // Ignore this animal's own colliders and triggers when checking obstacles.
        int count = Physics.SphereCastNonAlloc(transform.position + Vector3.up * 0.4f, 0.15f,
            direction, m_Hits, step + 0.12f, m_GroundLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var hit = m_Hits[i];
            // Some authored animals start inside a bush collider. Let them exit that overlap.
            if (!hit.transform.IsChildOf(transform) && hit.distance > 0.001f && hit.normal.y < 0.55f)
            {
                Play(m_IdleState);
                return;
            }
        }
        Vector3 next = transform.position + direction * step;
        count = Physics.RaycastNonAlloc(next + Vector3.up * 2f, Vector3.down, m_Hits, 4f,
            m_GroundLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        bool grounded = false;
        for (int i = 0; i < count; i++)
        {
            var hit = m_Hits[i];
            // Do not snap up onto foliage/another animal above the actual walking surface.
            if (hit.transform.IsChildOf(transform) || hit.normal.y < 0.55f || hit.distance >= nearest
                || Mathf.Abs(hit.point.y - transform.position.y) > 0.45f
                || hit.collider.GetComponentInParent<QuestAnimalMotion>() != null)
                continue;
            nearest = hit.distance;
            next.y = hit.point.y + 0.02f;
            grounded = true;
        }
        if (!grounded)
        {
            Play(m_IdleState);
            return;
        }
        transform.position = next;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 360f * Time.deltaTime);
        Play(m_Running ? m_RunState : m_WalkState);
    }

    void Play(string state)
    {
        if (m_Animator == null || string.IsNullOrEmpty(state) || state == m_CurrentAnimation)
            return;
        int hash = Animator.StringToHash(state);
        if (!m_Animator.HasState(0, hash))
            return;
        m_Animator.CrossFadeInFixedTime(hash, 0.15f);
        m_CurrentAnimation = state;
    }
}
