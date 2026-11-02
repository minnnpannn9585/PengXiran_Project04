using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public enum FeedbackCue
{
    Focus, Dialogue, Pickup, Drop, Throw, Recall, Impact, Place, Harvest, Brew,
    Heal, Reward, Success, Surprise, Reunion, UIHover, UIClick, GameComplete, Blocked, Teleport
}

/// <summary>Bounded, reusable voices for short sounds and world-space interaction sparkles.</summary>
public sealed class InteractionFeedback : MonoBehaviour
{
    const int VoiceCount = 16;
    static InteractionFeedback s_Instance;
    static float s_Volume = 0.75f;

    sealed class Voice
    {
        public AudioSource Audio;
        public ParticleSystem Particles;
        public float BusyUntil;
        public bool IsUI;
    }

    struct Style
    {
        public Color Color;
        public int Count;
        public float Radius, Speed, Lifetime, Size, Volume, Cooldown;
    }

    readonly Voice[] m_Voices = new Voice[VoiceCount];
    readonly AudioClip[] m_Clips = new AudioClip[Enum.GetValues(typeof(FeedbackCue)).Length];
    readonly float[] m_NextCueTime = new float[Enum.GetValues(typeof(FeedbackCue)).Length];

    public static float Volume
    {
        get => s_Volume;
        set => s_Volume = Mathf.Clamp01(value);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_Instance = null;
        s_Volume = 0.75f;
    }

    public static void Play(FeedbackCue cue, Transform target)
    {
        if (target != null)
            Play(cue, VisualCenter(target));
    }

    public static void Play(FeedbackCue cue, Vector3 position) => Emit(cue, position, false, true, true);
    public static void PlayUI(FeedbackCue cue) => Emit(cue, Vector3.zero, true, true, false);
    public static void PlaySound(FeedbackCue cue, Vector3 position) => Emit(cue, position, false, true, false);
    public static void PlayVisual(FeedbackCue cue, Vector3 position) => Emit(cue, position, false, false, true);
    public static void PlayVisual(FeedbackCue cue, Transform target)
    {
        if (target != null)
            PlayVisual(cue, VisualCenter(target));
    }

    static void Emit(FeedbackCue cue, Vector3 position, bool ui, bool sound, bool visual)
    {
        if (!Application.isPlaying)
            return;
        if (s_Instance == null)
            s_Instance = FindObjectOfType<InteractionFeedback>();
        if (s_Instance == null)
        {
            var root = new GameObject("Interaction Feedback");
            s_Instance = root.AddComponent<InteractionFeedback>();
        }
        s_Instance.PlayCue(cue, position, ui, sound, visual);
    }

    void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        s_Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        if (s_Instance != null && s_Instance != this)
            return;
        s_Instance = this;
        if (m_Voices[0] == null)
            InitializeVoices();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void InitializeVoices()
    {
        var material = Resources.Load<Material>("Effects/InteractionSpark");
        for (int i = 0; i < m_Clips.Length; i++)
            m_Clips[i] = Resources.Load<AudioClip>("Feedback/Audio/" + (FeedbackCue)i);

        for (int i = 0; i < VoiceCount; i++)
        {
            // Rebind the existing pool after an Editor script reload instead of adding more voices.
            var child = transform.Find("Feedback Voice " + (i + 1));
            var go = child != null ? child.gameObject : new GameObject("Feedback Voice " + (i + 1));
            if (child == null)
                go.transform.SetParent(transform, false);
            var audio = go.GetComponent<AudioSource>();
            if (audio == null) audio = go.AddComponent<AudioSource>();
            audio.Stop();
            audio.playOnAwake = false;
            audio.loop = false;
            audio.dopplerLevel = 0f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 2f;
            audio.maxDistance = 18f;
            var particles = go.GetComponent<ParticleSystem>();
            if (particles == null) particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.1f;
            main.maxParticles = 64;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true;
            var emission = particles.emission;
            emission.enabled = false;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.7f, 0.3f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));
            m_Voices[i] = new Voice { Audio = audio, Particles = particles };
        }
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (s_Instance == this)
            s_Instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single)
            return;
        Array.Clear(m_NextCueTime, 0, m_NextCueTime.Length);
        foreach (var voice in m_Voices)
        {
            // The menu click can finish after its originating scene unloads.
            if (voice == null || voice.IsUI)
                continue;
            voice.Audio.Stop();
            voice.Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            voice.BusyUntil = 0f;
        }
    }

    void PlayCue(FeedbackCue cue, Vector3 position, bool ui, bool sound, bool visual)
    {
        int index = (int)cue;
        var style = GetStyle(cue);
        float now = Time.unscaledTime;
        if (sound && now < m_NextCueTime[index])
            return;
        if (sound)
            m_NextCueTime[index] = now + style.Cooldown;

        Voice selected = null;
        foreach (var voice in m_Voices)
        {
            if (voice.BusyUntil <= now)
            {
                selected = voice;
                break;
            }
        }
        // Under heavy input keep existing sounds intact instead of stacking more voices.
        if (selected == null)
            return;

        selected.Audio.Stop();
        selected.Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        selected.Audio.transform.position = position;
        selected.IsUI = ui;
        selected.Audio.clip = m_Clips[index];
        selected.Audio.spatialBlend = ui ? 0f : 1f;
        selected.Audio.volume = style.Volume * Volume;
        selected.Audio.pitch = cue == FeedbackCue.GameComplete || cue == FeedbackCue.Success ? 1f : UnityEngine.Random.Range(0.96f, 1.04f);
        selected.Audio.priority = cue == FeedbackCue.Focus || cue == FeedbackCue.UIHover ? 180 : 100;
        float duration = 0.1f;
        if (sound && selected.Audio.clip != null)
        {
            selected.Audio.Play();
            duration = selected.Audio.clip.length / selected.Audio.pitch;
        }

        if (visual && style.Count > 0)
        {
            var main = selected.Particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(style.Lifetime * 0.65f, style.Lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(style.Speed * 0.4f, style.Speed);
            main.startSize = new ParticleSystem.MinMaxCurve(style.Size * 0.5f, style.Size);
            main.startColor = style.Color;
            main.gravityModifier = cue == FeedbackCue.Heal || cue == FeedbackCue.Brew ? -0.12f : 0.03f;
            var shape = selected.Particles.shape;
            shape.enabled = true;
            shape.shapeType = cue == FeedbackCue.Place || cue == FeedbackCue.Impact ? ParticleSystemShapeType.Circle : ParticleSystemShapeType.Sphere;
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.radius = style.Radius;
            selected.Particles.Play();
            selected.Particles.Emit(style.Count);
            duration = Mathf.Max(duration, style.Lifetime);
        }
        selected.BusyUntil = now + duration + 0.12f;
    }

    static Vector3 VisualCenter(Transform target)
    {
        Bounds bounds = new Bounds(target.position, Vector3.zero);
        bool found = false;
        foreach (var renderer in target.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                continue;
            if (found) bounds.Encapsulate(renderer.bounds);
            else bounds = renderer.bounds;
            found = true;
        }
        return found ? bounds.center : target.position;
    }

    static Style GetStyle(FeedbackCue cue)
    {
        var style = new Style { Color = GroveUi.Accent, Count = 12, Radius = 0.12f, Speed = 0.65f,
            Lifetime = 0.55f, Size = 0.07f, Volume = 0.42f, Cooldown = 0.10f };
        switch (cue)
        {
            case FeedbackCue.Focus:
            case FeedbackCue.UIHover:
                style.Count = 4; style.Size = 0.035f; style.Speed = 0.2f; style.Lifetime = 0.3f;
                style.Volume = 0.15f; style.Cooldown = 0.3f; break;
            case FeedbackCue.Dialogue:
                style.Count = 4; style.Radius = 0.16f; style.Size = 0.04f; style.Speed = 0.15f;
                style.Volume = 0.23f; style.Cooldown = 0.12f; break;
            case FeedbackCue.Pickup:
                style.Color = new Color(0.6f, 0.92f, 1f); break;
            case FeedbackCue.Drop:
            case FeedbackCue.Impact:
                style.Color = new Color(0.8f, 0.69f, 0.48f); style.Count = 7; style.Speed = 0.35f;
                style.Lifetime = 0.32f; style.Volume = 0.3f; style.Cooldown = 0.16f; break;
            case FeedbackCue.Throw:
                style.Count = 10; style.Speed = 1.1f; style.Lifetime = 0.3f; break;
            case FeedbackCue.Recall:
            case FeedbackCue.Teleport:
                style.Color = new Color(0.42f, 0.84f, 1f); style.Count = 18; style.Cooldown = 0.3f; break;
            case FeedbackCue.Place:
                style.Color = GroveUi.Sage; style.Count = 14; style.Radius = 0.17f; break;
            case FeedbackCue.Harvest:
                style.Color = new Color(0.48f, 0.9f, 0.32f); style.Count = 22; style.Radius = 0.22f; style.Lifetime = 0.8f; break;
            case FeedbackCue.Brew:
                style.Color = new Color(0.71f, 0.48f, 1f); style.Count = 38; style.Radius = 0.24f;
                style.Lifetime = 1.2f; style.Volume = 0.55f; style.Cooldown = 0.6f; break;
            case FeedbackCue.Heal:
                style.Color = new Color(0.5f, 1f, 0.64f); style.Count = 40; style.Radius = 0.38f;
                style.Lifetime = 1.3f; style.Volume = 0.5f; style.Cooldown = 0.6f; break;
            case FeedbackCue.Reward:
            case FeedbackCue.Success:
            case FeedbackCue.Reunion:
                style.Color = new Color(1f, 0.82f, 0.4f); style.Count = 36; style.Radius = 0.28f;
                style.Lifetime = 1f; style.Size = 0.1f; style.Volume = 0.55f; style.Cooldown = 0.45f; break;
            case FeedbackCue.Surprise:
                style.Color = new Color(1f, 0.62f, 0.38f); style.Count = 18; style.Speed = 1.3f; style.Lifetime = 0.4f; break;
            case FeedbackCue.UIClick:
                style.Count = 0; style.Volume = 0.38f; style.Cooldown = 0.08f; break;
            case FeedbackCue.GameComplete:
                style.Count = 60; style.Lifetime = 1.5f; style.Volume = 0.65f; style.Cooldown = 1.5f; break;
            case FeedbackCue.Blocked:
                style.Color = new Color(1f, 0.6f, 0.3f); style.Count = 4; style.Speed = 0.15f;
                style.Volume = 0.2f; style.Cooldown = 0.6f; break;
        }
        return style;
    }
}
