using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared, tintable nine-slice surfaces and readable forest UI colors.</summary>
public static class GroveUi
{
    public static readonly Color Surface = new Color(0.075f, 0.13f, 0.11f, 0.97f);
    public static readonly Color Text = new Color(0.96f, 0.95f, 0.89f);
    public static readonly Color Muted = new Color(0.72f, 0.79f, 0.72f);
    public static readonly Color Accent = new Color(0.84f, 0.76f, 0.55f);
    public static readonly Color Sage = new Color(0.76f, 0.85f, 0.71f);

    static Sprite s_Rounded;
    public static Sprite Rounded
    {
        get
        {
            if (s_Rounded == null)
                s_Rounded = Resources.Load<Sprite>("UI/RoundedPanel");
            return s_Rounded;
        }
    }

    public static Image CreateSurface(string name, Transform parent, Color color, float radius = 24f)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.sprite = Rounded;
        image.type = Image.Type.Sliced;
        // The source has a 48px radius at 100 PPU, matching the Canvas reference PPU.
        // Only the center stretches; the corners keep their intended size.
        image.pixelsPerUnitMultiplier = 48f / Mathf.Max(1f, radius);
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset;
        rect.offsetMax = Vector2.one * -inset;
    }

    public static void AddPromptSurface(RectTransform root)
    {
        var surface = CreateSurface("Background", root, Surface, 18f);
        Stretch(surface.rectTransform);
        surface.transform.SetAsFirstSibling();
    }

    public static void StylePrompt(TextMeshProUGUI text)
    {
        text.color = Text;
        text.fontSize = 34f;
        text.fontStyle = FontStyles.Normal;
        text.lineSpacing = 6f;
        text.extraPadding = true;
        text.raycastTarget = false;
        text.enableWordWrapping = true;
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(24f, 16f);
        text.rectTransform.offsetMax = new Vector2(-24f, -16f);
    }

    public static void FitPrompt(TextMeshProUGUI text)
    {
        var root = (RectTransform)text.transform.parent;
        float height = text.GetPreferredValues(text.text, root.rect.width - 48f, Mathf.Infinity).y;
        root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(104f, height + 32f));
    }
}
