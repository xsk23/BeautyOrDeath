using UnityEngine;
using Mirror;
using System.Collections;

public class WitchSkill_Curse : SkillBase
{
    [Header("Ghost Wallpass Settings (幽灵穿墙)")]
    public float ghostDuration = 4f;        // 幽灵态持续时间
    public float gracePeriod = 1.0f;        // 技能结束后的宽限期（秒），让玩家自己走出来
    public float stunDuration = 2.0f;       // 如果没走出来，被强行挤出后的眩晕惩罚时间
    public float searchRadius = 5.0f;       // 寻找最近安全点的最大半径

    [Tooltip("可能卡住女巫的层级 (在 Inspector 中勾选 Default 和 Prop)")]
    public LayerMask obstacleLayers;

    private Coroutine activeGhostRoutine;

    private void Awake()
    {
        
        cooldownTime = 3f; // 冷却时间
        skillName = "Ghost Wallpass";
    }

    protected override void OnCast()
    {
        WitchPlayer witch = ownerPlayer as WitchPlayer;
        if (witch == null || witch.isGhosted) return;

        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} entered Ghost State!</color>");
        
        // 借用现有的音效作为进入幽灵态的提示
        GameManager.Instance?.ServerPlay3DAt("女巫迷雾", ownerPlayer.transform.position); 
        RpcPlayGhostEffect();

        if (activeGhostRoutine != null) StopCoroutine(activeGhostRoutine);
        activeGhostRoutine = StartCoroutine(GhostRoutine(witch));
    }

    [Server]
    private IEnumerator GhostRoutine(WitchPlayer witch)
    {
        witch.isGhosted = true; // 触发 SyncVar Hook 修改所有客户端的 Layer

        float timer = 0f;
        // 持续时间内，如果被陷阱抓住 (isTrappedByNet)，则强制提前退出幽灵态
        while (timer < ghostDuration && witch.isGhosted && !witch.isTrappedByNet)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        // 恢复正常状态
        if (witch.isGhosted)
        {
            witch.isGhosted = false;
        }

        // 启动卡墙检测：宽限期 + 惩罚
        StartCoroutine(StuckInWallCheckRoutine(witch));
    }

    [Server]
    private IEnumerator StuckInWallCheckRoutine(WitchPlayer witch)
    {
        CharacterController cc = witch.GetComponent<CharacterController>();
        if (cc == null) yield break;

        // 1. 给予 1 秒的宽限期
        // 在这1秒内，女巫的 layer 已经恢复正常，但如果她还在墙里，Unity 物理引擎会尝试自动把她挤出去。
        float timer = 0f;
        while (timer < gracePeriod)
        {
            // 如果期间女巫死了，或者进入了复活赛，直接终止检测
            if (witch.isPermanentDead || witch.isInSecondChance) yield break;
            
            timer += Time.deltaTime;
            yield return null;
        }

        // 2. 宽限期结束，进行最终检测：她是不是还卡在障碍物里？
        Vector3 p1 = witch.transform.position + Vector3.up * cc.radius;
        Vector3 p2 = witch.transform.position + Vector3.up * (cc.height - cc.radius);

        if (Physics.CheckCapsule(p1, p2, cc.radius * 0.9f, obstacleLayers))
        {
            Debug.Log($"<color=red>[Witch] {witch.playerName} failed to exit wall in time. Popping out and stunning!</color>");
            
            // 3. 寻找附近的安全位置并强制瞬移过去
            Vector3 safePos = FindSafePosition(witch.transform.position, cc);
            
            // 必须先关闭 CC 才能强行修改位置
            cc.enabled = false;
            witch.transform.position = safePos;
            cc.enabled = true;

            // 强制同步物理状态
            cc.Move(Vector3.down * 0.01f);

            // 4. 施加负面惩罚：眩晕
            RpcPlayStuckEffect(safePos);
            StartCoroutine(ApplyStunPenalty(witch, stunDuration));
        }
    }

    // 辅助方法：环形向外探测寻找安全出生点
    private Vector3 FindSafePosition(Vector3 center, CharacterController cc)
    {
        // 步长 1 米，最大探测 searchRadius (5米)
        for (float r = 1f; r <= searchRadius; r += 1f)
        {
            // 8 个方向探测 (0, 45, 90, 135...)
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                Vector3 testPos = center + dir * r;

                // 探测该点是否会卡住
                Vector3 p1 = testPos + Vector3.up * cc.radius;
                Vector3 p2 = testPos + Vector3.up * (cc.height - cc.radius);

                if (!Physics.CheckCapsule(p1, p2, cc.radius * 0.9f, obstacleLayers))
                {
                    return testPos; // 找到安全点
                }
            }
        }

        // 如果周围 5 米全都是死路（极端情况），就把她往上弹飞
        return center + Vector3.up * 5f; 
    }

    [Server]
    private IEnumerator ApplyStunPenalty(WitchPlayer witch, float duration)
    {
        // 开启眩晕（会禁止玩家移动和跳跃）
        witch.isStunned = true;
        
        yield return new WaitForSeconds(duration);
        
        // 恢复眩晕。注意：如果这期间她被猎人的兜网抓住了，不能帮她解开眩晕
        if (!witch.isTrappedByNet) 
        {
            witch.isStunned = false;
        }
    }

    [ClientRpc]
    private void RpcPlayGhostEffect()
    {
        // 此处可添加客户端视觉表现，比如进入幽灵态的屏幕滤镜
    }

    [ClientRpc]
    private void RpcPlayStuckEffect(Vector3 pos)
    {
        // 播放卡在墙里被弹出来的提示音效 (复用护符碎裂或受到伤害的音效)
        AudioManager.Instance?.Play3D("护符碎裂", pos); 
        // 【新增】只有本地玩家触发屏幕眩晕 (2秒持续时间，0.08强度)
        if (isLocalPlayer && CameraDrunkEffect.Instance != null)
        {
            CameraDrunkEffect.Instance.PlayDrunkEffect(stunDuration, 0.05f);
        }
    }
}