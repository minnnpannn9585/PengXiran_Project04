using System;
using UnityEngine;

/// <summary>
/// Trigger volume on the wolf. A dropped or thrown ball that enters is ready to be returned.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[AddComponentMenu("Ball/Ball Return")]
public class BallReturn : MonoBehaviour
{
    public event Action<LostBall> Arrived;

    void Awake()
    {
        var collider = GetComponent<Collider>();
        if (collider == null || !collider.isTrigger)
            Debug.LogError("Ball Return needs a trigger collider.", this);
    }

    void OnTriggerEnter(Collider other)
    {
        var ball = other.GetComponentInParent<LostBall>();
        if (ball == null || ball.IsHeld || ball.IsReturned)
            return;

        Arrived?.Invoke(ball);
    }

    public bool Contains(LostBall ball)
    {
        var collider = GetComponent<Collider>();
        return ball != null && collider != null && collider.bounds.Contains(ball.transform.position);
    }
}
