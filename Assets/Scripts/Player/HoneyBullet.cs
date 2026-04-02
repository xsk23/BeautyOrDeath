using UnityEngine;
using Mirror;

public class HoneyBullet : MonoBehaviour
{
    [HideInInspector] public GameObject launcherRoot;
    [HideInInspector] public PlayerRole ownerRole;

    // 引用由武器传来的贴花
    [HideInInspector] public GameObject environmentDecalPrefab;
    [HideInInspector] public GameObject playerDecalPrefab;

    public float decalDuration = 8f;

    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        // 1. 忽略发射者
        if (launcherRoot != null && (other.gameObject == launcherRoot || other.transform.IsChildOf(launcherRoot.transform)))
            return;

        // 2. 忽略触发器（除非是玩家）
        if (other.isTrigger && other.GetComponentInParent<GamePlayer>() == null) return;

        // 3. 碰撞检测逻辑：尝试获取玩家组件
        GamePlayer target = other.GetComponent<GamePlayer>() ?? other.GetComponentInParent<GamePlayer>();

        if (target != null)
        {
            // 友军检查
            if (target.playerRole != ownerRole || GameManager.Instance.FriendlyFire)
            {
                if (target is WitchPlayer witch)
                {
                    // A. 增加蜂蜜累积
                    HoneyAccumulation acc = witch.GetComponent<HoneyAccumulation>();
                    if (acc != null) acc.ServerAddHoney(12f);

                    // B. 在玩家身上生成专属贴花（仅玩家层渲染）
                    SpawnDecalOnTarget(other, true);
                }
                Destroy(gameObject);
            }
            return;
        }

        // 4. 命中环境
        SpawnDecalOnTarget(other, false);
        Destroy(gameObject);
    }

    [Server]
    private void SpawnDecalOnTarget(Collider hitCollider, bool isPlayer)
    {
        Vector3 moveDir = GetComponent<Rigidbody>().velocity.normalized;
        RaycastHit hit;

        // 使用射线获取精准法线，使贴花贴合表面
        if (Physics.Raycast(transform.position - moveDir, moveDir, out hit, 5f))
        {
            GameObject prefab = isPlayer ? playerDecalPrefab : environmentDecalPrefab;
            if (prefab == null) return;

            // 生成并设置方向（面向法线反方向）
            GameObject decal = Instantiate(prefab, hit.point, Quaternion.LookRotation(-hit.normal));

            // 如果是玩家，父级设为该玩家，随其移动
            if (isPlayer)
            {
                decal.transform.SetParent(hitCollider.transform);
            }

            NetworkServer.Spawn(decal);
            Destroy(decal, decalDuration);
        }
    }
}