using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>Gentle arrival feedback for the existing XR teleport controls.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TeleportationProvider))]
public class TeleportFeedback : MonoBehaviour
{
    TeleportationProvider m_Provider;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        SceneManager.sceneLoaded -= BindScene;
        SceneManager.sceneLoaded += BindScene;
    }

    static void BindScene(Scene scene, LoadSceneMode mode)
    {
        foreach (var provider in FindObjectsOfType<TeleportationProvider>(true))
            if (provider.gameObject.scene == scene && provider.GetComponent<TeleportFeedback>() == null)
                provider.gameObject.AddComponent<TeleportFeedback>();
    }

    void OnEnable()
    {
        m_Provider = GetComponent<TeleportationProvider>();
        m_Provider.locomotionEnded += OnArrived;
    }

    void OnDisable()
    {
        if (m_Provider != null)
            m_Provider.locomotionEnded -= OnArrived;
        StopAllCoroutines();
    }

    void OnArrived(LocomotionProvider provider) => StartCoroutine(Arrival(m_Provider.positionTransformation.targetPosition));

    IEnumerator Arrival(Vector3 destination)
    {
        // XRI applies queued body transformations later in the frame.
        yield return null;
        InteractionFeedback.Play(FeedbackCue.Teleport, destination + Vector3.up * 0.08f);
    }
}
