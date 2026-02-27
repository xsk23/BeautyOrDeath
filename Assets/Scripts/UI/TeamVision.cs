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

    // 【新增】标记当前是否正在强制显示猎人（防止逻辑竞争）
    private bool isEffectRevealingHunters = false;
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

    public IEnumerator TempShowEnemies(float duration)
    {
        isEffectRevealingHunters = true; // 开启强制显示标记
        
        // 立即刷新一次
        UpdateAllOutlines();

        yield return new WaitForSeconds(duration);

        isEffectRevealingHunters = false; // 关闭强制显示
        
        // 效果结束立即刷新，清除红色描边
        UpdateAllOutlines();
    }

    private void ForceShowHuntersOnce()
    {
        foreach (var p in GamePlayer.AllPlayers)
        {
            if (p != null && p.playerRole == PlayerRole.Hunter)
            {
                var outline = p.GetComponent<PlayerOutline>();
                if (outline) outline.SetOutline(true, Color.red); // 强制显示红色描边
            }
        }
    }
    // private void UpdateAllOutlines()
    // {
    //     if (localPlayer == null) return;
        
    //     foreach (var targetPlayer in GamePlayer.AllPlayers)
    //     {
    //         if (targetPlayer == null || targetPlayer == localPlayer) continue;

    //         var outline = targetPlayer.GetComponent<PlayerOutline>();
    //         if (outline == null) continue;

    //         // 获取同步变量
    //         bool isTrapped = targetPlayer.isTrappedByNet;
    //         bool IAmHunter = (localPlayer.playerRole == PlayerRole.Hunter);
    //         bool isTeammate = (targetPlayer.playerRole == localPlayer.playerRole);
            

    //         // --- 核心逻辑优先级：被抓状态高于一切 ---
    //         if (isTrapped)
    //         {
    //             // 只要被抓了，不管是猎人看她，还是女巫队友看她，全部显示红色
    //             // 这样队友也能意识到“糟糕，她被抓了，需要掩护/解救”
    //             outline.SetOutline(true, Color.red);
    //             // if (targetPlayer.nameText != null) targetPlayer.nameText.gameObject.SetActive(false);
    //             continue; 
    //         }

    //         // --- 正常的队友显示逻辑 ---
    //         if (localPlayer.playerRole != PlayerRole.None && isTeammate)
    //         {
    //             Color c = (targetPlayer.playerRole == PlayerRole.Witch) ? witchColor : hunterColor;
    //             outline.SetOutline(true, c);
                
    //             if (targetPlayer.nameText != null)
    //             {
    //                 bool shouldShowName = !(targetPlayer is WitchPlayer w && w.isMorphed);
    //                 targetPlayer.nameText.gameObject.SetActive(shouldShowName);
    //                 targetPlayer.nameText.color = Color.green;
    //             }
    //         }
    //         // --- 正常的敌对显示逻辑 ---
    //         else
    //         {
    //             outline.SetOutline(false, Color.white);
    //             if (targetPlayer.nameText != null) targetPlayer.nameText.gameObject.SetActive(false);
    //         }
    //     }
    //     // 2. --- 处理已发现树木的常驻高亮 ---
    //     if (localPlayer.playerRole == PlayerRole.Witch)
    //     {
    //         PropTarget[] allProps = Object.FindObjectsOfType<PropTarget>();
    //         foreach (var prop in allProps)
    //         {
    //             if (prop == null) continue;

    //             // 如果是被发现的静态树，强制开启高亮渲染。
    //             // SetHighlight(false) 传入 false 是因为此时准星没指着它，
    //             // 但内部逻辑会因为 isScouted 为 true 而决定继续显示高亮。
    //             // 判定逻辑里应包含临时状态 (由于 PropTarget.SetHighlight 已经改了，这里只需确保调用)
    //             if ((prop.isScouted || prop.isLocalTempRevealed) && (prop.isStaticTree || prop.isAncientTree))
    //             {
    //                 prop.SetHighlight(false); 
    //             }
    //         }
    //     }
    // }

    private void UpdateAllOutlines()
    {
        if (localPlayer == null) return;
        // --- 【新增：胜利区域清理逻辑】 ---
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            // 1. 清理所有玩家的描边
            foreach (var p in GamePlayer.AllPlayers)
            {
                if (p != null)
                {
                    var outline = p.GetComponent<PlayerOutline>();
                    if (outline != null) outline.SetOutline(false, Color.clear);
                    if (p.nameText != null) p.nameText.gameObject.SetActive(false);
                }
            }
            // 2. 清理场景中所有道具/树木的高亮
            PropTarget[] allProps = Object.FindObjectsOfType<PropTarget>();
            foreach (var prop in allProps)
            {
                if (prop != null) prop.SetHighlight(false);
            }
            return; // 直接跳出，不再执行后续的高亮逻辑
        }
        // ---------------------------------

        // 1. 处理玩家描边
        foreach (var targetPlayer in GamePlayer.AllPlayers)
        {
            if (targetPlayer == null || targetPlayer == localPlayer) continue;
            // 关键补丁：如果角色还没同步好(None)，跳过本次循环，等下一秒再试
            if (targetPlayer.playerRole == PlayerRole.None) continue;
            var outline = targetPlayer.GetComponent<PlayerOutline>();
            if (outline == null) continue;

            bool isTrapped = targetPlayer.isTrappedByNet;
            bool isTeammate = (targetPlayer.playerRole == localPlayer.playerRole);
            bool isTargetHunter = (targetPlayer.playerRole == PlayerRole.Hunter);

            // --- 优先级逻辑 ---
            if (isTrapped)
            {
                outline.SetOutline(true, Color.red);
            }
            // 【核心修复】如果是猎人且正处于“奖励透视期”
            else if (isTargetHunter && isEffectRevealingHunters)
            {
                outline.SetOutline(true, Color.red);
            }
            else if (isTeammate)
            {
                // 队友：显示名字（如果是女巫且变身中则隐藏）
                if (targetPlayer.nameText != null)
                {
                    bool shouldShowName = !(targetPlayer is WitchPlayer w && w.isMorphed);
                    targetPlayer.nameText.gameObject.SetActive(shouldShowName);
                }
                Color c = (targetPlayer.playerRole == PlayerRole.Witch) ? witchColor : hunterColor;
                outline.SetOutline(true, c);
                // 即使脚本被别人关了，这里也强行打开
                if (!outline.enabled) outline.enabled = true; 
                outline.SetOutline(true, c);
            }
            else
            {
                // 敌人：强制隐藏名字
                if (targetPlayer.nameText != null) 
                {
                    targetPlayer.nameText.gameObject.SetActive(false);
                }
                // 正常敌对状态（非透视期且未被抓），关闭描边
                outline.SetOutline(false, Color.white);
            }
        }

        // 2. 处理树木描边
        if (localPlayer.playerRole == PlayerRole.Witch)
        {
            PropTarget[] allProps = Object.FindObjectsOfType<PropTarget>();
            foreach (var prop in allProps)
            {
                if (prop == null || (!prop.isStaticTree && !prop.isAncientTree)) continue;

                // 【核心修复】始终调用 SetHighlight，让内部去根据最新状态决定是否显示
                // 内部条件 active || isScouted || isLocalTempRevealed 只要全为 false，高亮就会消失
                prop.SetHighlight(false); 
            }
        }
    }

    public void ForceUpdateVisuals()
    {
        UpdateAllOutlines();
    }

}