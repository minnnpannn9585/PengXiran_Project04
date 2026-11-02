using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Shared quest celebrations. Bursts live independently of the objects they replace.</summary>
public static class QuestCompletionEffects
{
    public static void Begin(MonoBehaviour host, DialogueInteractable dialogue, DialogueData data, Action onFinished)
    {
        if (host == null || !host.isActiveAndEnabled)
            return;
        if (dialogue != null)
            dialogue.PlayCompletion(data, onFinished);
        else
            onFinished?.Invoke();
    }

    public static void BurstAndHide(params GameObject[] targets)
    {
        var unique = new HashSet<GameObject>();
        foreach (var target in targets)
            if (target != null && target.activeInHierarchy)
                unique.Add(target);

        bool playedSound = false;
        // Capture all positions before hiding any parents (the bottle can be under the deer).
        foreach (var target in unique)
        {
            Bounds bounds = new Bounds(target.transform.position + Vector3.up * 0.15f, Vector3.one * 0.3f);
            bool hasVisual = false;
            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer is ParticleSystemRenderer
                    || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                    continue;
                if (hasVisual)
                    bounds.Encapsulate(renderer.bounds);
                else
                    bounds = renderer.bounds;
                hasVisual = true;
            }

            // Trigger-only helpers do not need a second burst at the same character.
            if (hasVisual)
            {
                CreateBurst(bounds.center, Mathf.Clamp(bounds.extents.magnitude, 0.22f, 0.8f), target.name);
                if (!playedSound)
                {
                    InteractionFeedback.PlaySound(FeedbackCue.Success, bounds.center);
                    playedSound = true;
                }
            }
        }

        foreach (var target in unique)
        {
            foreach (var item in target.GetComponentsInChildren<CarryItem>(true))
                item.FinishInteraction();
            foreach (var ball in target.GetComponentsInChildren<LostBall>(true))
                ball.FinishInteraction();
            target.SetActive(false);
        }
    }

    static void CreateBurst(Vector3 position, float radius, string targetName)
    {
        var go = new GameObject("Quest Completion Burst - " + targetName);
        go.transform.position = position;
        var particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = 0.15f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(GroveUi.Accent, GroveUi.Sage);
        main.gravityModifier = -0.05f;
        main.maxParticles = 40;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        var emission = particles.emission;
        emission.enabled = false;
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius * 0.5f;

        var color = particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(GroveUi.Accent, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.25f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = Resources.Load<Material>("Effects/QuestCompletionBurst");
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Play();
        particles.Emit(32);
        // Also bound the lifetime if the system is culled or simulation is paused.
        UnityEngine.Object.Destroy(go, 2f);
    }
}
