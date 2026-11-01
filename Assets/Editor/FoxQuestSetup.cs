using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Rebuildable scene wiring for the existing StageThree animals and footprint layout.</summary>
public static class FoxQuestSetup
{
    public const string ScenePath = "Assets/StylizedNatureBundle/Demo/Scenes/DemoScene_01.unity";
    const string DataFolder = "Assets/Dialogue/Fox";
    const string FoxController = "Assets/PolyArt/Animals/PolyArt_Forest_Animals/Foxes/FBX/Adult/LowPoly_Fox_Demo_AContr.controller";
    const string HareController = "Assets/PolyArt/Animals/PolyArt_Forest_Animals/Hares/FBX/Adult/Hare_AContr_Demo.controller";

    [MenuItem("Tools/Grove/Set Up Fox Reunion")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before setting up the fox quest.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory("Assets/Audio");
        Directory.CreateDirectory("Assets/Animations/FoxQuest");
        AssetDatabase.Refresh();

        var waiting = Dialogue("FoxWaiting", "Fox", "I am looking for an old friend. First, please help the wolf and the injured fawn.", "Come back when they are safe. I will be waiting here.");
        var introduction = Dialogue("FoxIntroduction", "Fox", "A rabbit helped me when I was lost and hungry, a long time ago.", "I never had a chance to thank my friend. I want to repay that kindness.", "Will you help me find that rabbit? Follow the glowing blue paw prints. I will come with you.");
        var searching = Dialogue("FoxSearching", "Fox", "Let's follow the blue paw prints. My old friend must be somewhere ahead.");
        var wrong = Dialogue("RabbitWrongColor", "Fox", "Wait... this rabbit's fur is a different color.", "This isn't the rabbit who helped me. Sorry for the mix-up!", "There is another trail. Let's follow the next blue paw prints.");
        var scream = Dialogue("RabbitScream", "Rabbit", "AAAAH! A FOX! Don't eat me!");
        var speechless = Dialogue("FoxSpeechless", "Fox", "...", "I didn't even get to say hello. I only wanted to say thank you...", "Well. Let's try the last trail of blue paw prints.");
        var reunion = Dialogue("RabbitReunion", "Rabbit", "Oh! I remember you. You were the little fox lost in the woods!", "I'm glad you made it home. It is lovely to see you again.");
        var thanks = Dialogue("FoxThanks", "Fox", "It's you! I finally found you. I never forgot how you helped me.", "Now it is my turn. Whenever you need help, you can count on me.", "Thank you for bringing us together. My search is over!");
        var complete = Dialogue("FoxComplete", "Fox", "Thanks to you, I found the rabbit who helped me. I can finally return that kindness.");

        var stage = Find("StageThree");
        var fox = ConfigureDialogue(Find("Fox_1"), waiting, new Vector3(0.75f, 0.85f, 1.2f));
        var rabbits = new[]
        {
            ConfigureDialogue(Find("Rabbit_WrongColor"), wrong, new Vector3(0.55f, 0.8f, 0.8f)),
            ConfigureDialogue(Find("Rabbit_Scream"), scream, new Vector3(0.55f, 0.8f, 0.8f)),
            ConfigureDialogue(Find("Rabbit_Correct"), reunion, new Vector3(0.55f, 0.8f, 0.8f))
        };
        var foxAnimator = MakeController("FoxQuest", FoxController, "Arm_Fox|");
        var rabbitAnimator = MakeController("RabbitQuest", HareController, "Arm_Hare|");
        var foxMotion = ConfigureMotion(fox.gameObject, foxAnimator);
        QuestAnimalMotion scaredMotion = null;
        for (int i = 0; i < rabbits.Length; i++)
        {
            var motion = ConfigureMotion(rabbits[i].gameObject, rabbitAnimator);
            if (i == 1) scaredMotion = motion;
            rabbits[i].gameObject.SetActive(false);
        }

        var shader = Shader.Find("Grove/Rabbit Trail Glow");
        if (shader == null) throw new InvalidOperationException("Rabbit trail shader did not import.");
        var footprint = AssetDatabase.LoadAssetAtPath<Material>("Assets/Mesh/RabbitFoot.mat");
        footprint.shader = shader;
        footprint.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Mesh/rabbitFoot.png"));
        footprint.SetColor("_GlowColor", new Color(0.08f, 0.65f, 5.5f, 1f));
        footprint.SetFloat("_UseTexture", 1f);
        footprint.SetFloat("_PulseAmount", 0.12f);
        footprint.shaderKeywords = Array.Empty<string>();
        footprint.renderQueue = 3000;
        footprint.enableInstancing = true;
        EditorUtility.SetDirty(footprint);
        var particles = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/RabbitTrailWisps.mat");
        if (particles == null)
        {
            particles = new Material(shader);
            AssetDatabase.CreateAsset(particles, "Assets/Materials/RabbitTrailWisps.mat");
        }
        particles.SetColor("_GlowColor", new Color(0.15f, 0.9f, 5f, 1f));
        particles.SetFloat("_UseTexture", 0f);
        particles.SetFloat("_PulseAmount", 0.1f);
        EditorUtility.SetDirty(particles);
        var trails = new FootprintTrail[3];
        for (int i = 0; i < trails.Length; i++)
        {
            var root = Find("part" + (i + 1));
            trails[i] = GetOrAdd<FootprintTrail>(root);
            Set(trails[i], "m_ParticleMaterial", particles);
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.sharedMaterial = footprint;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.gameObject.layer = 2; // Decorative quads must not block interaction/ground raycasts.
                foreach (var collider in renderer.GetComponents<Collider>())
                    Object.DestroyImmediate(collider);
            }
            root.SetActive(false);
        }

        var escape = stage.transform.Find("Rabbit Escape Point");
        if (escape == null)
        {
            escape = new GameObject("Rabbit Escape Point").transform;
            escape.SetParent(stage.transform, false);
            // Away from the incoming second trail, on the same patch of forest floor.
            escape.position = rabbits[1].transform.position + Vector3.forward * -9f;
        }
        var quest = GetOrAdd<FoxReunion>(stage);
        Set(quest, "m_Manager", Object.FindObjectOfType<GameManager>());
        Set(quest, "m_Fox", fox);
        SetArray(quest, "m_Rabbits", rabbits);
        SetArray(quest, "m_Trails", trails);
        Set(quest, "m_FoxMotion", foxMotion);
        Set(quest, "m_ScaredRabbitMotion", scaredMotion);
        Set(quest, "m_FleeDestination", escape);
        Set(quest, "m_ScreamSound", MakeSqueak());
        Set(quest, "m_Introduction", introduction);
        Set(quest, "m_Searching", searching);
        Set(quest, "m_Speechless", speechless);
        Set(quest, "m_Thanks", thanks);
        Set(quest, "m_Complete", complete);
        ConfigureBloom();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(stage.scene);
        EditorSceneManager.SaveScene(stage.scene);
        Debug.Log("FOX_SETUP_OK: three trails, four animals, dialogues, squeak, HDR and Bloom saved.");
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }
    public static GameObject Find(string name)
    {
        return Resources.FindObjectsOfTypeAll<Transform>()
            .First(t => t.gameObject.scene.IsValid() && t.name == name && t.parent?.name != name).gameObject;
    }

    static DialogueInteractable ConfigureDialogue(GameObject go, DialogueData data, Vector3 size)
    {
        var collider = GetOrAdd<BoxCollider>(go);
        collider.isTrigger = false;
        collider.size = size;
        collider.center = Vector3.up * size.y * 0.5f;
        var dialogue = GetOrAdd<DialogueInteractable>(go);
        Set(dialogue, "m_Data", data);
        var serialized = new SerializedObject(dialogue);
        serialized.FindProperty("m_Loop").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return dialogue;
    }

    static QuestAnimalMotion ConfigureMotion(GameObject go, RuntimeAnimatorController controller)
    {
        var animator = go.GetComponentInChildren<Animator>(true);
        if (animator == null) animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        var motion = GetOrAdd<QuestAnimalMotion>(go);
        Set(motion, "m_Animator", animator);
        var serialized = new SerializedObject(motion);
        serialized.FindProperty("m_IdleState").stringValue = "Idle";
        serialized.FindProperty("m_WalkState").stringValue = "Walk";
        serialized.FindProperty("m_RunState").stringValue = "Run";
        serialized.FindProperty("m_FearState").stringValue = "Fear";
        serialized.FindProperty("m_GroundLayers").intValue = Physics.DefaultRaycastLayers;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return motion;
    }

    static AnimatorController MakeController(string name, string sourcePath, string prefix)
    {
        string path = "Assets/Animations/FoxQuest/" + name + ".controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller != null) return controller;
        controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var source = AssetDatabase.LoadAssetAtPath<AnimatorController>(sourcePath);
        string[] names = { "Idle", "Walk", "Run", "Fear" };
        string[] clips = { "Idle_1", "Walk_F_IP", "Run_F_IP", "Fear" };
        for (int i = 0; i < names.Length; i++)
        {
            var original = source.layers[0].stateMachine.states.FirstOrDefault(s => s.state.name == prefix + clips[i]).state;
            if (original == null) continue;
            var state = controller.layers[0].stateMachine.AddState(names[i]);
            state.motion = original.motion;
            if (i == 0) controller.layers[0].stateMachine.defaultState = state;
        }
        return controller;
    }

    static DialogueData Dialogue(string name, string speaker, params string[] lines)
    {
        string path = DataFolder + "/" + name + ".asset";
        var data = AssetDatabase.LoadAssetAtPath<DialogueData>(path);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<DialogueData>();
            AssetDatabase.CreateAsset(data, path);
        }
        var serialized = new SerializedObject(data);
        serialized.FindProperty("m_SpeakerName").stringValue = speaker;
        var list = serialized.FindProperty("m_Lines");
        list.arraySize = lines.Length;
        for (int i = 0; i < lines.Length; i++) list.GetArrayElementAtIndex(i).stringValue = lines[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return data;
    }

    static AudioClip MakeSqueak()
    {
        const string path = "Assets/Audio/RabbitStartled.wav";
        if (!File.Exists(path))
        {
            const int rate = 22050;
            const int count = 17640;
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                double phase = 0;
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / count;
                    phase += 2 * Math.PI * (900 + 950 * Math.Sin(t * Math.PI) + 90 * Math.Sin(t * 65)) / rate;
                    double envelope = Math.Sin(Math.PI * t) * Math.Min(1, t * 25);
                    writer.Write((short)(11000 * envelope * (Math.Sin(phase) + 0.18 * Math.Sin(phase * 2))));
                }
            }
            AssetDatabase.ImportAsset(path);
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static void ConfigureBloom()
    {
        const string path = "Assets/Settings/FoxTrailBloom.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        if (!profile.TryGet<Bloom>(out var bloom))
        {
            bloom = profile.Add<Bloom>(true);
            AssetDatabase.AddObjectToAsset(bloom, profile);
        }
        bloom.threshold.Override(1.1f);
        bloom.intensity.Override(0.7f);
        bloom.scatter.Override(0.65f);
        bloom.highQualityFiltering.Override(false);
        EditorUtility.SetDirty(bloom);
        EditorUtility.SetDirty(profile);
        var volume = Object.FindObjectsOfType<Volume>(true).FirstOrDefault(v => v.name == "Rabbit Trail Bloom");
        if (volume == null) volume = new GameObject("Rabbit Trail Bloom").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 20f;
        volume.sharedProfile = profile;
        foreach (var camera in Object.FindObjectsOfType<Camera>(true))
        {
            camera.allowHDR = true;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.volumeLayerMask |= 1 << volume.gameObject.layer;
        }
        // Every configured quality tier needs HDR, including the Android/VR tier.
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            var pipeline = QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset;
            if (pipeline == null) continue;
            pipeline.supportsHDR = true;
            EnsurePostProcessing(pipeline);
            EditorUtility.SetDirty(pipeline);
        }
        var defaultPipeline = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (defaultPipeline != null)
        {
            defaultPipeline.supportsHDR = true;
            EnsurePostProcessing(defaultPipeline);
            EditorUtility.SetDirty(defaultPipeline);
        }
    }

    static void EnsurePostProcessing(UniversalRenderPipelineAsset pipeline)
    {
        var serialized = new SerializedObject(pipeline);
        var renderers = serialized.FindProperty("m_RendererDataList");
        for (int i = 0; i < renderers.arraySize; i++)
        {
            var renderer = renderers.GetArrayElementAtIndex(i).objectReferenceValue;
            if (renderer == null) continue;
            var data = new SerializedObject(renderer);
            var post = data.FindProperty("postProcessData");
            if (post == null || post.objectReferenceValue != null) continue;
            post.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Object>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void Set(Object target, string property, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(property).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetArray(Object target, string property, Object[] values)
    {
        var serialized = new SerializedObject(target);
        var array = serialized.FindProperty(property);
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
