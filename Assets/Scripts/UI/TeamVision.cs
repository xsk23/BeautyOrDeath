using UnityEngine;
using Mirror;
using System.Collections;

public class TeamVision : NetworkBehaviour
{
    [Header("阵营颜色")]
    public Color witchColor = Color.magenta;
    public Color hunterColor = Color.cyan;
    public Color enemyColor = Color.red; // 可选：敌人的颜色

    [Header("设置")]
    public float checkInterval = 0.5f; // 每0.5秒刷新一次，节省性能

    private GamePlayer localPlayer;

    public override void OnStartLocalPlayer()
    {
        localPlayer = GetComponent<GamePlayer>();
        StartCoroutine(VisionRoutine());
    }

    private IEnumerator VisionRoutine()
    {
        while (true)
        {
            UpdateAllOutlines();
            yield return new WaitForSeconds(checkInterval);
        }
    }

    private void UpdateAllOutlines()
    {
        if (localPlayer == null) return;

        // 遍历全局玩家列表 (需要在 GamePlayer 里维护这个静态列表)
        foreach (var targetPlayer in GamePlayer.AllPlayers)
        {
            if (targetPlayer == null || targetPlayer == localPlayer) continue;

            // 获取目标身上的视觉组件
            var outline = targetPlayer.GetComponent<PlayerOutline>();
            if (outline == null) continue;

            // --- 核心逻辑 ---
            
            // 1. 判断阵营
            bool isTeammate = (targetPlayer.playerRole == localPlayer.playerRole);
            bool isRoleAssigned = (localPlayer.playerRole != PlayerRole.None);

            // 2. 决定是否高亮
            // 规则：只要我有身份，我就能看到队友的轮廓
            if (isRoleAssigned && isTeammate)
            {
                Color c = (targetPlayer.playerRole == PlayerRole.Witch) ? witchColor : hunterColor;
                outline.SetOutline(true, c);
                
                // 队友显示名字 (绿色)
                if (targetPlayer.nameText != null)
                {
                    if(targetPlayer.playerRole == PlayerRole.Witch && targetPlayer.isMorphed)
                    {
                        targetPlayer.nameText.gameObject.SetActive(false); 
                    }
                    else
                    {
                        targetPlayer.nameText.gameObject.SetActive(true);
                        targetPlayer.nameText.color = Color.green;                        
                    }
                }
            }
            else
            {
                // 非队友：关闭轮廓 (或者你可以开启它并设为红色)
                outline.SetOutline(false, Color.white);

                // 敌人处理名字 (红色)
                if (targetPlayer.nameText != null)
                {
                    // 这里选择一直显示名字但标红，或者你可以 .SetActive(false) 隐藏
                    targetPlayer.nameText.gameObject.SetActive(false); 
                    // targetPlayer.nameText.color = Color.red;
                }
            }
        }
    }
}