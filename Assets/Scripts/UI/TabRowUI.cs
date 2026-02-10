using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class TabRowUI : MonoBehaviour
{
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI playerRoleText;
    public TextMeshProUGUI playerPingText;
    [Header("Skill UI")]
    public Image skill1Image; // 拖入子物体 Skill1
    public Image skill2Image; // 拖入子物体 Skill2

    public void UpdateRow(GamePlayer player, List<SkillData> database)
    {
        // 更新名字
        playerNameText.text = player.playerName;
        
        // 更新角色 (根据阵营显示不同颜色)
        playerRoleText.text = player.playerRole.ToString();
        playerRoleText.color = player.playerRole == PlayerRole.Witch ? Color.magenta : Color.cyan;

        // 更新 Ping
        playerPingText.text = player.ping + "ms";
        // --- 【核心修改：设置技能图标】 ---
        SetSkillIcon(skill1Image, player.syncedSkill1Name, database);
        SetSkillIcon(skill2Image, player.syncedSkill2Name, database);
        // Ping 颜色反馈
        if (player.ping < 80) playerPingText.color = Color.green;
        else if (player.ping < 150) playerPingText.color = Color.yellow;
        else playerPingText.color = Color.red;

        // 如果玩家永久死亡，可以将整行变灰（可选）
        if (player.isPermanentDead)
        {
            playerNameText.text += " (Dead)";
            playerNameText.alpha = 0.5f;
        }
    }
    private void SetSkillIcon(Image targetImg, string className, List<SkillData> database)
    {
        if (targetImg == null) return;

        if (string.IsNullOrEmpty(className))
        {
            targetImg.gameObject.SetActive(false);
            return;
        }

        // 从数据库中查找匹配类名的 SkillData
        SkillData data = database.Find(d => d.scriptClassName == className);
        if (data != null && data.icon != null)
        {
            targetImg.sprite = data.icon;
            targetImg.gameObject.SetActive(true);
        }
        else
        {
            targetImg.gameObject.SetActive(false);
        }
    }
}