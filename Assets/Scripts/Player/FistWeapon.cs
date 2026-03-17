using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class FistWeapon : WeaponBase
{
    [Header("近战特有设置")]
    public float attackDistance = 3.0f; // 攻击距离

    [Range(0, 180)]
    public float attackAngle = 90f;     // 攻击扇形角度（面前90度）
    public float stunDuration = 0.5f; // 眩晕时间
    // 缓存引用
    private HunterPlayer ownerHunter;
    private void Awake()
    {
        // 初始化默认值
        damage = 15f;
        fireRate = 0.5f;
        weaponName = "Fist";
    }

    public override void OnFire(Vector3 origin, Vector3 direction)
    {   
        // 1. 获取所有者引用（用于判断自身朝向）
        if (ownerHunter == null) ownerHunter = GetComponentInParent<HunterPlayer>();
        
        // 播放音效（本地/所有客户端由 HunterPlayer 控制，这里仅处理逻辑）
        // 注意：原代码逻辑在 HunterPlayer 中有 RpcFireEffect 处理音效
        
        nextFireTime = Time.time + fireRate;

        if (isServer)
        {
            PerformMeleeScan();
        }
    }
    [Server]
    private void PerformMeleeScan()
    {
        // 1. 在猎人周围找出所有碰撞体
        // 使用猎人脚底或中心作为圆心，而不是摄像机
        Vector3 scanCenter = ownerHunter.transform.position + Vector3.up * 1.0f;
        Collider[] hits = Physics.OverlapSphere(scanCenter, attackDistance);

        GamePlayer bestTarget = null;
        float minAngle = float.MaxValue;

        foreach (var hit in hits)
        {
            // 2. 排除自己
            if (hit.gameObject == ownerHunter.gameObject) continue;

            // 3. 获取玩家组件
            GamePlayer target = hit.GetComponent<GamePlayer>() ?? hit.GetComponentInParent<GamePlayer>();
            if (target == null || target.isPermanentDead) continue;

            // 4. 扇形角度判断
            Vector3 dirToTarget = (target.transform.position - ownerHunter.transform.position).normalized;
            dirToTarget.y = 0; // 忽略高度差带来的角度偏移，只看平面朝向
            
            Vector3 hunterForward = ownerHunter.transform.forward;
            hunterForward.y = 0;

            float angle = Vector3.Angle(hunterForward, dirToTarget);

            // 如果在扇形范围内
            if (angle <= attackAngle / 2f)
            {
                // 队友伤害检查 (复用 GunWeapon 的逻辑)
                bool isSameTeam = (target.playerRole == ownerHunter.playerRole);
                bool canDamage = !isSameTeam || GameManager.Instance.FriendlyFire;

                if (canDamage)
                {
                    // 为了手感，我们通常只打击范围内最接近准星/正前方的那个
                    if (angle < minAngle)
                    {
                        minAngle = angle;
                        bestTarget = target;
                    }
                }
            }
        }

        // 5. 对最终选定的目标造成伤害
        if (bestTarget != null)
        {
            bestTarget.ServerTakeDamage(damage);
            if (bestTarget is WitchPlayer)
            {
                StartCoroutine(ApplyMicroStun(bestTarget));
            }
            Debug.Log($"[Fist] Melee Hit: {bestTarget.playerName} (Angle: {minAngle})");
        }
    }

    [Server]
    private IEnumerator ApplyMicroStun(GamePlayer target)
    {
        if (!target.isStunned)
        {
            target.isStunned = true;
            yield return new WaitForSeconds(stunDuration);
            // 只有当玩家没有被陷阱抓到时才解除眩晕（防止拳头解除陷阱禁锢）
            if (!target.isTrappedByNet)
                target.isStunned = false;
        }
    }

    // 调试绘图：在编辑器里看扇形范围
    private void OnDrawGizmosSelected()
    {
        if (ownerHunter == null) ownerHunter = GetComponentInParent<HunterPlayer>();
        if (ownerHunter == null) return;

        Gizmos.color = Color.red;
        Vector3 pos = ownerHunter.transform.position + Vector3.up * 1.0f;
        Gizmos.DrawWireSphere(pos, attackDistance);

        Vector3 leftBoundary = Quaternion.Euler(0, -attackAngle / 2f, 0) * ownerHunter.transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0, attackAngle / 2f, 0) * ownerHunter.transform.forward;

        Gizmos.DrawLine(pos, pos + leftBoundary * attackDistance);
        Gizmos.DrawLine(pos, pos + rightBoundary * attackDistance);
    }
}