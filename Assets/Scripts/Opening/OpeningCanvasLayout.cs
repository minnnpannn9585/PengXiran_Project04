using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fits only the dedicated safe-area wrappers. Their child layouts are authored
/// in the scene and are never repositioned, restyled or rebuilt by this component.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed class OpeningCanvasLayout : MonoBehaviour
{
    [SerializeField] RectTransform m_MenuSafeArea;
    [SerializeField] RectTransform m_StorySafeArea;
    [SerializeField] RawImage m_Illustration;
    [SerializeField] Vector2 m_ReferenceSize = new Vector2(1680f, 945f);
    [Tooltip("Fit the safe-area wrappers to the Canvas. Edit their child layouts freely.")]
    [SerializeField] bool m_FitContent = true;
    [Tooltip("Crop the background to fill its RectTransform without stretching. Disable to edit UV Rect manually.")]
    [SerializeField] bool m_FillIllustration = true;

    void LateUpdate()
    {
        RefreshLayout();
    }

    public void RefreshLayout()
    {
        var canvasRect = transform as RectTransform;
        if (canvasRect == null)
            return;

        Vector2 size = canvasRect.rect.size;
        if (size.x <= 0f || size.y <= 0f)
            return;

        if (m_FitContent && m_ReferenceSize.x > 0f && m_ReferenceSize.y > 0f)
        {
            float scale = Mathf.Min(size.x / m_ReferenceSize.x, size.y / m_ReferenceSize.y);
            FitSafeArea(m_MenuSafeArea, scale);
            FitSafeArea(m_StorySafeArea, scale);
        }

        if (!m_FillIllustration || m_Illustration == null || m_Illustration.texture == null)
            return;

        Vector2 artSize = m_Illustration.rectTransform.rect.size;
        if (artSize.x <= 0f || artSize.y <= 0f)
            return;

        float imageAspect = (float)m_Illustration.texture.width / m_Illustration.texture.height;
        float viewAspect = artSize.x / artSize.y;
        Rect uv;
        if (viewAspect < imageAspect)
        {
            float width = viewAspect / imageAspect;
            uv = new Rect((1f - width) * 0.5f, 0f, width, 1f);
        }
        else
        {
            float height = imageAspect / viewAspect;
            uv = new Rect(0f, (1f - height) * 0.5f, 1f, height);
        }

        if (m_Illustration.uvRect != uv)
            m_Illustration.uvRect = uv;
    }

    void FitSafeArea(RectTransform area, float scale)
    {
        if (area == null)
            return;
        if (area.sizeDelta != m_ReferenceSize)
            area.sizeDelta = m_ReferenceSize;
        Vector3 fittedScale = Vector3.one * scale;
        if (area.localScale != fittedScale)
            area.localScale = fittedScale;
    }
}
