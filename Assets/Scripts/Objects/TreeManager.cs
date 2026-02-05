using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class TreeManager : NetworkBehaviour
{
    public static TreeManager Instance;

    [Header("Spawn Protection")]
    public float spawnSafeRadius = 4.0f; // 出生点周围保护半径

    [Header("Forest Density & Spacing")]
    public float minTreeSpacing = 2.5f; // 树与树之间的最小间距
    [Tooltip("当位置冲突时，最大尝试偏移寻找新位置的次数")]
    public int maxAdjustmentAttempts = 5; 
    [Tooltip("每次尝试偏移的距离步长")]
    public float adjustmentStep = 1.5f;

    [Header("Settings")]
    public float positionOffsetRange = 0.5f; // 最终分布时的微小随机抖动
    public bool randomYRotation = true;    // 随机旋转

    private List<PropTarget> allTrees = new List<PropTarget>();

    private void Awake()
    {
        Instance = this;
    }

    [Server]
    public void ShuffleTrees()
    {
        // 1. 获取所有出生点
        List<Vector3> spawnPoints = new List<Vector3>();
        var nss = Object.FindObjectsOfType<Mirror.NetworkStartPosition>();
        foreach (var sp in nss) spawnPoints.Add(sp.transform.position);
        
        if (spawnPoints.Count == 0) {
            GameObject[] groups = { GameObject.Find("WitchSpawnPoints"), GameObject.Find("HunterSpawnPoints") };
            foreach(var g in groups) if(g != null) foreach(Transform t in g.transform) spawnPoints.Add(t.position);
        }

        // 2. 初始化树木状态并收集所有原始坐标
        allTrees.Clear();
        List<Vector3> rawCandidatePositions = new List<Vector3>();

        PropTarget[] sceneProps = Object.FindObjectsOfType<PropTarget>();
        foreach (var prop in sceneProps)
        {
            if (prop.isStaticTree)
            {
                prop.isAncientTree = false; 
                prop.isHiddenByPossession = false;
                prop.ServerSetHidden(false);
                allTrees.Add(prop);
                rawCandidatePositions.Add(prop.transform.position);
            }
        }

        if (allTrees.Count == 0) return;

        // 3. 打乱候选坐标顺序
        for (int i = 0; i < rawCandidatePositions.Count; i++) {
            Vector3 temp = rawCandidatePositions[i];
            int randomIndex = Random.Range(i, rawCandidatePositions.Count);
            rawCandidatePositions[i] = rawCandidatePositions[randomIndex];
            rawCandidatePositions[randomIndex] = temp;
        }

        // 4. 【核心逻辑修改】筛选并尝试偏移坐标
        List<Vector3> finalFilteredPositions = new List<Vector3>();
        
        foreach (Vector3 originalPos in rawCandidatePositions) {
            Vector3 currentTestPos = originalPos;
            bool successfullyPlaced = false;

            // 尝试多次偏移以寻找合法位置
            for (int attempt = 0; attempt <= maxAdjustmentAttempts; attempt++) {
                if (IsPositionValid(currentTestPos, finalFilteredPositions, spawnPoints)) {
                    finalFilteredPositions.Add(currentTestPos);
                    successfullyPlaced = true;
                    break;
                }

                // 如果不合法，计算一个随机偏移量尝试推开
                // 随着尝试次数增加，偏移半径逐渐扩大
                Vector2 randomNudge = Random.insideUnitCircle.normalized * (adjustmentStep * (attempt + 1));
                currentTestPos = new Vector3(originalPos.x + randomNudge.x, originalPos.y, originalPos.z + randomNudge.y);
            }
            
            // 如果经过多次偏移还是找不到位置，该树将在后续步骤被隐藏（防止重叠卡死）
        }

        Debug.Log($"[TreeManager] {allTrees.Count} trees total. Successfully spaced {finalFilteredPositions.Count} positions.");

        // 5. 分配最终坐标
        int dynamicAncientCount = GameManager.Instance.GetCalculatedAncientTreeCount();
        int actualAncientCount = 0;

        for (int i = 0; i < allTrees.Count; i++)
        {
            if (i >= finalFilteredPositions.Count) {
                // 如果偏移重试后依然无法满足间距限制，将多余的树移除地图
                allTrees[i].transform.position = Vector3.down * 100f; 
                allTrees[i].ServerSetHidden(true);
                continue;
            }

            Vector3 targetBasePos = finalFilteredPositions[i];
            
            // 最后的微小随机抖动（不破坏整体间距）
            float jitter = Mathf.Min(positionOffsetRange, minTreeSpacing * 0.1f);
            Vector3 finalPos = targetBasePos + new Vector3(Random.Range(-jitter, jitter), 0, Random.Range(-jitter, jitter));
            
            allTrees[i].transform.position = finalPos;
            if (randomYRotation) allTrees[i].transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);

            if (i < dynamicAncientCount) {
                allTrees[i].isAncientTree = true;
                actualAncientCount++;
            }
        }

        if (GameManager.Instance != null) {
            GameManager.Instance.availableAncientTreesCount = actualAncientCount;
        }
    }

    // 辅助判定函数：检查坐标是否同时远离已选中的树和出生点
    private bool IsPositionValid(Vector3 pos, List<Vector3> acceptedPositions, List<Vector3> spawnPoints)
    {
        // 检查与出生点的距离
        foreach (Vector3 spPos in spawnPoints) {
            if (Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(spPos.x, spPos.z)) < spawnSafeRadius)
                return false;
        }

        // 检查与其他树的距离
        foreach (Vector3 acceptedPos in acceptedPositions) {
            if (Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(acceptedPos.x, acceptedPos.z)) < minTreeSpacing)
                return false;
        }

        return true;
    }
}