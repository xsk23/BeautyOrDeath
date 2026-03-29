using UnityEngine;
using Mirror;

public class NetBullet : MonoBehaviour
{
    [HideInInspector] public GameObject launcherRoot; 
    [HideInInspector] public PlayerRole ownerRole;
    [HideInInspector] public GameObject honeyPuddlePrefab;
    [HideInInspector] public float puddleDuration = 12f;

    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        // 1. 彻底忽略发射者及其子物体
        if (launcherRoot != null && (other.gameObject == launcherRoot || other.transform.IsChildOf(launcherRoot.transform)))
            return;

        // 2. 忽略所有触发器 (Trigger)
        if (other.isTrigger) return;

        Rigidbody rb = GetComponent<Rigidbody>();
        Vector3 moveDirection = rb != null ? rb.velocity.normalized : transform.forward;

        // 3. 检查玩家
        GamePlayer target = other.GetComponent<GamePlayer>() ?? other.GetComponentInParent<GamePlayer>();
        if (target != null)
        {
            if (target.playerRole != ownerRole || GameManager.Instance.FriendlyFire)
            {
                target.ServerGetTrapped();
                // 修复点 1：第二个参数传入 target.transform 而不是 Vector3
                HandlePuddleSpawn(target.transform.position, target.transform);
                Destroy(gameObject);
            }
            return;
        }

        // 4. 环境碰撞处理
        RaycastHit hit;
        if (Physics.Raycast(transform.position - moveDirection * 0.5f, moveDirection, out hit, 2f))
        {
            // 修复点 2：第二个参数传入 null（或 hit.collider.transform 如果你想让它随地板移动）
            HandlePuddleSpawn(hit.point, null);
        }
        else
        {
            // 修复点 3：兜底逻辑，第二个参数传入 null
            HandlePuddleSpawn(transform.position, null);
        }
        Destroy(gameObject);
    }

    [Server]
    private void HandlePuddleSpawn(Vector3 worldPoint, Transform parent)
    {
        if (honeyPuddlePrefab == null) return;

        // 使用 Prefab 默认旋转
        Quaternion spawnRot = honeyPuddlePrefab.transform.rotation;

        GameObject puddle = Instantiate(honeyPuddlePrefab, worldPoint, spawnRot);

        // 如果传入了父物体（说明命中了玩家）
        if (parent != null)
        {
            puddle.transform.SetParent(parent);
            // 设为父物体局部坐标的中心，并稍微抬高一点防止穿模
            puddle.transform.localPosition = new Vector3(0, 0.05f, 0); 
        }

        // 全网同步生成
        NetworkServer.Spawn(puddle);

        // 自动销毁
        Destroy(puddle, puddleDuration);
    }
}