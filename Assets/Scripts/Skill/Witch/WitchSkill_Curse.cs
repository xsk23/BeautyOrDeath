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
        cooldownTime = 25f;
        skillName = "Ghost Wallpass";
    }

    protected override void OnCast()
    {
        WitchPlayer witch = ownerPlayer as WitchPlayer;
        if (witch == null || witch.isGhosted) return;

        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} entered Ghost State!</color>");
        
        GameManager.Instance?.ServerPlay3DAt("女巫迷雾", ownerPlayer.transform.position); 
        RpcPlayGhostEffect();

        if (activeGhostRoutine != null) StopCoroutine(activeGhostRoutine);
        activeGhostRoutine = StartCoroutine(GhostRoutine(witch));
    }

    [Server]
    private IEnumerator GhostRoutine(WitchPlayer witch)
    {
        witch.isGhosted = true; 

        float timer = 0f;
        while (timer < ghostDuration && witch.isGhosted && !witch.isTrappedByNet)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        if (witch.isGhosted)
        {
            witch.isGhosted = false;
        }

        StartCoroutine(StuckInWallCheckRoutine(witch));
    }

    [Server]
    private IEnumerator StuckInWallCheckRoutine(WitchPlayer witch)
    {
        CharacterController cc = witch.GetComponent<CharacterController>();
        if (cc == null) yield break;

        // 1. 宽限期
        float timer = 0f;
        while (timer < gracePeriod)
        {
            if (witch.isPermanentDead || witch.isInSecondChance) yield break;
            timer += Time.deltaTime;
            yield return null;
        }

        // 2. 检测是否卡墙
        Vector3 p1 = witch.transform.position + Vector3.up * cc.radius;
        Vector3 p2 = witch.transform.position + Vector3.up * (cc.height - cc.radius);

        if (Physics.CheckCapsule(p1, p2, cc.radius * 0.9f, obstacleLayers))
        {
            Debug.Log($"<color=red>[Witch] {witch.playerName} failed to exit wall in time. Popping out and stunning!</color>");
            
            // 计算安全位置
            Vector3 safePos = FindSafePosition(witch.transform.position, cc);
            
            // 【核心修复】：通过 TargetRpc 命令拥有权威的客户端强行瞬移
            if (witch.connectionToClient != null)
            {
                TargetForceTeleport(witch.connectionToClient, safePos);
            }
            else
            {
                // 兜底：如果是 Host 自己
                cc.enabled = false;
                witch.transform.position = safePos;
                cc.enabled = true;
            }

            RpcPlayStuckEffect(safePos);
            StartCoroutine(ApplyStunPenalty(witch, stunDuration));
        }
    }

    // 【新增】专属定向 RPC，命令客户端强行改变位置
    [TargetRpc]
    private void TargetForceTeleport(NetworkConnection target, Vector3 safePos)
    {
        CharacterController cc = ownerPlayer.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        
        ownerPlayer.transform.position = safePos;
        
        if (cc != null) 
        {
            cc.enabled = true;
            cc.Move(Vector3.down * 0.01f); // 强制刷新物理状态
        }
        Debug.Log("[Client] Force teleported out of wall.");
    }

    private Vector3 FindSafePosition(Vector3 center, CharacterController cc)
    {
        for (float r = 1f; r <= searchRadius; r += 1f)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f;
                Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                Vector3 testPos = center + dir * r;

                Vector3 p1 = testPos + Vector3.up * cc.radius;
                Vector3 p2 = testPos + Vector3.up * (cc.height - cc.radius);

                if (!Physics.CheckCapsule(p1, p2, cc.radius * 0.9f, obstacleLayers))
                {
                    return testPos;
                }
            }
        }
        return center + Vector3.up * 5f; 
    }

    [Server]
    private IEnumerator ApplyStunPenalty(WitchPlayer witch, float duration)
    {
        witch.isStunned = true;
        yield return new WaitForSeconds(duration);
        if (!witch.isTrappedByNet) 
        {
            witch.isStunned = false;
        }
    }

    [ClientRpc]
    private void RpcPlayGhostEffect() { }

    [ClientRpc]
    private void RpcPlayStuckEffect(Vector3 pos)
    {
        AudioManager.Instance?.Play3D("护符碎裂", pos); 
        if (isLocalPlayer && CameraDrunkEffect.Instance != null)
        {
            CameraDrunkEffect.Instance.PlayDrunkEffect(stunDuration, 0.08f);
        }
    }
}