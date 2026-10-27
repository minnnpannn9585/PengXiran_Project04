using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small world-space label used by the deer quest objects.
/// </summary>
public class WorldPrompt : MonoBehaviour
{
    TextMeshProUGUI m_Text;

    public static WorldPrompt Create(Transform parent)
    {
        var font = TMP_Settings.defaultFontAsset;
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null)
            return null;

        var root = new GameObject("Prompt");
        root.layer = 2;
        root.transform.SetParent(parent, false);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 70;
        canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;

        var rect = root.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(460f, 140f);
        rect.pivot = new Vector2(0.5f, 0f);

        var group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var textObject = new GameObject("Label");
        textObject.layer = 2;
        textObject.transform.SetParent(root.transform, false);
        var text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = 36f;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.98f, 0.94f, 0.88f);
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        var textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var prompt = root.AddComponent<WorldPrompt>();
        prompt.m_Text = text;
        root.SetActive(false);
        return prompt;
    }

    public void SetText(string value)
    {
        if (m_Text != null && m_Text.text != value)
            m_Text.text = value;
    }

    public void Place(Vector3 worldAnchor, float worldScale)
    {
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        transform.position = worldAnchor;
        float parentScale = Mathf.Max(0.0001f, transform.parent != null ? transform.parent.lossyScale.x : 1f);
        transform.localScale = Vector3.one * (worldScale / parentScale);

        var camera = Camera.main;
        if (camera == null)
            return;

        Vector3 forward = worldAnchor - camera.transform.position;
        if (forward.sqrMagnitude < 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    public void Hide()
    {
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }
}
