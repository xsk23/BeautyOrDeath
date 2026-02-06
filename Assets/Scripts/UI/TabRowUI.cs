using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TabRowUI : MonoBehaviour
{
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI playerRoleText;
    public TextMeshProUGUI playerPingText;
    public Transform skillContainer; // 暂时保留，后续扩展使用

    public void UpdateRow(GamePlayer player)
    {
        // 更新名字
        playerNameText.text = player.playerName;
        
        // 更新角色 (根据阵营显示不同颜色)
        playerRoleText.text = player.playerRole.ToString();
        playerRoleText.color = player.playerRole == PlayerRole.Witch ? Color.magenta : Color.cyan;

        // 更新 Ping
        playerPingText.text = player.ping + "ms";
        
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
}