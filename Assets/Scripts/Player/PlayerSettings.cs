using UnityEngine;

using System.Collections.Generic;

public class PlayerSettings : MonoBehaviour
{
    public static PlayerSettings Instance { get; private set; }
    public string PlayerName { get; set; } = "";

    // 存储选中的技能名称（或者 ID）
    public List<string> selectedWitchSkillNames = new List<string>();
    public List<string> selectedHunterSkillNames = new List<string>();
    public string selectedWitchItemName = ""; // 存储选中的道具类名

    private void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // 可選：提供清除方法（斷線重連時用）
    public void Clear()
    {
        PlayerName = "Player";
    }
}