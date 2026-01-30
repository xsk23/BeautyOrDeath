using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class GunWeapon : WeaponBase
{
    [Header("猎枪特有设置")]
    public float range = 100;// 射程
    public GameObject impactEffectPrefab; // 命中特效预制体
    public override void OnFire(Vector3 origin, Vector3 direction)
    {

        // 1. 设置冷却
        nextFireTime = Time.time + fireRate;
        // 3. 服务器进行射线检测
        if (isServer)
        {
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
                    Debug.Log($"[GunWeapon] Hit {target.playerName} on CC");
                }
                // 2. ★ 触发命中特效
                // 传入命中点 (hit.point) 和 法线 (hit.normal)
                RpcSpawnImpact(hit.point, hit.normal);
            }
            [ClientRpc]
            void RpcSpawnImpact(Vector3 hitPoint, Vector3 surfaceNormal)
            {
                // 如果没有配特效，直接返回
                if (impactEffectPrefab == null) return;

                // 3. 生成特效
                // position: 命中点
                // rotation: 这里的 LookRotation(surfaceNormal) 会让特效的 Z 轴朝向墙面外侧
                GameObject effect = Instantiate(impactEffectPrefab, hitPoint, Quaternion.LookRotation(surfaceNormal));
                Destroy(effect, 2.0f);
            }

        }
    }
}
