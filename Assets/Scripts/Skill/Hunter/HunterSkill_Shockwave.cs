using UnityEngine;
using Mirror;
//using System.Diagnostics;


public class HunterSkill_Shockwave : SkillBase
{
    public float radius = 8f;
    public GameObject vfxPrefab; // 震地特效

    public bool hitAnyWitch = false; // 是否命中至少一个女巫s
    protected override void OnCast()
    {
        RpcPlayVFX();

        Collider[] hits = Physics.OverlapSphere(ownerPlayer.transform.position, radius);
        Debug.Log($"<color=green>[Hunter] {ownerPlayer.playerName} used skill: Shockwave! Affected {hits.Length} targets.</color>");
        foreach (var hit in hits)
        {
            // 找到女巫
            WitchPlayer witch = hit.GetComponent<WitchPlayer>();
            if (witch == null) {
                continue;
            }
            else
            {
                Debug.Log($"[Hunter] Found witch: {witch.playerName}");
            }

            if (!witch.isPermanentDead)
            {
                // 1. 强制显形
                if (witch.isMorphed)
                {
                    // 调用女巫现有的 Revert 命令逻辑
                    // 由于这是服务器端，我们不能调用 Cmd，需要把 CmdRevert 的逻辑拆分出一个 ServerRevert
                    // 或者我们这里简单暴力点，直接修改变量并调用 ApplyRevert
                    witch.isMorphed = false;
                    witch.morphedPropID = -1;
                    // 通过 Rpc 通知女巫客户端 (WitchPlayer 需要对应修改 OnMorphedPropIDChanged 钩子来处理逻辑)
                    // 目前代码里 OnMorphedPropIDChanged 已经处理了 ApplyRevert
                }

                // 2. 减速 (需要给 GamePlayer 加个 StatusEffect 系统，这里简化直接改速度，3秒后改回)
                StartCoroutine(SlowDownWitch(witch));

                // 标记命中
                hitAnyWitch = true;

                if (hitAnyWitch)
                {
                    // 【核心修复】获取安全的连接对象
                    // 如果 connectionToClient 为空（即 Host），则尝试使用 NetworkServer.localConnection
                    NetworkConnection targetConn = ownerPlayer.connectionToClient;
                    
                    // 如果是 Host 模式，connectionToClient 可能为 null，需要特殊处理
                    if (targetConn == null && ownerPlayer.isLocalPlayer)
                    {
                        // 如果是 Host 自己释放技能，直接在本地打印日志或调用 UI，不走 RPC
                        Debug.Log("<color=yellow>[Host] Shockwave hit a witch!</color>");
                        // 你也可以直接调用本地 UI 函数，例如：
                        // SceneScript.Instance.ShowHitFeedback(); 
                    }
                    else if (targetConn != null)
                    {
                        // 如果是远程客户端，正常发送 TargetRpc
                        TargetHitFeedback(targetConn);
                    }
                }
            }
        }
    }

    [TargetRpc]
    void TargetHitFeedback(NetworkConnection conn)
    {
        // UI 显示 "Hit!"
        Debug.Log("<color=yellow>[Hunter] Shockwave hit a witch!</color>");
    }

    [ClientRpc]
    void RpcPlayVFX()
    {
        if (vfxPrefab) Instantiate(vfxPrefab, ownerPlayer.transform.position, Quaternion.identity);
        else Debug.LogWarning("[HunterSkill_Shockwave] VFX Prefab is not assigned!");
    }

    [Server]
    System.Collections.IEnumerator SlowDownWitch(WitchPlayer witch)
    {
        float originalSpeed = witch.moveSpeed;
        witch.moveSpeed = 2f; // 极慢
        yield return new WaitForSeconds(3f);
        witch.moveSpeed = originalSpeed;
    }
}