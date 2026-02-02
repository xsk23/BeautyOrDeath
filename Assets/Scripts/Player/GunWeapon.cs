using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class GunWeapon : WeaponBase
{
    [Header("猎枪特有设置")]
    public float range = 100;// 射程
    public GameObject impactEffectPrefab; // 命中特效预制体
    private void Awake()
    {
        weaponName = "Gun";
    }
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
                // 【核心修复】使用 GetComponentInParent，因为 Collider 可能在模型子节点上
                GamePlayer target = hit.collider.GetComponentInParent<GamePlayer>();

                if (target != null)
                {
                    // 获取攻击者（枪是在猎人手里的，所以父级一定是 HunterPlayer）
                    GamePlayer attacker = GetComponentInParent<GamePlayer>();
                    if (target == attacker) return;
                    // --- 【队友伤害检查逻辑】 ---
                    bool isSameTeam = (target.playerRole == attacker.playerRole);
                    bool canDamage = !isSameTeam || GameManager.Instance.FriendlyFire;

                    if (canDamage)
                    {
                        target.ServerTakeDamage(damage);
                        Debug.Log($"[GunWeapon] {attacker.playerName} shot {target.playerName}. FF: {isSameTeam}");
                    }
                    else
                    {
                        Debug.Log($"[GunWeapon] Hit blocked by Friendly Fire setting!");
                    }
                }
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
