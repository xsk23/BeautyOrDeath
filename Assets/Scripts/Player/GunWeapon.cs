using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
public class GunWeapon : WeaponBase
{
    [Header("猎枪特有设置")]
    public float range = 100;// 射程
    public LineRenderer bulletTracer; // 弹道线
    public override void OnFire(Vector3 origin, Vector3 direction)
    {
        // 1. 设置冷却
        nextFireTime = Time.time + fireRate;

        // 3. 服务器进行射线检测
        if (isServer)
        {
            // 【关键】增加 LayerMask，假设玩家在第 3 层 "Player"
            // 这样射线会忽略掉发射者所在的层级（或者在代码里偏移起点）
            int layerMask = ~LayerMask.GetMask("Player"); 
            
            // 方案：起点稍微向前偏移 0.6米，跳出猎人自己的 CharacterController 范围
            Vector3 startPos = origin + direction * 0.6f;

            if (Physics.Raycast(startPos, direction, out RaycastHit hit, range))
            {
                // CharacterController 会被识别为 hit.collider
                // 我们尝试从命中物体或其父级寻找 GamePlayer
                GamePlayer target = hit.collider.GetComponent<GamePlayer>();
                
                if (target == null) 
                    target = hit.collider.GetComponentInParent<GamePlayer>();

                if (target != null)
                {
                    target.ServerTakeDamage(damage);
                    UnityEngine.Debug.Log($"[GunWeapon] Hit {target.playerName} on CC");
                }
            }
        }
    }

    // [ClientRpc]
    // void RpcShowTracer(Vector3 start, Vector3 end)
    // {
    //     if (bulletTracer)
    //     {
    //         StartCoroutine(ShowLine(start, end));
    //     }
    // }

    // System.Collections.IEnumerator ShowLine(Vector3 start, Vector3 end)
    // {
    //     bulletTracer.enabled = true;
    //     bulletTracer.SetPosition(0, start);
    //     bulletTracer.SetPosition(1, end);
    //     yield return new WaitForSeconds(0.05f);
    //     bulletTracer.enabled = false;
    // }
}
