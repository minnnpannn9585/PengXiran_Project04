using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Exercises the actual scene's completion flow without saving any play-mode state.</summary>
[InitializeOnLoad]
public static class QuestCompletionValidation
{
    const string RunningKey = "Grove.QuestCompletionValidation";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator s_Checks;
    static double s_Deadline;
    static int s_LastFrame;

    static QuestCompletionValidation()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    [MenuItem("Tools/Grove/Validate Quest Completions (Play Mode)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before running validation.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != FoxQuestSetup.ScenePath || scene.isDirty)
            throw new InvalidOperationException("Open the saved DemoScene_01 forest scene before validation.");
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/QuestCompletionValidation.txt", "RUNNING\n");
        SessionState.SetBool(RunningKey, true);
        EditorApplication.isPlaying = true;
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode && s_Checks != null)
        {
            EditorApplication.update -= Tick;
            s_Checks = null;
            File.WriteAllText("Logs/QuestCompletionValidation.txt", "FAIL: validation was interrupted.\n");
        }
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(RunningKey, false))
            return;
        SessionState.SetBool(RunningKey, false);
        s_Checks = CheckFlow();
        s_LastFrame = -1;
        s_Deadline = EditorApplication.timeSinceStartup + 60;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > s_Deadline)
                throw new Exception("Completion validation timed out.");
            if (Time.frameCount == s_LastFrame)
                return;
            s_LastFrame = Time.frameCount;
            if (s_Checks.MoveNext())
                return;
            File.WriteAllText("Logs/QuestCompletionValidation.txt",
                "PASS: swept throw scoring and short-throw guard; automatic wolf/deer/final fox dialogue; "
                + "independent visible bursts and timed disposal; wolf/ball/marker and deer/medicine/herb/table cleanup; "
                + "necklace preserved; full fox encounter sequence; single readable ending panel; repeat completion guards; "
                + "all feedback clips contain audio; action sounds and particles; bounded reusable voices; button motion restores; "
                + "real teleport arrival; menu navigation and persistent audio.\n");
            Debug.Log("QUEST_COMPLETION_VALIDATION_OK");
        }
        catch (Exception error)
        {
            File.WriteAllText("Logs/QuestCompletionValidation.txt", "FAIL: " + error);
            Debug.LogException(error);
        }
        EditorApplication.update -= Tick;
        s_Checks = null;
        EditorApplication.isPlaying = false;
    }

    static IEnumerator CheckFlow()
    {
        yield return null;
        float longestFeedbackClip = CheckFeedbackAssets();
        var manager = Object.FindObjectOfType<GameManager>();
        var quest = Object.FindObjectOfType<FoxReunion>();
        var deer = Object.FindObjectOfType<DeerRescue>();
        Assert(manager != null && quest != null && deer != null, "Missing quest components in scene.");
        var wolf = Read<DialogueInteractable>(manager, "m_Wolf");
        var ball = Read<LostBall>(manager, "m_Ball");
        var target = Read<BallTarget>(manager, "m_Target");
        var fox = Read<DialogueInteractable>(quest, "m_Fox");
        var rabbits = Read<DialogueInteractable[]>(quest, "m_Rabbits");
        var trails = Read<FootprintTrail[]>(quest, "m_Trails");
        foreach (var existing in Object.FindObjectsOfType<Camera>())
        {
            existing.tag = "Untagged";
            existing.enabled = false;
        }
        var camera = new GameObject("Completion Validation Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        LookAt(camera, wolf.transform);
        Write(quest, "m_Camera", camera);
        Assert(Object.FindObjectOfType<GameCompletionPanel>() == null, "Ending panel appeared before completing tasks.");

        // Enter the throw phase, then exercise the real swept target check, not ScoreThrow directly.
        Write(manager, "m_Phase", GamePhase.PlayBall);
        target.gameObject.SetActive(true);
        ball.PrepareForPlay();
        ball.GetComponent<Rigidbody>().isKinematic = true;
        Vector3 landing = target.transform.position + Vector3.up * 0.25f;
        ball.transform.position = landing;
        Write(manager, "m_ThrowInFlight", true);
        Write(manager, "m_FlightTime", 0.3f);
        Write(manager, "m_ThrowOrigin", landing + Vector3.right * 0.1f);
        Write(manager, "m_ThrowPrevious", landing + Vector3.right * 0.1f);
        Invoke(manager, "UpdateThrow");
        Assert(manager.Phase == GamePhase.PlayBall, "Moving the ball a short distance incorrectly scored.");
        Write(manager, "m_ThrowOrigin", landing + Vector3.right * 4f);
        Write(manager, "m_ThrowPrevious", landing + Vector3.right * 2f);
        Invoke(manager, "UpdateThrow");
        Assert(manager.Phase == GamePhase.Complete, "A valid swept marker hit did not complete stage one.");
        AssertFeedbackCue(FeedbackCue.Success);
        Assert(ball.GetComponent<Rigidbody>().isKinematic && !ball.IsHeld && !ball.IsRecalling,
            "Scored ball was not frozen and released.");
        Assert(wolf.gameObject.activeInHierarchy && ball.gameObject.activeInHierarchy && target.gameObject.activeInHierarchy,
            "Stage one objects disappeared before the congratulations could be read.");
        Assert(wolf.Data == Read<DialogueData>(manager, "m_ScoredDialogue"), "Wrong wolf completion dialogue.");
        Invoke(manager, "ScoreThrow");
        while (wolf.gameObject.activeInHierarchy)
            yield return null;
        Assert(!ball.gameObject.activeInHierarchy && !target.gameObject.activeInHierarchy, "Ball or marker survived completion.");
        var wolfBursts = CompletionBursts();
        AssertBurstFor(wolfBursts, wolf.gameObject);
        AssertBurstFor(wolfBursts, ball.gameObject);
        AssertBurstFor(wolfBursts, target.gameObject);
        Invoke(manager, "ScoreThrow");
        Assert(CompletionBursts().Length == wolfBursts.Length, "Duplicate score created more bursts.");
        Assert(quest.Phase == FoxPhase.Waiting, "Stage three unlocked before helping the deer.");

        // Use the normal harvesting, brewing and medicine delivery APIs.
        LookAt(camera, deer.transform);
        var table = Object.FindObjectOfType<MedicineTable>();
        Assert(table != null, "Medicine table is missing.");
        var herbs = Object.FindObjectsOfType<Herb>(true);
        foreach (HerbKind kind in new[] { HerbKind.RedFlower, HerbKind.PurpleFlower })
        {
            var herb = herbs.FirstOrDefault(item => item.Kind == kind && item.gameObject.activeInHierarchy);
            Assert(herb != null, "Missing active herb: " + kind);
            Assert(herb.GetComponent<CarryItem>().PickUp(null), "Could not pick up " + kind);
            AssertFeedbackCue(FeedbackCue.Harvest);
            Assert(table.TryPlaceHeld(), "Could not place " + kind);
            AssertFeedbackCue(FeedbackCue.Place);
        }
        Assert(table.TryGrind(), "Both herbs did not brew medicine.");
        AssertFeedbackCue(FeedbackCue.Brew);
        var bottle = Read<MedicineBottle>(table, "m_Bottle");
        Assert(bottle != null && bottle.GetComponent<CarryItem>().PickUp(null), "Could not pick up medicine.");
        AssertFeedbackCue(FeedbackCue.Pickup);
        var necklace = Read<CarryItem>(deer, "m_Necklace");
        var props = Object.FindObjectsOfType<Herb>(true).Select(item => item.gameObject)
            .Concat(Object.FindObjectsOfType<HerbPlant>(true).Select(item => item.gameObject))
            .Concat(Object.FindObjectsOfType<MedicineBottle>(true).Select(item => item.gameObject))
            .Concat(Object.FindObjectsOfType<MedicineTable>(true).Select(item => item.gameObject)).Distinct().ToArray();
        Assert(deer.TryGiveMedicine(), "Medicine delivery did not heal the deer.");
        AssertFeedbackCue(FeedbackCue.Heal);
        AssertFeedbackCue(FeedbackCue.Reward);
        Assert(!deer.TryGiveMedicine(), "A repeated handoff was accepted.");
        Assert(manager.DeerQuest == DeerPhase.Healed, "Deer completion state was not set.");
        Assert(deer.gameObject.activeInHierarchy && bottle.gameObject.activeInHierarchy,
            "Deer or medicine disappeared before the thanks dialogue.");
        while (deer.gameObject.activeInHierarchy)
            yield return null;
        Assert(props.All(item => !item.activeInHierarchy), "A stage two prop survived completion.");
        Assert(necklace != null && necklace.gameObject.activeInHierarchy && !necklace.transform.IsChildOf(deer.transform),
            "The player's necklace reward disappeared with the deer.");
        Assert(CarryItem.FindHeld<MedicineBottle>() == null, "Consumed medicine remained held.");
        var deerBursts = CompletionBursts();
        AssertBurstFor(deerBursts, deer.gameObject);
        AssertBurstFor(deerBursts, bottle.gameObject);
        AssertBurstFor(deerBursts, table.gameObject);
        Assert(wolfBursts.All(item => item == null), "Stage one particle objects did not dispose themselves.");

        // Traverse all encounters; the last thank-you advances on its own at normal reading speed.
        int completions = 0;
        quest.Completed += () => completions++;
        LookAt(camera, fox.transform);
        yield return null;
        Assert(quest.Phase == FoxPhase.TalkToFox, "Earlier tasks did not unlock stage three.");
        float normalFoxSpeed = Read<float>(fox, "m_CharactersPerSecond");
        foreach (var step in Finish(fox, () => quest.Phase != FoxPhase.TalkToFox)) yield return step;
        camera.transform.position = rabbits[0].transform.position + new Vector3(0f, 1.6f, -1.6f);
        yield return null;
        Assert(quest.Phase == FoxPhase.WrongColor, "First encounter failed.");
        foreach (var step in Finish(rabbits[0], () => quest.Phase != FoxPhase.WrongColor)) yield return step;
        camera.transform.position = rabbits[1].transform.position + new Vector3(0f, 1.6f, -1.6f);
        yield return null;
        Assert(quest.Phase == FoxPhase.Scream, "Second encounter failed.");
        while (quest.Phase == FoxPhase.Scream || quest.Phase == FoxPhase.Fleeing) yield return null;
        Assert(quest.Phase == FoxPhase.Speechless, "Fox reaction was skipped.");
        foreach (var step in Finish(fox, () => quest.Phase != FoxPhase.Speechless)) yield return step;
        Write(fox, "m_CharactersPerSecond", normalFoxSpeed);
        camera.transform.position = rabbits[2].transform.position + new Vector3(0f, 1.6f, -1.6f);
        yield return null;
        Assert(quest.Phase == FoxPhase.Reunion, "Final reunion failed.");
        AssertFeedbackCue(FeedbackCue.Reunion);
        foreach (var step in Finish(rabbits[2], () => quest.Phase != FoxPhase.Reunion)) yield return step;
        Assert(quest.Phase == FoxPhase.Thanks, "Final thanks were skipped.");
        Assert(Object.FindObjectOfType<GameCompletionPanel>() == null, "Ending appeared before final thanks.");
        while (!quest.IsComplete) yield return null;
        AssertFeedbackCue(FeedbackCue.GameComplete, false, true);
        Assert(completions == 1, "Completion event did not run exactly once.");
        Assert(trails.All(trail => !trail.gameObject.activeSelf), "A trail remained active after the ending.");
        Assert(deerBursts.All(item => item == null), "Stage two particle objects did not dispose themselves.");
        GameCompletionPanel.Show();
        Assert(Object.FindObjectsOfType<GameCompletionPanel>().Length == 1, "Repeated completion duplicated the panel.");
        Invoke(quest, "OnFoxFinished", fox);
        Assert(completions == 1, "Repeated final dialogue completed the game twice.");
        var panel = Object.FindObjectOfType<GameCompletionPanel>();
        float readyAt = Time.unscaledTime + 1f;
        while (Time.unscaledTime < readyAt) yield return null;
        Assert(panel.GetComponent<CanvasGroup>().alpha > 0.99f, "Ending panel did not fade in.");
        Assert(panel.GetComponentInChildren<Button>().interactable, "Return-to-menu button never became usable.");
        foreach (var text in panel.GetComponentsInChildren<TMP_Text>())
        {
            text.ForceMeshUpdate();
            Assert(!text.isTextOverflowing, "Ending text overflows: " + text.name);
        }
        ScreenCapture.CaptureScreenshot("Logs/QuestCompletionPanel.png");
        for (int i = 0; i < 4; i++) yield return null;

        // Exercise the actual ending button's pointer motion without leaving the test scene.
        var button = panel.GetComponentInChildren<Button>();
        var buttonFeedback = button.GetComponent<FeedbackButton>();
        Assert(buttonFeedback != null, "Ending button has no interaction feedback.");
        Vector3 restScale = button.transform.localScale;
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        buttonFeedback.OnPointerEnter(pointer);
        AssertFeedbackCue(FeedbackCue.UIHover, false, true);
        buttonFeedback.OnPointerDown(pointer);
        float pressedUntil = Time.unscaledTime + 0.18f;
        while (Time.unscaledTime < pressedUntil) yield return null;
        Assert(button.transform.localScale.x < restScale.x * 0.99f, "Button did not compress on press.");
        buttonFeedback.OnPointerUp(pointer);
        buttonFeedback.OnPointerExit(pointer);
        buttonFeedback.OnDeselect(new BaseEventData(EventSystem.current));
        float restoredAt = Time.unscaledTime + 0.4f;
        while (Time.unscaledTime < restoredAt) yield return null;
        Assert(Vector3.Distance(button.transform.localScale, restScale) < 0.003f, "Button scale did not restore after release.");

        // Rapid input must reuse bounded voices and leave no immortal particles or looping sounds.
        foreach (FeedbackCue cue in Enum.GetValues(typeof(FeedbackCue)))
            for (int repeat = 0; repeat < 4; repeat++)
                InteractionFeedback.Play(cue, camera.transform.position + camera.transform.forward);
        var feedback = AssertFeedbackPool();
        Assert(feedback.GetComponentsInChildren<ParticleSystem>().Any(system => system.particleCount > 0),
            "Feedback stress burst emitted no particles.");
        float quietAt = Time.unscaledTime + Mathf.Max(2f, longestFeedbackClip / 0.96f + 0.4f);
        while (Time.unscaledTime < quietAt) yield return null;
        AssertFeedbackPool();
        Assert(feedback.GetComponentsInChildren<AudioSource>().All(source => !source.isPlaying), "A feedback sound kept playing.");
        Assert(feedback.GetComponentsInChildren<ParticleSystem>().All(system => system.particleCount == 0),
            "Interaction particles did not expire.");

        var provider = Object.FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>();
        Assert(provider != null && provider.GetComponent<TeleportFeedback>() != null, "Teleport feedback was not attached.");
        var origin = provider.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
        Vector3 destination = origin.transform.position + Vector3.right * 0.5f;
        provider.QueueTeleportRequest(new UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportRequest
        {
            destinationPosition = destination,
            matchOrientation = UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.MatchOrientation.None
        });
        AudioSource arrival = null;
        while (arrival == null)
        {
            yield return null;
            arrival = feedback.GetComponentsInChildren<AudioSource>().FirstOrDefault(source =>
                source.clip != null && source.clip.name == "Teleport" && source.isPlaying);
        }
        Assert(Vector3.Distance(arrival.transform.position, destination + Vector3.up * 0.08f) < 0.01f,
            "Teleport feedback was not placed at the destination floor.");

        // Use real menu callbacks; the same pooled audio service must survive both transitions.
        button.onClick.Invoke();
        while (SceneManager.GetActiveScene().name != "MainMenu") yield return null;
        yield return null;
        Assert(AssertFeedbackPool() == feedback, "Returning to menu replaced the shared feedback service.");
        var menuButtons = Object.FindObjectsOfType<Button>();
        Assert(menuButtons.All(item => item.GetComponent<FeedbackButton>() != null), "A menu button has no feedback.");
        menuButtons.First(item => item.name == "Begin").onClick.Invoke();
        Assert(Read<bool>(Object.FindObjectOfType<OpeningFlow>(), "m_StoryActive"), "Begin button did not start the opening.");
        Assert(feedback.GetComponentsInChildren<AudioSource>().Any(source =>
            source.clip != null && source.clip.name == "UIClick" && source.isPlaying && source.spatialBlend == 0f),
            "Menu button did not play audible non-spatial click feedback.");
        float skipAt = Time.unscaledTime + 0.2f;
        while (Time.unscaledTime < skipAt) yield return null;
        menuButtons.First(item => item.name == "Skip").onClick.Invoke();
        while (SceneManager.GetActiveScene().path != FoxQuestSetup.ScenePath) yield return null;
        Assert(AssertFeedbackPool() == feedback, "Starting the game duplicated the feedback service.");
    }

    static float CheckFeedbackAssets()
    {
        float longestClip = 0f;
        foreach (FeedbackCue cue in Enum.GetValues(typeof(FeedbackCue)))
        {
            var clip = Resources.Load<AudioClip>("Feedback/Audio/" + cue);
            Assert(clip != null && clip.samples > 0 && clip.frequency > 0, "Missing or empty feedback audio: " + cue);
            clip.LoadAudioData();
            var samples = new float[clip.samples * clip.channels];
            Assert(clip.GetData(samples, 0), "Feedback audio PCM could not be read: " + cue);
            Assert(samples.Any(sample => Mathf.Abs(sample) > 0.0001f), "Feedback clip is silent: " + cue);
            Assert(samples.All(sample => !float.IsNaN(sample) && !float.IsInfinity(sample)), "Invalid PCM in " + cue);
            longestClip = Mathf.Max(longestClip, clip.length);
        }
        var material = Resources.Load<Material>("Effects/InteractionSpark");
        Assert(material != null && material.shader != null && material.shader.isSupported,
            "Interaction sparks have no supported material.");
        return longestClip;
    }

    static InteractionFeedback AssertFeedbackPool()
    {
        var services = Object.FindObjectsOfType<InteractionFeedback>();
        Assert(services.Length == 1, "Feedback must use exactly one shared service.");
        var voices = services[0].GetComponentsInChildren<AudioSource>();
        Assert(voices.Length > 0 && voices.Length <= 16, "Feedback audio voice count exceeded its limit.");
        Assert(services[0].GetComponentsInChildren<ParticleSystem>().Length <= 16, "Feedback particle pool exceeded its limit.");
        return services[0];
    }

    static void AssertFeedbackCue(FeedbackCue cue, bool particles = true, bool ui = false)
    {
        var feedback = AssertFeedbackPool();
        var clip = Resources.Load<AudioClip>("Feedback/Audio/" + cue);
        var voice = feedback.GetComponentsInChildren<AudioSource>().FirstOrDefault(source => source.clip == clip);
        Assert(voice != null && voice.volume > 0f && !voice.loop, "Action did not produce its feedback sound: " + cue);
        Assert(Mathf.Approximately(voice.spatialBlend, ui ? 0f : 1f), "Incorrect audio positioning for " + cue);
        if (particles)
            Assert(voice.GetComponent<ParticleSystem>().particleCount > 0, "Action did not produce feedback particles: " + cue);
    }

    static ParticleSystem[] CompletionBursts() => Object.FindObjectsOfType<ParticleSystem>()
        .Where(item => item.name.StartsWith("Quest Completion Burst - ")).ToArray();

    static void AssertBurstFor(ParticleSystem[] bursts, GameObject target)
    {
        var burst = bursts.FirstOrDefault(item => item.name == "Quest Completion Burst - " + target.name);
        Assert(burst != null && burst.gameObject.activeInHierarchy && burst.transform.parent == null,
            "No independent active completion burst for " + target.name);
        Assert(burst.particleCount > 0, "Completion burst did not emit for " + target.name);
        var material = burst.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        Assert(material != null && material.shader != null && material.shader.isSupported,
            "Completion burst has no supported material.");
    }

    static IEnumerable Finish(DialogueInteractable dialogue, Func<bool> done)
    {
        for (int i = 0; i < 50 && !done(); i++)
        {
            Write(dialogue, "m_NextAdvanceTime", 0f);
            Write(dialogue, "m_CharactersPerSecond", 100000f);
            Invoke(dialogue, "TryAdvance");
            yield return null;
        }
        Assert(done(), "Conversation did not finish: " + dialogue.name);
    }

    static void LookAt(Camera camera, Transform target)
    {
        camera.transform.position = target.position + new Vector3(0f, 1.6f, -3.5f);
        camera.transform.LookAt(target.position + Vector3.up * 0.6f);
    }

    static T Read<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
    static void Write(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
