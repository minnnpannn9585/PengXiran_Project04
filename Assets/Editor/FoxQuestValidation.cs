using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Play-mode integration check against the actual forest scene, without saving test state.</summary>
[InitializeOnLoad]
public static class FoxQuestValidation
{
    const string RunningKey = "Grove.FoxQuestValidation";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator s_Checks;
    static double s_Deadline;
    static int s_Completions;
    static int s_LastFrame = -1;

    static FoxQuestValidation()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    [MenuItem("Tools/Grove/Validate Fox Reunion (Play Mode)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before running validation.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(FoxQuestSetup.ScenePath);
        SessionState.SetBool(RunningKey, true);
        EditorApplication.isPlaying = true;
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(RunningKey, false)) return;
        SessionState.SetBool(RunningKey, false);
        s_Checks = CheckFlow();
        s_LastFrame = -1;
        s_Deadline = EditorApplication.timeSinceStartup + 90;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > s_Deadline) throw new Exception("Validation timed out.");
            if (Time.frameCount == s_LastFrame) return;
            s_LastFrame = Time.frameCount;
            if (s_Checks.MoveNext()) return;
            File.WriteAllText("Logs/FoxQuestValidation.txt", "PASS: prerequisite gates, all three encounters, ordered trails, particle emission, flee movement, completion exactly once.\n");
            Debug.Log("FOX_VALIDATION_OK");
        }
        catch (Exception error)
        {
            File.WriteAllText("Logs/FoxQuestValidation.txt", "FAIL: " + error);
            Debug.LogException(error);
        }
        EditorApplication.update -= Tick;
        s_Checks = null;
        EditorApplication.isPlaying = false;
    }

    static IEnumerator CheckFlow()
    {
        yield return null;
        var quest = Object.FindObjectOfType<FoxReunion>();
        var manager = Object.FindObjectOfType<GameManager>();
        var fox = Read<DialogueInteractable>(quest, "m_Fox");
        var rabbits = Read<DialogueInteractable[]>(quest, "m_Rabbits");
        var trails = Read<FootprintTrail[]>(quest, "m_Trails");
        foreach (var existing in Object.FindObjectsOfType<Camera>())
        {
            existing.tag = "Untagged";
            existing.enabled = false;
        }
        var camera = new GameObject("Fox Quest Validation Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.allowHDR = true;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        camera.transform.position = fox.transform.position + new Vector3(0, 1.6f, -3.5f);
        camera.transform.LookAt(fox.transform.position + Vector3.up * 0.5f);
        Write(quest, "m_Camera", camera);
        s_Completions = 0;
        quest.Completed += () => s_Completions++;
        Assert(quest.Phase == FoxPhase.Waiting, "Quest must start locked.");
        CheckTrails(trails, -1);
        foreach (var rabbit in rabbits) Assert(!rabbit.gameObject.activeSelf, "Future rabbits must be hidden.");
        manager.OnDeerHealed();
        yield return null;
        Assert(quest.Phase == FoxPhase.Waiting, "Deer alone must not unlock stage three.");
        Write(manager, "m_Phase", GamePhase.Complete);
        yield return null;
        Assert(quest.Phase == FoxPhase.TalkToFox, "Both earlier quests must unlock introduction.");
        // Pointing at an inactive future rabbit must never skip a trail.
        Invoke(rabbits[2], "TryAdvance");
        Assert(quest.Phase == FoxPhase.TalkToFox, "Future interaction advanced the story.");
        foreach (var step in Finish(fox, () => quest.Phase != FoxPhase.TalkToFox)) yield return step;
        Assert(quest.Phase == FoxPhase.FollowPart1, "Introduction did not reveal trail one.");
        CheckTrails(trails, 0);
        yield return null;
        var previewPrint = trails[0].GetComponentsInChildren<MeshRenderer>()
            .OrderBy(print => Vector3.Distance(print.bounds.center, fox.transform.position)).First();
        camera.transform.position = previewPrint.bounds.center + Vector3.up * 1.6f;
        float wait = Time.time + 1.2f;
        while (Time.time < wait) yield return null;
        Assert(trails[0].GetComponentInChildren<ParticleSystem>().particleCount > 0, "Trail particles did not emit.");
        Capture(camera, "Logs/FoxTrailPreview.png", previewPrint.bounds.center);
        var bloom = UnityEngine.Rendering.VolumeManager.instance.stack.GetComponent<Bloom>();
        Debug.Log($"FOX_BLOOM: pipeline={UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name}, HDR={((UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline).supportsHDR}, active={bloom.active}, intensity={bloom.intensity.value}");
        camera.transform.position = rabbits[0].transform.position + new Vector3(0, 1.6f, -1.6f);
        yield return null;
        Assert(quest.Phase == FoxPhase.WrongColor, "First rabbit encounter failed: " + quest.Phase);
        CheckTrails(trails, -1);
        foreach (var step in Finish(rabbits[0], () => quest.Phase != FoxPhase.WrongColor)) yield return step;
        Assert(quest.Phase == FoxPhase.FollowPart2, "Wrong color must reveal trail two.");
        CheckTrails(trails, 1);
        Invoke(rabbits[0], "TryAdvance");
        Assert(quest.Phase == FoxPhase.FollowPart2, "Repeated first rabbit changed phase.");
        camera.transform.position = rabbits[1].transform.position + new Vector3(0, 1.6f, -1.6f);
        yield return null;
        Assert(quest.Phase == FoxPhase.Scream, "Second rabbit should scream first.");
        Vector3 start = rabbits[1].transform.position;
        while (quest.Phase == FoxPhase.Scream || quest.Phase == FoxPhase.Fleeing) yield return null;
        Assert((rabbits[1].transform.position - start).sqrMagnitude > 1f, "Scared rabbit did not run away.");
        Assert(!rabbits[1].gameObject.activeSelf, "Escaped rabbit should be hidden.");
        Assert(quest.Phase == FoxPhase.Speechless, "Fox reaction must precede trail three.");
        CheckTrails(trails, -1);
        foreach (var step in Finish(fox, () => quest.Phase != FoxPhase.Speechless)) yield return step;
        CheckTrails(trails, 2);
        camera.transform.position = rabbits[2].transform.position + new Vector3(0, 1.6f, -1.6f);
        yield return null;
        Assert(quest.Phase == FoxPhase.Reunion, "Correct rabbit encounter failed.");
        foreach (var step in Finish(rabbits[2], () => quest.Phase != FoxPhase.Reunion)) yield return step;
        Assert(quest.Phase == FoxPhase.Thanks, "Reunion should lead to thanks.");
        foreach (var step in Finish(fox, () => quest.Phase != FoxPhase.Thanks)) yield return step;
        Assert(quest.IsComplete && s_Completions == 1, "Completion must be emitted exactly once.");
        CheckTrails(trails, -1);
        for (int i = 0; i < 8; i++)
        {
            Advance(fox);
            yield return null;
        }
        Assert(s_Completions == 1 && quest.IsComplete, "Post-completion dialogue restarted the quest.");
        Assert(manager.Phase == GamePhase.Complete && manager.DeerQuest == DeerPhase.Healed, "Earlier quest states changed.");
    }

    static IEnumerable Finish(DialogueInteractable dialogue, Func<bool> done)
    {
        for (int i = 0; i < 50 && !done(); i++)
        {
            Advance(dialogue);
            yield return null;
        }
        Assert(done(), "Conversation did not finish: " + dialogue.name);
    }

    static void Advance(DialogueInteractable dialogue)
    {
        Write(dialogue, "m_NextAdvanceTime", 0f);
        Write(dialogue, "m_CharactersPerSecond", 100000f);
        Invoke(dialogue, "TryAdvance");
    }

    static void CheckTrails(FootprintTrail[] trails, int active)
    {
        for (int i = 0; i < trails.Length; i++)
            Assert(trails[i].gameObject.activeSelf == (i == active), "Incorrect active trail: " + (i + 1));
    }

    static void Capture(Camera camera, string path, Vector3 target)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        Vector3 oldPosition = camera.transform.position;
        Quaternion oldRotation = camera.transform.rotation;
        camera.transform.position = target + new Vector3(1.3f, 1.4f, -1.8f);
        camera.transform.LookAt(target);
        var texture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBHalf);
        camera.targetTexture = texture;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = texture;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        Object.Destroy(image);
        Object.Destroy(texture);
        camera.transform.SetPositionAndRotation(oldPosition, oldRotation);
    }

    static T Read<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
    static void Write(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static void Invoke(object target, string method) => target.GetType().GetMethod(method, Private).Invoke(target, null);
    static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
