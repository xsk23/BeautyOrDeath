using UnityEngine;
using Mirror;

public class HoneyWeapon : WeaponBase
{
    [Header("贴花预制体")]
    public GameObject environmentDecalPrefab; // 仅渲染环境层
    public GameObject playerDecalPrefab;      // 仅渲染玩家层

    [Header("子弹设置")]
    public GameObject netPrefab;
    private float BulletSpeed = 35f;
    private float lifeTime = 3f;

    private void Awake()
    {
        weaponName = "HoneyGun";
        fireRate = 0.15f;
        damage = 1f;
    }

    public override void OnFire(Vector3 origin, Vector3 direction)
    {
        nextFireTime = Time.time + fireRate;

        if (isServer)
        {
            GamePlayer player = GetComponentInParent<GamePlayer>();
            if (player == null) return;

            // 1. 确定发射点
            Vector3 referencePoint = player.transform.position + Vector3.up * 1.4f;

            // 2. 预测落点用于旋转子弹
            Ray aimRay = new Ray(origin, direction);
            Vector3 targetPoint;
            int layerMask = ~LayerMask.GetMask("Bullet", "Ignore Raycast");

            if (Physics.Raycast(aimRay, out RaycastHit aimHit, 100f, layerMask))
                targetPoint = aimHit.point;
            else
                targetPoint = origin + direction * 100f;

            float forwardOffset = 1.2f;
            float downwardOffset = 0.4f;
            Vector3 spawnPos = referencePoint + (direction * forwardOffset) + (Vector3.down * downwardOffset);
            Vector3 fireDir = (targetPoint - spawnPos).normalized;

            if (Vector3.Dot(fireDir, direction) < 0) fireDir = direction;

            // 3. 生成子弹
            GameObject net = Instantiate(netPrefab, spawnPos, Quaternion.LookRotation(fireDir));

            HoneyBullet bulletScript = net.GetComponent<HoneyBullet>();
            if (bulletScript != null)
            {
                bulletScript.launcherRoot = player.gameObject;
                bulletScript.ownerRole = player.playerRole;
                // 【核心修改】传递两套贴花引用
                bulletScript.environmentDecalPrefab = environmentDecalPrefab;
                bulletScript.playerDecalPrefab = playerDecalPrefab;
            }

            Rigidbody rb = net.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = true;
                rb.velocity = fireDir * BulletSpeed;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            }

            NetworkServer.Spawn(net);
            Destroy(net, lifeTime);
        }
    }
}