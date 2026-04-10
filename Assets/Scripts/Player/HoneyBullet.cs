using UnityEngine;
using Mirror;

public class HoneyBullet : MonoBehaviour
{
    [HideInInspector] public GameObject launcherRoot;
    [HideInInspector] public PlayerRole ownerRole;
    [HideInInspector] public GameObject environmentDecalPrefab;
    [HideInInspector] public GameObject playerDecalPrefab;

    public float decalDuration = 8f;
    private bool hasHit = false;

    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        if (hasHit) return;

        // 1. 忽略发射者及其子物体
        if (launcherRoot != null && (other.gameObject == launcherRoot || other.transform.IsChildOf(launcherRoot.transform)))
            return;

        // 2. 尝试获取玩家组件（处理 CharacterController 碰撞）
        GamePlayer target = other.GetComponent<GamePlayer>() ?? other.GetComponentInParent<GamePlayer>();

        if (target != null)
        {
            if (target.playerRole != ownerRole || GameManager.Instance.FriendlyFire)
            {
                if (target is WitchPlayer witch)
                {
                    hasHit = true;
                    HoneyAccumulation acc = witch.GetComponent<HoneyAccumulation>();
                    if (acc != null)
                    {
                        bool canSpawn = !acc.hasVisibleDecal;
                        acc.ServerAddHoney(12f, decalDuration);
                        GameManager.Instance?.ServerPlay3DAt("honey_impact", transform.position);
                        if (canSpawn) SpawnDecalAttachedToPlayer(witch);
                    }
                }
                Destroy(gameObject);
            }
            return;
        }

        // 3. 命中环境逻辑
        if (!other.isTrigger)
        {
            hasHit = true;
            GameManager.Instance?.ServerPlay3DAt("honey_impact", transform.position);
            SpawnDecalOnEnvironment();
            Destroy(gameObject);
        }
    }

    // =================================================================
    // 【核心修复】修改贴花的生成方式与同步逻辑
    // =================================================================
    [Server]
    private void SpawnDecalAttachedToPlayer(WitchPlayer witch)
    {
        if (playerDecalPrefab == null) return;

        // 1. 首先在世界坐标系下实例化它（为了能正确的被 Spawn 广播）
        GameObject decal = Instantiate(playerDecalPrefab, witch.transform.position, Quaternion.Euler(90f, 0f, 0f));

        // 2. 将物体广播到所有的客户端
        NetworkServer.Spawn(decal);

        // 3. 获取刚刚生成的贴花网路标识
        NetworkIdentity decalNetId = decal.GetComponent<NetworkIdentity>();

        // 4. 让被命中的女巫组件发起 RPC：告诉所有客户端，“把那个贴花拽过来当我儿子”
        HoneyAccumulation acc = witch.GetComponent<HoneyAccumulation>();
        if (acc != null && decalNetId != null)
        {
            acc.RpcAttachDecal(decalNetId);
        }

        // 5. 在服务器端也强行建立一下父子关系，保证逻辑严谨
        decal.transform.SetParent(witch.transform);
        decal.transform.localPosition = new Vector3(0, 1.0f, 0);
        decal.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        decal.transform.localScale = Vector3.one;

        // 6. 销毁定时器
        Destroy(decal, decalDuration);
    }

    [Server]
    private void SpawnDecalOnEnvironment()
    {
        if (environmentDecalPrefab == null) return;

        Vector3 moveDir = GetComponent<Rigidbody>().velocity.normalized;
        if (moveDir == Vector3.zero) moveDir = transform.forward;

        RaycastHit hit;
        // 针对环境，依然使用射线来寻找地表精准位置
        if (Physics.Raycast(transform.position - moveDir, moveDir, out hit, 5f, ~LayerMask.GetMask("Bullet", "Player")))
        {
            Quaternion verticalRot = Quaternion.Euler(90f, 0f, 0f);

            // 在撞击点上方 0.3 米生成，垂直向下照
            Vector3 spawnPos = hit.point + Vector3.up * 0.3f;

            GameObject decal = Instantiate(environmentDecalPrefab, spawnPos, verticalRot);
            NetworkServer.Spawn(decal);
            Destroy(decal, decalDuration);
        }
    }
}