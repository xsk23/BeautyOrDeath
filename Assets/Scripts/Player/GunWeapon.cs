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
            Vector3 startPos = origin + direction * 1.2f;

            if (Physics.Raycast(startPos, direction, out RaycastHit hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                // CharacterController 会被识别为 hit.collider
                // 【核心修复】使用 GetComponentInParent，因为 Collider 可能在模型子节点上
                GamePlayer target = hit.collider.GetComponentInParent<GamePlayer>();
                // 调试打印：看看由于打中了什么而没射中
                if (target == null) {
                    Debug.Log($"Shot hit object without Player script: {hit.collider.name} on Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
                }       
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
            else
            {
                Debug.Log("[Server] Raycast hit nothing.");
            }

        }
    }
    [ClientRpc]
    void RpcSpawnImpact(Vector3 hitPoint, Vector3 surfaceNormal)
    {
        // 如果没有配特效，在控制台发出警告并返回
        if (impactEffectPrefab == null) 
        {
            Debug.LogWarning("[GunWeapon] 警告：没有在 Inspector 中分配命中特效 (Impact Effect Prefab)！");
            return;
        }

        // 【核心修复】生成位置顺着法线向外偏移 0.02 米，防止被墙体吞没或发生 Z-Fighting 闪烁
        Vector3 spawnPos = hitPoint + surfaceNormal * 0.02f;
        
        // 生成特效，LookRotation 让特效的 Z 轴朝向墙面外侧
        GameObject effect = Instantiate(impactEffectPrefab, spawnPos, Quaternion.LookRotation(surfaceNormal));
        
        // 2秒后自动销毁
        Destroy(effect, 2.0f);
    }
}
