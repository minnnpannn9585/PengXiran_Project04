using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One bounded particle system per trail; only nearby footprints emit wisps.</summary>
[DisallowMultipleComponent]
public class FootprintTrail : MonoBehaviour
{
    [SerializeField] Material m_ParticleMaterial;
    [SerializeField, Min(0f)] float m_ParticlesPerPrintPerSecond = 1.5f;
    [SerializeField, Min(1f)] float m_VisibleDistance = 18f;

    Renderer[] m_Prints;
    ParticleSystem m_Particles;
    Camera m_Camera;
    float m_Emission;

    void Awake()
    {
        m_Prints = GetComponentsInChildren<MeshRenderer>(true);
        var go = new GameObject("Blue Rising Wisps");
        go.transform.SetParent(transform, false);
        m_Particles = go.AddComponent<ParticleSystem>();
        m_Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = m_Particles.main;
        main.playOnAwake = false;
        main.loop = true;
        main.maxParticles = 256;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.065f);
        main.startSpeed = 0f;
        main.startColor = new Color(0.22f, 0.7f, 1f, 0.8f);
        main.gravityModifier = 0f;
        var emission = m_Particles.emission;
        emission.enabled = false;
        var shape = m_Particles.shape;
        shape.enabled = false;
        var color = m_Particles.colorOverLifetime;
        color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.25f, 0.65f, 1f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0f, 1f) });
        color.color = gradient;
        var size = m_Particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));
        var renderer = m_Particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = m_ParticleMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
    }

    void OnEnable()
    {
        m_Emission = 0f;
        if (m_Particles != null)
            m_Particles.Play();
    }

    void OnDisable()
    {
        if (m_Particles != null)
            m_Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void Update()
    {
        if (m_Camera == null)
            m_Camera = Camera.main;
        if (m_Camera == null || m_ParticleMaterial == null)
            return;
        m_Emission += Time.deltaTime * m_ParticlesPerPrintPerSecond;
        if (m_Emission < 1f)
            return;
        m_Emission %= 1f;
        float maxDistanceSquared = m_VisibleDistance * m_VisibleDistance;
        foreach (var footprint in m_Prints)
        {
            Vector3 point = footprint.bounds.center;
            if ((point - m_Camera.transform.position).sqrMagnitude > maxDistanceSquared)
                continue;
            var particle = new ParticleSystem.EmitParams
            {
                position = point + new Vector3(Random.Range(-0.12f, 0.12f), 0.04f, Random.Range(-0.12f, 0.12f)),
                velocity = new Vector3(Random.Range(-0.06f, 0.06f), Random.Range(0.18f, 0.4f), Random.Range(-0.06f, 0.06f))
            };
            m_Particles.Emit(particle, 1);
        }
    }
}
