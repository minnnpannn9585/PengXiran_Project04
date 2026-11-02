using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

/// <summary>Creates the editable opening UI once; never replaces an authored canvas.</summary>
public static class OpeningUiBaker
{
    [MenuItem("Tools/Grove Elf/Bake Opening UI")]
    static void BakeSelectedScene()
    {
        var flow = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<OpeningFlow>() : null;
        if (flow == null)
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                flow = root.GetComponentInChildren<OpeningFlow>(true);
                if (flow != null)
                    break;
            }
        }

        if (flow == null)
        {
            Debug.LogWarning("Open MainMenu, or select an OpeningFlow component, before baking the opening UI.");
            return;
        }

        var canvas = Bake(flow);
        if (canvas != null)
            Selection.activeGameObject = canvas;
    }

    [MenuItem("Tools/Grove Elf/Bake Opening UI", true)]
    static bool CanBake()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    public static GameObject Bake(OpeningFlow flow)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Bake the opening UI outside Play mode.");
        if (flow == null || !flow.gameObject.scene.IsValid() || !flow.gameObject.scene.isLoaded)
            throw new ArgumentException("OpeningFlow must belong to a loaded scene.", nameof(flow));

        var serializedFlow = new SerializedObject(flow);
        var existing = serializedFlow.FindProperty("m_CanvasRect").objectReferenceValue as RectTransform;
        if (existing != null)
        {
            Debug.Log("Opening UI is already baked. Edit its scene objects directly; no changes were made.", existing);
            return existing.gameObject;
        }

        var scene = flow.gameObject.scene;
        foreach (var sceneRoot in scene.GetRootGameObjects())
        {
            foreach (var child in sceneRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name != "Opening Canvas")
                    continue;
                Debug.LogWarning("An Opening Canvas already exists. Reconnect it in OpeningFlow's Inspector; baking will not overwrite it.", child);
                return null;
            }
        }

        var font = TMP_Settings.defaultFontAsset != null ? TMP_Settings.defaultFontAsset
            : Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        var texture = Resources.Load<Texture2D>("UI/GroveMenuIllustration");
        if (font == null || texture == null)
            throw new InvalidOperationException("The opening font or UI/GroveMenuIllustration texture is missing.");

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Bake Opening UI");
        Undo.RecordObject(flow, "Connect baked opening UI");

        var root = new GameObject("Opening Canvas", typeof(RectTransform));
        root.layer = 5;
        SceneManager.MoveGameObjectToScene(root, scene);
        Undo.RegisterCreatedObjectUndo(root, "Create opening canvas");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 100;
        canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1680f, 945f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        root.AddComponent<TrackedDeviceGraphicRaycaster>().enabled = false;
        var rect = root.GetComponent<RectTransform>();

        var backdrop = CreateImage("Backdrop", rect, new Color(0.055f, 0.12f, 0.10f));
        Stretch(backdrop.rectTransform);
        var art = NewRect("Forest Illustration", rect);
        var illustration = art.gameObject.AddComponent<RawImage>();
        illustration.texture = texture;
        illustration.raycastTarget = false;
        Stretch(art);

        var menuGroup = CreateGroup("Menu", rect);
        var menuSafeArea = CreateSafeArea("Menu Safe Area", menuGroup.transform);
        var menu = NewRect("Menu Layout", menuSafeArea);
        Stretch(menu);

        var rule = GroveUi.CreateSurface("Rule", menu, GroveUi.Accent, 2f);
        rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rule.rectTransform.sizeDelta = new Vector2(64f, 3f);
        rule.rectTransform.anchoredPosition = new Vector2(-470f, 280f);

        var eyebrow = CreateText("Eyebrow", menu, font, 21f, FontStyles.Normal, GroveUi.Accent);
        eyebrow.text = "A FOREST STORY";
        eyebrow.characterSpacing = 5f;
        Place(eyebrow.rectTransform, new Vector2(580f, 40f), new Vector2(-470f, 330f));

        var title = CreateText("Title", menu, font, 116f, FontStyles.Bold, GroveUi.Text);
        title.text = "Grove\nElf";
        title.lineSpacing = -20f;
        Place(title.rectTransform, new Vector2(580f, 280f), new Vector2(-470f, 125f));

        var subtitle = CreateText("Subtitle", menu, font, 26f, FontStyles.Normal, GroveUi.Muted);
        subtitle.text = "A little kindness goes a long way.";
        Place(subtitle.rectTransform, new Vector2(590f, 70f), new Vector2(-470f, -65f));

        var begin = CreateChoice(menu, font, "Begin", new Vector2(-470f, -175f), flow.Begin, true);
        CreateChoice(menu, font, "Leave", new Vector2(-470f, -280f), flow.Leave);
        var footer = CreateText("Footer", menu, font, 18f, FontStyles.Normal, GroveUi.Accent);
        footer.text = "EXPLORE  /  HELP  /  BEFRIEND";
        footer.characterSpacing = 2f;
        Place(footer.rectTransform, new Vector2(590f, 40f), new Vector2(-470f, -395f));

        var storyGroup = CreateGroup("Story", rect);
        storyGroup.alpha = 0f;
        storyGroup.interactable = false;
        storyGroup.blocksRaycasts = false;
        var veil = CreateImage("Veil", storyGroup.transform, new Color(0.025f, 0.065f, 0.05f, 0.84f));
        Stretch(veil.rectTransform);
        var continueImage = CreateImage("Continue", storyGroup.transform, Color.clear);
        continueImage.raycastTarget = true;
        Stretch(continueImage.rectTransform);
        var continueButton = continueImage.gameObject.AddComponent<Button>();
        continueButton.targetGraphic = continueImage;
        continueButton.transition = Selectable.Transition.None;
        UnityEventTools.AddPersistentListener(continueButton.onClick, flow.Advance);
        var continueFeedback = continueImage.gameObject.AddComponent<FeedbackButton>();
        continueFeedback.Animate = false;
        continueFeedback.PlayClickSound = false;
        continueFeedback.PlayFocusSound = false;

        var storySafeArea = CreateSafeArea("Story Safe Area", storyGroup.transform);
        var story = NewRect("Story Layout", storySafeArea);
        Stretch(story);
        var storyLines = serializedFlow.FindProperty("m_Story");
        int storyLength = storyLines != null ? storyLines.arraySize : 0;
        var storyText = CreateText("Line", story, font, 48f, FontStyles.Normal, GroveUi.Text);
        storyText.lineSpacing = 12f;
        storyText.text = storyLength > 0 ? storyLines.GetArrayElementAtIndex(0).stringValue : string.Empty;
        Place(storyText.rectTransform, new Vector2(1200f, 360f), new Vector2(0f, 20f));

        var storyCount = CreateText("Count", story, font, 24f, FontStyles.Normal, GroveUi.Accent);
        storyCount.characterSpacing = 4f;
        storyCount.text = storyLength > 0 ? "1  /  " + storyLength : string.Empty;
        Place(storyCount.rectTransform, new Vector2(400f, 40f), new Vector2(0f, 150f));
        storyCount.rectTransform.anchorMin = storyCount.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        var storyHint = CreateText("Hint", story, font, 26f, FontStyles.Normal, GroveUi.Muted);
        storyHint.text = "Click or press Space to continue";
        Place(storyHint.rectTransform, new Vector2(1200f, 50f), new Vector2(0f, 90f));
        storyHint.rectTransform.anchorMin = storyHint.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        var skip = CreateChoice(story, font, "Skip", new Vector2(620f, 400f), flow.Skip);
        skip.GetComponent<RectTransform>().sizeDelta = new Vector2(220f, 64f);

        var layout = root.AddComponent<OpeningCanvasLayout>();
        var serializedLayout = new SerializedObject(layout);
        SetReference(serializedLayout, "m_MenuSafeArea", menuSafeArea);
        SetReference(serializedLayout, "m_StorySafeArea", storySafeArea);
        SetReference(serializedLayout, "m_Illustration", illustration);
        serializedLayout.FindProperty("m_ReferenceSize").vector2Value = new Vector2(1680f, 945f);
        serializedLayout.FindProperty("m_FitContent").boolValue = true;
        serializedLayout.FindProperty("m_FillIllustration").boolValue = true;
        serializedLayout.ApplyModifiedPropertiesWithoutUndo();

        SetReference(serializedFlow, "m_MenuGroup", menuGroup);
        SetReference(serializedFlow, "m_StoryGroup", storyGroup);
        SetReference(serializedFlow, "m_StoryText", storyText);
        SetReference(serializedFlow, "m_StoryHint", storyHint);
        SetReference(serializedFlow, "m_StoryCount", storyCount);
        SetReference(serializedFlow, "m_ContinueButton", continueButton);
        SetReference(serializedFlow, "m_BeginButton", begin);
        SetReference(serializedFlow, "m_CanvasRect", rect);
        serializedFlow.ApplyModifiedProperties();

        EnsureEventSystem(scene);
        EditorUtility.SetDirty(flow);
        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log("Opening UI baked into the scene. Edit Title, Subtitle, Begin, Leave and Forest Illustration directly in the Hierarchy.", root);
        return root;
    }

    static void EnsureEventSystem(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
            if (root.GetComponentInChildren<EventSystem>(true) != null)
                return;
        var eventObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        SceneManager.MoveGameObjectToScene(eventObject, scene);
        Undo.RegisterCreatedObjectUndo(eventObject, "Create opening event system");
        var module = eventObject.GetComponent<InputSystemUIInputModule>();
        const string path = "Packages/com.unity.inputsystem/InputSystem/Plugins/PlayerInput/DefaultInputActions.inputactions";
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        module.actionsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        module.point = FindAction(assets, "UI/Point");
        module.leftClick = FindAction(assets, "UI/Click");
        module.rightClick = FindAction(assets, "UI/RightClick");
        module.middleClick = FindAction(assets, "UI/MiddleClick");
        module.scrollWheel = FindAction(assets, "UI/ScrollWheel");
        module.move = FindAction(assets, "UI/Navigate");
        module.submit = FindAction(assets, "UI/Submit");
        module.cancel = FindAction(assets, "UI/Cancel");
        module.trackedDevicePosition = FindAction(assets, "UI/TrackedDevicePosition");
        module.trackedDeviceOrientation = FindAction(assets, "UI/TrackedDeviceOrientation");
    }

    static InputActionReference FindAction(UnityEngine.Object[] assets, string name)
    {
        foreach (var asset in assets)
            if (asset is InputActionReference reference && reference.name == name)
                return reference;
        throw new InvalidOperationException("The persistent default input action is missing: " + name);
    }

    static void SetReference(SerializedObject target, string field, UnityEngine.Object value)
    {
        target.FindProperty(field).objectReferenceValue = value;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static RectTransform CreateSafeArea(string name, Transform parent)
    {
        var rect = NewRect(name, parent);
        Place(rect, new Vector2(1680f, 945f), Vector2.zero);
        return rect;
    }

    static CanvasGroup CreateGroup(string name, Transform parent)
    {
        var rect = NewRect(name, parent);
        Stretch(rect);
        return rect.gameObject.AddComponent<CanvasGroup>();
    }

    static Image CreateImage(string name, Transform parent, Color color)
    {
        var image = NewRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static TextMeshProUGUI CreateText(string name, Transform parent, TMP_FontAsset font, float size, FontStyles style, Color color)
    {
        var text = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        text.extraPadding = true;
        return text;
    }

    static Button CreateChoice(Transform parent, TMP_FontAsset font, string label, Vector2 position, UnityAction action, bool primary = false)
    {
        var image = GroveUi.CreateSurface(label, parent, Color.white, 18f);
        image.raycastTarget = true;
        var button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        image.gameObject.AddComponent<FeedbackButton>();
        UnityEventTools.AddPersistentListener(button.onClick, action);
        Place(button.GetComponent<RectTransform>(), new Vector2(460f, 88f), position);
        var colors = button.colors;
        colors.normalColor = primary ? GroveUi.Sage : new Color(0.15f, 0.22f, 0.18f);
        colors.highlightedColor = primary ? new Color(0.87f, 0.94f, 0.80f) : new Color(0.25f, 0.34f, 0.28f);
        colors.pressedColor = primary ? new Color(0.61f, 0.73f, 0.55f) : new Color(0.11f, 0.17f, 0.14f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.22f, 0.27f, 0.23f, 0.5f);
        colors.fadeDuration = 0.12f;
        button.colors = colors;
        var text = CreateText("Label", button.transform, font, 32f,
            primary ? FontStyles.Bold : FontStyles.Normal,
            primary ? new Color(0.08f, 0.14f, 0.11f) : GroveUi.Text);
        text.text = label;
        Stretch(text.rectTransform);
        return button;
    }

    static void Place(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
