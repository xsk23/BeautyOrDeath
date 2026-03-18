using UnityEngine;
using Mirror;
using System.Collections;

public class WitchSkill_Curse : SkillBase
{
    [Header("Ghost Wallpass Settings (幽灵穿墙)")]
    public float ghostDuration = 4f;        // 持续时间
    public float stuckDamagePerSec = 10f;   // 卡在墙内每秒扣血量
    public float damageTickRate = 0.5f;     // 扣血检测频率（0.5秒扣5点）

    [Tooltip("可能卡住女巫的层级 (必须勾选 Environment, Wall, PropTree 等，切勿勾选 Ground！)")]
    public LayerMask obstacleLayers;

    private Coroutine activeGhostRoutine;

    private void Awake()
    {
        // 强制覆盖默认设定以匹配设计文档
        cooldownTime = 3f;
        skillName = "Ghost Wallpass";
    }

    protected override void OnCast()
    {
        WitchPlayer witch = ownerPlayer as WitchPlayer;
        if (witch == null || witch.isGhosted) return;

        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} entered Ghost State!</color>");
        
        // 借用现有的女巫迷雾音效作为进入幽灵态的提示
        GameManager.Instance?.ServerPlay3DAt("女巫迷雾", ownerPlayer.transform.position); 
        RpcPlayGhostEffect();

        if (activeGhostRoutine != null) StopCoroutine(activeGhostRoutine);
        activeGhostRoutine = StartCoroutine(GhostRoutine(witch));
    }

    [Server]
    private IEnumerator GhostRoutine(WitchPlayer witch)
    {
        witch.isGhosted = true; // 触发 SyncVar Hook 修改客户端 Layer

        float timer = 0f;
        // 如果在持续时间内被陷阱抓住(isTrappedByNet)，则提前强制退出幽灵态
        while (timer < ghostDuration && witch.isGhosted && !witch.isTrappedByNet)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        // 时间到，或被陷阱中断，恢复正常状态
        if (witch.isGhosted)
        {
            witch.isGhosted = false;
        }

        // 启动卡墙检测扣血 (如果被陷阱抓了且恰好在墙里，依然会扣血作为惩罚)
        StartCoroutine(StuckInWallCheckRoutine(witch));
    }

    [Server]
    private IEnumerator StuckInWallCheckRoutine(WitchPlayer witch)
    {
        CharacterController cc = witch.GetComponent<CharacterController>();
        if (cc == null) yield break;

        // 只要不是幽灵态，且没有彻底死亡/进入复活赛，就持续检测
        while (!witch.isGhosted && !witch.isPermanentDead && !witch.isInSecondChance)
        {
            // 构建与 CharacterController 大小一致的胶囊体检测
            Vector3 p1 = witch.transform.position + Vector3.up * cc.radius;
            Vector3 p2 = witch.transform.position + Vector3.up * (cc.height - cc.radius);

            // 如果与障碍物发生重叠 (注意 obstacleLayers 不应该包含 Ground)
            if (Physics.CheckCapsule(p1, p2, cc.radius * 0.9f, obstacleLayers))
            {
                Debug.Log($"<color=red>[Witch] {witch.playerName} stuck in wall! Taking damage.</color>");
                // 扣除伤害 (例如 10 * 0.5 = 5点伤害)
                witch.ServerTakeDamage(stuckDamagePerSec * damageTickRate);
                RpcPlayStuckEffect(witch.transform.position);
            }
            else
            {
                // 已脱离墙体，结束检测
                break;
            }

            yield return new WaitForSeconds(damageTickRate);
        }
    }

    [ClientRpc]
    private void RpcPlayGhostEffect()
    {
        // 此处可添加客户端视觉表现，比如播放粒子特效
        // 目前仅做日志提示
    }

    [ClientRpc]
    private void RpcPlayStuckEffect(Vector3 pos)
    {
        // 播放卡在墙里受伤的提示音效
        AudioManager.Instance?.Play3D("护符碎裂", pos); // 暂用现存音效代替
    }
}