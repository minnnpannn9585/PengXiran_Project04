using UnityEngine;

/// <summary>
/// Spoken lines for one character. The dialogue system reads this asset; it does not contain interaction logic.
/// </summary>
[CreateAssetMenu(fileName = "Dialogue", menuName = "Dialogue/Dialogue Data")]
public class DialogueData : ScriptableObject
{
    [SerializeField] string m_SpeakerName = "Speaker";

    [TextArea(2, 4)]
    [SerializeField] string[] m_Lines = { };

    public string SpeakerName => string.IsNullOrEmpty(m_SpeakerName) ? "Speaker" : m_SpeakerName;

    public int LineCount => m_Lines == null ? 0 : m_Lines.Length;

    public string GetLine(int index)
    {
        return m_Lines[index];
    }
}
