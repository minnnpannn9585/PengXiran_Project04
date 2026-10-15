using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Landing zone for the throw. The ball passes when it travels into this marker.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SphereCollider))]
[AddComponentMenu("Ball/Ball Target")]
public class BallTarget : MonoBehaviour
{
    [SerializeField] float m_Radius = 1.2f;
    [SerializeField] float m_Height = 1.4f;

    GameObject m_Label;
    TextMeshProUGUI m_LabelText;

    void Awake()
    {
        var collider = GetComponent<SphereCollider>();
        if (collider != null)
            collider.isTrigger = true;

        BuildLabel();
    }

    void LateUpdate()
    {
        if (m_Label == null)
            return;

        var camera = Camera.main;
        if (camera == null)
            return;

        Vector3 anchor = transform.position + Vector3.up * 1.15f;
        m_Label.transform.position = anchor;
        Vector3 forward = anchor - camera.transform.position;
        if (forward.sqrMagnitude < 0.0001f)
            return;

        m_Label.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    public bool ContainsPoint(Vector3 point)
    {
        Vector3 delta = point - transform.position;
        float horizontal = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
        return horizontal <= m_Radius && delta.y >= -0.25f && delta.y <= m_Height;
    }

    public bool ContainsSegment(Vector3 from, Vector3 to)
    {
        const int steps = 8;
        for (int i = 0; i <= steps; i++)
        {
            if (ContainsPoint(Vector3.Lerp(from, to, i / (float)steps)))
                return true;
        }

        return false;
    }

    void BuildLabel()
    {
        var font = TMP_Settings.defaultFontAsset;
        if (font == null)
            font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font == null)
            return;

        m_Label = new GameObject("TargetLabel");
        m_Label.layer = 2;
        m_Label.transform.SetParent(transform, false);

        var canvas = m_Label.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 50;
        canvas.additionalShaderChannels =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.Normal |
            AdditionalCanvasShaderChannels.Tangent;

        var rect = m_Label.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(520f, 120f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.localScale = Vector3.one * 0.004f;

        var group = m_Label.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var textObject = new GameObject("Label");
        textObject.layer = 2;
        textObject.transform.SetParent(m_Label.transform, false);
        m_LabelText = textObject.AddComponent<TextMeshProUGUI>();
        m_LabelText.font = font;
        m_LabelText.fontSize = 42f;
        m_LabelText.fontStyle = FontStyles.Bold;
        m_LabelText.alignment = TextAlignmentOptions.Center;
        m_LabelText.color = new Color(1f, 0.93f, 0.55f);
        m_LabelText.text = "Throw here";
        m_LabelText.raycastTarget = false;
        var textRect = m_LabelText.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
    }
}
