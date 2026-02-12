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
        // --- 修复：本地玩家不应该看到自己的名字标签 ---
        if (localPlayer.nameText != null)
        {
            localPlayer.nameText.gameObject.SetActive(false);
        }
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
        
        foreach (var targetPlayer in GamePlayer.AllPlayers)
        {
            if (targetPlayer == null || targetPlayer == localPlayer) continue;

            var outline = targetPlayer.GetComponent<PlayerOutline>();
            if (outline == null) continue;

            // 获取同步变量
            bool isTrapped = targetPlayer.isTrappedByNet;
            bool IAmHunter = (localPlayer.playerRole == PlayerRole.Hunter);
            bool isTeammate = (targetPlayer.playerRole == localPlayer.playerRole);

            // --- 核心逻辑优先级：被抓状态高于一切 ---
            if (isTrapped)
            {
                // 只要被抓了，不管是猎人看她，还是女巫队友看她，全部显示红色
                // 这样队友也能意识到“糟糕，她被抓了，需要掩护/解救”
                outline.SetOutline(true, Color.red);
                // if (targetPlayer.nameText != null) targetPlayer.nameText.gameObject.SetActive(false);
                continue; 
            }

            // --- 正常的队友显示逻辑 ---
            if (localPlayer.playerRole != PlayerRole.None && isTeammate)
            {
                Color c = (targetPlayer.playerRole == PlayerRole.Witch) ? witchColor : hunterColor;
                outline.SetOutline(true, c);
                
                if (targetPlayer.nameText != null)
                {
                    bool shouldShowName = !(targetPlayer is WitchPlayer w && w.isMorphed);
                    targetPlayer.nameText.gameObject.SetActive(shouldShowName);
                    targetPlayer.nameText.color = Color.green;
                }
            }
            // --- 正常的敌对显示逻辑 ---
            else
            {
                outline.SetOutline(false, Color.white);
                if (targetPlayer.nameText != null) targetPlayer.nameText.gameObject.SetActive(false);
            }
        }
    }

    public void ForceUpdateVisuals()
    {
        UpdateAllOutlines();
    }

}