using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class FistWeapon : WeaponBase
{
    [Header("近战特有设置")]
    public float attackRadius = 0.5f; // 拳头的判定半径
    public float attackDistance = 2.0f; // 攻击距离
    public float stunDuration = 0.5f; // 眩晕时间

    private void Awake()
    {
        // 初始化默认值
        if (damage == 0) damage = 10f;
        if (fireRate == 1.0f) fireRate = 0.4f;
        weaponName = "Fist";
    }

    public override void OnFire(Vector3 origin, Vector3 direction)
    {
        nextFireTime = Time.time + fireRate;
        if (isServer)
        {
            if (Physics.SphereCast(origin, attackRadius, direction, out RaycastHit hit, attackDistance))
            {
                GamePlayer target = hit.collider.GetComponent<GamePlayer>();
                if (target == null)
                    target = hit.collider.GetComponentInParent<GamePlayer>();

                if (target != null)
                {
                    // 造成伤害
                    target.ServerTakeDamage(damage);
                    if (target is WitchPlayer)
                    {
                        StartCoroutine(ApplyMicroStun(target));
                    }

                    Debug.Log($"[Fist] Punched {target.playerName}!");
                }
            }
        }
    }

    // 服务器端协程：短暂眩晕
    [Server]
    private IEnumerator ApplyMicroStun(GamePlayer target)
    {
        if (!target.isStunned)
        {
            target.isStunned = true;
            yield return new WaitForSeconds(stunDuration);
            target.isStunned = false;
        }
    }
}