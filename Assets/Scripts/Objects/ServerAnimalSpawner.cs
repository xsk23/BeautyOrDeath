using UnityEngine;
using Mirror;
using System.Collections.Generic;
using System.Diagnostics;

public class ServerAnimalSpawner : NetworkBehaviour
{
    [Header("生成区域")]
    public BoxCollider spawnArea; // 拖入用于定义范围的 BoxCollider
    public LayerMask groundLayer; // 地面层级（建议设为 Environment 或 Terrain）

    [Server]
    public void SpawnAnimals(int countFromManager)
    {
        // 1. 基础检查
        if (spawnArea == null)
        {
            // Debug.LogError("[Server] 未分配 spawnArea (BoxCollider)!");
            UnityEngine.Debug.LogError("[Server] spawnArea (BoxCollider) not assigned!");
            return;
        }

        var db = PropDatabase.Instance;
        if (db == null || db.animalPrefabs.Count == 0) return;

        // 获取 Box 的边界信息
        Bounds bounds = spawnArea.bounds;

        for (int i = 0; i < countFromManager; i++)
        {
            // 2. 在 Box 范围内随机选一个 X 和 Z
            float randomX = Random.Range(bounds.min.x, bounds.max.x);
            float randomZ = Random.Range(bounds.min.z, bounds.max.z);

            // 3. 计算高度 (Y 轴)
            // 逻辑：从 Box 的最顶部（bounds.max.y）向下发射射线
            Vector3 rayOrigin = new Vector3(randomX, bounds.max.y, randomZ);
            Vector3 spawnPoint;

            // 尝试通过射线击中地面来确定 Y 坐标
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, bounds.size.y + 10f, groundLayer))
            {
                spawnPoint = hit.point;
            }
            else
            {
                // 兜底方案：如果没射中地面，直接取 Box 的中心点高度
                spawnPoint = new Vector3(randomX, bounds.center.y, randomZ);
                //改成英文debug
                // Debug.LogWarning($"[Spawner] 未能在位置 {randomX}, {randomZ} 下方找到地面，使用默认高度。");
                UnityEngine.Debug.LogWarning($"[Spawner] Could not find ground below position {randomX}, {randomZ}, using default height.");
            }

            // 4. 随机选一只动物 Prefab
            int animalIndex = Random.Range(0, db.animalPrefabs.Count);
            GameObject prefab = db.animalPrefabs[animalIndex];

            // 5. 实例化
            GameObject animal = Instantiate(prefab, spawnPoint, Quaternion.Euler(0, Random.Range(0, 360), 0));
            
            // 6. 映射 propID
            PropTarget propTarget = animal.GetComponentInChildren<PropTarget>();
            if (propTarget != null)
            {
                propTarget.propID = db.propPrefabs.IndexOf(prefab);
            }

            // 7. 网络生成
            NetworkServer.Spawn(animal);
        }
    }
}