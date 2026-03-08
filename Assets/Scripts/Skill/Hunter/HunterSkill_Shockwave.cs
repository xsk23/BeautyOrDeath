using UnityEngine;
using Mirror;
using System.Collections.Generic;
//using System.Diagnostics;


public class HunterSkill_Shockwave : SkillBase
{
    public float radius = 8f;
    public GameObject vfxPrefab; // 震地特效

    public bool hitAnyWitch = false; // 是否命中至少一个女巫s
    protected override void OnCast()
    {
        hitAnyWitch = false;
        RpcPlayVFX();
        GameManager.Instance?.ServerPlay3DAt("shockwave砸地", ownerPlayer.transform.position);

        Collider[] hits = Physics.OverlapSphere(ownerPlayer.transform.position, radius);

        HashSet<WitchPlayer> affectedWitches = new HashSet<WitchPlayer>();

        Debug.Log($"<color=green>[Hunter] {ownerPlayer.playerName} used skill: Shockwave! Affected {hits.Length} targets.</color>");

        bool sentHitFeedback = false;
        foreach (var hit in hits)
        {
            // 找到女巫
            WitchPlayer witch = hit.GetComponent<WitchPlayer>() ?? hit.GetComponentInParent<WitchPlayer>();
            if (witch == null || affectedWitches.Contains(witch)) continue; // 没有找到女巫或者这个女巫已经被处理了，跳过
            affectedWitches.Add(witch);

            if (!witch.isPermanentDead)
            {
                // 1. 强制显形
                if (witch.isMorphed)
                {
                    witch.ServerForceRevert(); 
                }

                // 2. 减速 (0.4倍速即为 5f * 0.4 = 2f) 后面是持续时间
                witch.ServerApplySlow(0.4f, 3f);

                // 标记命中
                hitAnyWitch = true;

                if (hitAnyWitch && !sentHitFeedback)
                {
                    
                    // 如果 connectionToClient 为空（即 Host），则尝试使用 NetworkServer.localConnection
                    NetworkConnection targetConn = ownerPlayer.connectionToClient;

                    // 如果是 Host 模式，connectionToClient 可能为 null，需要特殊处理
                    if (targetConn == null && ownerPlayer.isLocalPlayer)
                    {
                        // 如果是 Host 自己释放技能，直接在本地打印日志或调用 UI，不走 RPC
                        AudioManager.Instance?.Play2D("叮");
                        Debug.Log("<color=yellow>[Host] Shockwave hit a witch!</color>");
                        // 你也可以直接调用本地 UI 函数，例如：
                        // SceneScript.Instance.ShowHitFeedback();
                        sentHitFeedback = true;
                    }
                    else if (targetConn != null)
                    {
                        // 如果是远程客户端，正常发送 TargetRpc
                        TargetHitFeedback(targetConn);
                        sentHitFeedback = true;
                    }
                }
            }
        }
    }

    [TargetRpc]
    void TargetHitFeedback(NetworkConnection conn)
    {
        AudioManager.Instance?.Play2D("叮");
        // UI 显示 "Hit!"
        Debug.Log("<color=yellow>[Hunter] Shockwave hit a witch!</color>");
    }

    [ClientRpc]
    void RpcPlayVFX()
    {
        if (vfxPrefab) Instantiate(vfxPrefab, transform.position, Quaternion.identity);
        else Debug.LogWarning("[HunterSkill_Shockwave] VFX Prefab is not assigned!");
    }

    // [Server]
    // System.Collections.IEnumerator SlowDownWitch(WitchPlayer witch)
    // {
    //     float originalSpeed = witch.moveSpeed;
    //     witch.moveSpeed /= 2f; 
    //     yield return new WaitForSeconds(1f);
    //     witch.moveSpeed = originalSpeed;
    // }
}