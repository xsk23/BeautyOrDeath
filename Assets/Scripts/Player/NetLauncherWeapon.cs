using UnityEngine;
using Mirror;

public class NetLauncherWeapon : WeaponBase
{
    [Header("蜂蜜贴花设置")]
    public GameObject honeyOnObjectPrefab; 
    private float puddleLifeTime = 12f;     

    [Header("子弹设置")]
    public GameObject netPrefab; 
    private float BulletSpeed = 30f; 
    private float lifeTime = 5f; 

    private void Awake()
    {
        weaponName = "NetLauncher";
    }

  public override void OnFire(Vector3 origin, Vector3 direction)
    {
        nextFireTime = Time.time + fireRate;

        if (isServer)
        {
            GamePlayer player = GetComponentInParent<GamePlayer>();
            if (player == null) return;

            // --- 1. 确定“参考起点” (设为玩家约胸口/脖子的高度) ---
            Vector3 referencePoint = player.transform.position + Vector3.up * 1.4f;

            // --- 2. 确定“目标落点” (从相机投射射线，保证指哪打哪) ---
            Ray aimRay = new Ray(origin, direction);
            Vector3 targetPoint;
            int layerMask = ~LayerMask.GetMask("Bullet", "Ignore Raycast", "Player"); 
            
            if (Physics.Raycast(aimRay, out RaycastHit aimHit, 100f, layerMask))
            {
                targetPoint = aimHit.point;
            }
            else
            {
                targetPoint = origin + direction * 100f;
            }

            // --- 3. 【核心修改：确定生成起点】 ---
            // forwardOffset: 往前推的距离，防止撞到自己，也符合武器伸出去的长度
            // downwardOffset: 往下压的距离，让子弹看起来从腰部或手部位置射出
            float forwardOffset = 1.5f; 
            float downwardOffset = 0.6f; 

            Vector3 spawnPos = referencePoint + (direction * forwardOffset) + (Vector3.down * downwardOffset);

            // --- 4. 计算最终飞行方向 ---
            // 关键：方向必须是从这个“偏低”的起点指向“准星”的目标点
            Vector3 fireDir = (targetPoint - spawnPos).normalized;
            
            // 安全修正
            if (Vector3.Dot(fireDir, direction) < 0)
            {
                fireDir = direction;
            }

            // --- 5. 生成与初始化 ---
            GameObject net = Instantiate(netPrefab, spawnPos, Quaternion.LookRotation(fireDir));
            
            // ... 后续逻辑（NetBullet设置、Rigidbody速度设置、NetworkServer.Spawn等）保持不变
            NetBullet bulletScript = net.GetComponent<NetBullet>();
            if (bulletScript != null)
            {
                bulletScript.launcherRoot = player.gameObject; 
                bulletScript.ownerRole = player.playerRole;
                bulletScript.honeyPuddlePrefab = honeyOnObjectPrefab;
                bulletScript.puddleDuration = puddleLifeTime;
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