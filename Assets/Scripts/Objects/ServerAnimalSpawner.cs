using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class ServerAnimalSpawner : NetworkBehaviour
{
    [Header("生成设置")]
    public int animalCount = 10; // 生成总数
    public Terrain targetTerrain;
    
    // 生成区域边距（防止生成在地图最边缘）
    public float margin = 5f;

    [Server]
    public void SpawnAnimals(int countFromManager)
    {
        // 1. 改为使用 PropDatabase 里的 animalPrefabs
        var db = PropDatabase.Instance;
        if (db == null || db.animalPrefabs.Count == 0) return;

        TerrainData tData = targetTerrain.terrainData;
        Vector3 terrainPos = targetTerrain.transform.position;

        for (int i = 0; i < countFromManager; i++)
        {
            // 随机选一只动物
            int animalIndex = Random.Range(0, db.animalPrefabs.Count);
            GameObject prefab = db.animalPrefabs[animalIndex];

            // 计算位置
            float worldX = terrainPos.x + Random.Range(margin, tData.size.x - margin);
            float worldZ = terrainPos.z + Random.Range(margin, tData.size.z - margin);
            float worldY = targetTerrain.SampleHeight(new Vector3(worldX, 0, worldZ)) + terrainPos.y;
            Vector3 spawnPoint = new Vector3(worldX, worldY, worldZ);

            // 实例化
            GameObject animal = Instantiate(prefab, spawnPoint, Quaternion.Euler(0, Random.Range(0, 360), 0));
            
            // 【关键】映射 propID
            // 女巫变身需要的是该物体在 db.propPrefabs 里的索引
            // 我们通过 Prefab 引用找到它在总表里的位置
            PropTarget propTarget = animal.GetComponentInChildren<PropTarget>();
            if (propTarget != null)
            {
                propTarget.propID = db.propPrefabs.IndexOf(prefab);
            }

            // 在服务器生成并广播给所有客户端
            NetworkServer.Spawn(animal);
        }
    }
}