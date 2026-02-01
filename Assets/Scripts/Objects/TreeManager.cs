using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class TreeManager : NetworkBehaviour
{
    public static TreeManager Instance;

    [Header("Settings")]
    public float positionOffsetRange = 0.5f; // 随机偏移范围
    public bool randomYRotation = true;    // 是否允许随机旋转（让森林看起来更自然）

    private List<PropTarget> allTrees = new List<PropTarget>();
    private List<Vector3> spawnPositions = new List<Vector3>();
    [Header("Ancient Tree Settings")]
    public int ancientTreeCount = 3; // 每局产生的古树数量
    private void Awake()
    {
        Instance = this;
    }

    [Server] // 仅在服务器运行位置分配
    public void ShuffleTrees()
    {
        // 1. 获取场景中所有标记为树的 PropTarget
        allTrees.Clear();
        spawnPositions.Clear();

        PropTarget[] sceneProps = Object.FindObjectsOfType<PropTarget>();
        foreach (var prop in sceneProps)
        {
            if (prop.isStaticTree)
            {
                // 【新增】重置所有树的古树状态，防止连局游戏状态残留
                prop.isAncientTree = false;
                allTrees.Add(prop);
                spawnPositions.Add(prop.transform.position);
            }
        }

        if (allTrees.Count == 0)
        {
            Debug.LogWarning("[TreeManager] No objects marked as isStaticTree were found.");
            return;
        }

        Debug.Log($"[TreeManager] Shuffling positions of {allTrees.Count} trees...");

        // 2. 洗牌算法 (Fisher-Yates Shuffle) 打乱位置列表
        for (int i = 0; i < spawnPositions.Count; i++)
        {
            Vector3 temp = spawnPositions[i];
            int randomIndex = Random.Range(i, spawnPositions.Count);
            spawnPositions[i] = spawnPositions[randomIndex];
            spawnPositions[randomIndex] = temp;
        }

        // 3. 分配位置并标记前 N 棵为古树
        for (int i = 0; i < allTrees.Count; i++)
        {
            // 分配位置偏移
            Vector3 offset = new Vector3(Random.Range(-positionOffsetRange, positionOffsetRange), 0, Random.Range(-positionOffsetRange, positionOffsetRange));
            allTrees[i].transform.position = spawnPositions[i] + offset;
            if (randomYRotation) allTrees[i].transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);

            // 【关键】标记古树
            if (i < ancientTreeCount)
            {
                allTrees[i].isAncientTree = true;
                Debug.Log($"[TreeManager] Tree {allTrees[i].name} set as ANCIENT TREE.");
            }
        }
    }
}