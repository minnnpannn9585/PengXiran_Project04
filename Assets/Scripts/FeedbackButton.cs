using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Small hover/press motion and sounds shared by mouse, keyboard and tracked UI pointers.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public class FeedbackButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    [field: SerializeField] public bool Animate { get; set; } = true;
    [field: SerializeField] public bool PlayClickSound { get; set; } = true;
    [field: SerializeField] public bool PlayFocusSound { get; set; } = true;

    Button m_Button;
    Vector3 m_RestScale;
    bool m_Hovered;
    bool m_Selected;
    bool m_Pressed;
    float m_ClickUntil;

    void Awake()
    {
        m_Button = GetComponent<Button>();
        m_RestScale = transform.localScale;
    }

    void OnEnable()
    {
        m_Button.onClick.AddListener(Click);
    }

    void OnDisable()
    {
        m_Button.onClick.RemoveListener(Click);
        m_Hovered = m_Selected = m_Pressed = false;
        m_ClickUntil = 0f;
        transform.localScale = m_RestScale;
    }

    void Update()
    {
        if (!m_Button.IsInteractable())
            m_Hovered = m_Selected = m_Pressed = false;
        if (!Animate)
            return;

        float scale = m_Button.IsInteractable() && (m_Pressed || Time.unscaledTime < m_ClickUntil)
            ? 0.96f : m_Hovered || m_Selected ? 1.035f : 1f;
        transform.localScale = Vector3.Lerp(transform.localScale, m_RestScale * scale,
            1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        Focus();
        m_Hovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        m_Hovered = false;
        m_Pressed = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && m_Button.IsInteractable())
            m_Pressed = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
            m_Pressed = false;
    }

    public void OnSelect(BaseEventData eventData)
    {
        Focus();
        m_Selected = true;
    }

    public void OnDeselect(BaseEventData eventData)
    {
        m_Selected = false;
    }

    void Focus()
    {
        if (!m_Hovered && !m_Selected && m_Button.IsInteractable() && PlayFocusSound)
            InteractionFeedback.PlayUI(FeedbackCue.UIHover);
    }

    void Click()
    {
        if (!m_Button.IsInteractable())
            return;
        m_ClickUntil = Time.unscaledTime + 0.12f;
        if (PlayClickSound)
            InteractionFeedback.PlayUI(FeedbackCue.UIClick);
    }
}
