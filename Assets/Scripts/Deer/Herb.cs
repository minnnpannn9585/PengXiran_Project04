using UnityEngine;

public enum HerbKind
{
    RedFlower,
    PurpleFlower
}

/// <summary>
/// Marks a carried plant as the red flower or the purple flower.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CarryItem))]
[AddComponentMenu("Deer/Herb")]
public class Herb : MonoBehaviour
{
    [SerializeField] HerbKind m_Kind = HerbKind.RedFlower;

    public HerbKind Kind => m_Kind;

    public bool IsOnTable { get; private set; }

    public void SetOnTable(bool onTable)
    {
        IsOnTable = onTable;
    }
}
