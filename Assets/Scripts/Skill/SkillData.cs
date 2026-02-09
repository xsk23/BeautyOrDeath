using UnityEngine;

[CreateAssetMenu(fileName = "New Skill", menuName = "Game/Skill Data")]
public class SkillData : ScriptableObject
{
    public string skillName;      // UI显示的名字
    public string scriptClassName; // 脚本的类名 (例如: "WitchSkill_Mist")
    public PlayerRole role;
    public Sprite icon;
    [TextArea] public string description;
}