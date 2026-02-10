using UnityEngine;

[CreateAssetMenu(fileName = "New Witch Item", menuName = "Game/Witch Item Data")]
public class WitchItemData : ScriptableObject
{
    public string itemName;       // UI显示的道具名
    public string scriptClassName; // 对应的类名 (如 "InvisibilityCloak")
    public Sprite icon;           // 道具图片
    [TextArea] public string description; // 道具描述
}