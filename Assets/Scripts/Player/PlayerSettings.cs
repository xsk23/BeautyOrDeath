using UnityEngine;

using System.Collections.Generic;

public enum Gender { Male, Female }

public class PlayerSettings : MonoBehaviour
{
    public static PlayerSettings Instance { get; private set; }
    public string PlayerName { get; set; } = "";

    public Gender selectedGender = Gender.Male; // 默认男性
    // 存储选中的技能名称（或者 ID）
    public List<string> selectedWitchSkillNames = new List<string>();
    public List<string> selectedHunterSkillNames = new List<string>();
    public string selectedWitchItemName = ""; // 存储选中的道具类名

    private void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (selectedWitchSkillNames.Count < 2) {
            selectedWitchSkillNames.Clear();
            selectedWitchSkillNames.Add("WitchSkill_Mist"); // 默认值
            selectedWitchSkillNames.Add("WitchSkill_Decoy");
        }
        if (selectedHunterSkillNames.Count < 2) {
            selectedHunterSkillNames.Clear();
            selectedHunterSkillNames.Add("HunterSkill_Trap");
            selectedHunterSkillNames.Add("HunterSkill_Scan");
        }
        // 核心修改：在 Awake 阶段就锁定默认值，不要等 UI 脚本初始化
        if (string.IsNullOrEmpty(selectedWitchItemName)) {
            selectedWitchItemName = "InvisibilityCloak"; // 或者你想要的默认类名
        }
        DontDestroyOnLoad(gameObject);
    }

    // 可選：提供清除方法（斷線重連時用）
    public void Clear()
    {
        PlayerName = "Player";
    }
    // 供 UI 调用
    public void SetGender(int index)
    {
        selectedGender = (Gender)index;
    }
}