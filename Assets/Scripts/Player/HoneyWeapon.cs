using UnityEngine;
using Mirror;

public class HoneyWeapon : WeaponBase
{
    [Header("贴花预制体")]
    public GameObject environmentDecalPrefab;
    public GameObject playerDecalPrefab;

    [Header("弹药设置")]
    public int maxAmmo = 120;
    [SyncVar] public int currentAmmo = 120;

    [Header("子弹设置")]
    public GameObject netPrefab;
    private float BulletSpeed = 35f;

    private void Awake()
    {
        weaponName = "HoneyGun";
        fireRate = 0.15f;
        damage = 1f;
        currentAmmo = maxAmmo;
    }

    public override void OnFire(Vector3 origin, Vector3 direction)
    {
        if (currentAmmo <= 0) return;

        if (isServer) currentAmmo--;

        nextFireTime = Time.time + fireRate;

        if (isServer)
        {
            GamePlayer player = GetComponentInParent<GamePlayer>();
            Vector3 referencePoint = player.transform.position + Vector3.up * 1.4f;

            Ray aimRay = new Ray(origin, direction);
            Vector3 targetPoint = (Physics.Raycast(aimRay, out RaycastHit aimHit, 100f, ~LayerMask.GetMask("Bullet", "Ignore Raycast")))
                                  ? aimHit.point : origin + direction * 100f;

            Vector3 spawnPos = referencePoint + (direction * 1.5f) + (Vector3.down * 0.6f);
            Vector3 fireDir = (targetPoint - spawnPos).normalized;

            GameObject net = Instantiate(netPrefab, spawnPos, Quaternion.LookRotation(fireDir));
            HoneyBullet bulletScript = net.GetComponent<HoneyBullet>();
            if (bulletScript != null)
            {
                bulletScript.launcherRoot = player.gameObject;
                bulletScript.ownerRole = player.playerRole;
                bulletScript.environmentDecalPrefab = environmentDecalPrefab;
                bulletScript.playerDecalPrefab = playerDecalPrefab;
            }

            Rigidbody rb = net.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = true;
                rb.velocity = fireDir * BulletSpeed;
            }

            NetworkServer.Spawn(net);
            Destroy(net, 3f);
        }
    }

    [Server]
    public void ServerRefill()
    {
        currentAmmo = maxAmmo;
    }
}