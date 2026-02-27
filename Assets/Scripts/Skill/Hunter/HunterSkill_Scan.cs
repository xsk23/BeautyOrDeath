using UnityEngine;
using Mirror;
using System.Collections;
using System.Collections.Generic;

// 定义一个新结构体，用来打包“某一个女巫”的所有数据
[System.Serializable]
public struct WitchTrailGroup
{
    public Color trailColor;        // 这个女巫的代表色
    public TrailSnapshot[] trails;  // 这个女巫的轨迹点
}
public class HunterSkill_Scan : SkillBase
{
    public enum ScanMode
    {
        Footprints, 
        Ghost       
    }

    [Header("侦察设置")]
    public float scanRadius = 15f; 
    public float visualDuration = 2f; 
    public ScanMode currentMode = ScanMode.Ghost; 

    [Header("视觉过滤")]
    public float minVisualDistance = 1.0f; // 距离小于1米就不生成新的残影模型

    [Header("生成节奏")]
    public float spawnInterval = 0.5f; // 【新增】每个残影之间生成的间隔时间

    
    [Header("渐变设置")]
    [Range(0f, 1f)] public float minAlpha = 0.1f; 
    [Range(0f, 1f)] public float maxAlpha = 0.6f; 

    [Header("视觉资源")]
    public GameObject footprintPrefab;
    public GameObject humanGhostPrefab;
    public Material ghostMaterial;
    public GameObject fireflyParticlePrefab; 

    // Shader 属性 ID
    private static readonly int ColorPropID = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropID = Shader.PropertyToID("_BaseColor");

    protected override void OnCast()
    {
        ServerScanLogic(ownerPlayer.transform.position);
    }

    [Server] 
    private void ServerScanLogic(Vector3 center)
    {
        //  创建一个组的列表, 每个组包含一个女巫的所有轨迹和一个独特的颜色
        List<WitchTrailGroup> allGroups = new List<WitchTrailGroup>();

        foreach (var player in GamePlayer.AllPlayers)
        {
            if (player is WitchPlayer witch && !witch.isPermanentDead)
            {
                var recorder = witch.GetComponent<WitchTrailRecorder>();
                if (recorder != null)
                {
                    // 获取单个女巫的轨迹
                    var trailsList = recorder.GetTrailsInArea(center, scanRadius);
                    
                    if (trailsList.Count > 0)
                    {
                        // 生成唯一颜色 

                        Color uniqueColor = GetWitchColor(witch);

                        // 打包成组
                        WitchTrailGroup group = new WitchTrailGroup
                        {
                            trailColor = uniqueColor,
                            trails = trailsList.ToArray()
                        };
                        
                        allGroups.Add(group);
                    }
                }
            }
        }

        //Debug.Log($"[Server] Scan found {allGroups.Count} witch groups.");

        // 发送组数据
        NetworkConnection targetConn = ownerPlayer.connectionToClient;
        if (targetConn != null)
        {
            TargetShowTrails(targetConn, allGroups.ToArray());
        }
        else if (ownerPlayer.isLocalPlayer) 
        {
            //howTrailsLocal(allGroups.ToArray());
            StartCoroutine(SpawnTrailsSequentially(allGroups.ToArray()));
        }
    }

    // 辅助函数：获取女巫颜色
    private Color GetWitchColor(WitchPlayer witch)
    {
        // 使用 NetID 作为种子，确保同一个玩家每次被扫描颜色都一样
        Random.InitState((int)witch.netId);
        // 生成鲜艳的颜色 (Saturation 和 Value 调高)
        return Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f);
    }

    [TargetRpc]
    private void TargetShowTrails(NetworkConnection target, WitchTrailGroup[] groups)
    {
        //ShowTrailsLocal(groups);
        StartCoroutine(SpawnTrailsSequentially(groups));
    }


    private IEnumerator SpawnTrailsSequentially(WitchTrailGroup[] groups)
    {
        if (groups.Length == 0) yield break;

        // 1. 找到所有女巫中最长的一条轨迹长度
        int maxTrails = 0;
        foreach (var group in groups)
        {
            if (group.trails.Length > maxTrails)
                maxTrails = group.trails.Length;
        }

        // 2. 【新增】为每个女巫准备独立的状态追踪器
        Vector3[] lastSpawnedPos = new Vector3[groups.Length];
        int[] lastPropIDs = new int[groups.Length];
        int[] stackedCounts = new int[groups.Length];

        // 初始化追踪器
        for (int w = 0; w < groups.Length; w++)
        {
            lastSpawnedPos[w] = new Vector3(9999f, 9999f, 9999f); // 初始设为极远的点
            lastPropIDs[w] = -999;
            stackedCounts[w] = 0;
        }

        // 3. 按时间顺序逐个遍历
        for (int i = 0; i < maxTrails; i++)
        {
            bool spawnedAny = false;

            // 同时遍历所有女巫
            for (int w = 0; w < groups.Length; w++)
            {
                var group = groups[w];
                
                // 如果这个女巫在当前时间节点有痕迹
                if (i < group.trails.Length)
                {
                    TrailSnapshot currentSnap = group.trails[i];

                    // 【核心过滤逻辑】计算与上一次生成点的距离
                    float distSqr = Vector3.SqrMagnitude(currentSnap.position - lastSpawnedPos[w]);
                    // 判断变身形态是否发生了改变（即使在原地，只要变身了也应该生成新残影）
                    bool propChanged = currentSnap.propID != lastPropIDs[w];

                    // 如果距离大于阈值，或者形态改变了，才生成！
                    if (distSqr >= (minVisualDistance * minVisualDistance) || propChanged)
                    {
                        GameObject spawnedObj = null;

                        if (currentMode == ScanMode.Footprints)
                            spawnedObj = SpawnFootprint(currentSnap, group.trailColor);
                        else
                            spawnedObj = SpawnGhost(currentSnap, group.trailColor);

                        // 【进阶视觉表现】如果女巫在之前的位置蹲了很久（重叠次数多），可以把这个新残影放大！
                        if (spawnedObj != null && stackedCounts[w] >= 4)
                        {
                            // 例如：原地呆了 4 个快照(2秒)以上，残影变大 1.3 倍，提示猎人她在这里龟缩过
                            spawnedObj.transform.localScale *= 1.3f;
                        }

                        // 更新追踪器状态
                        lastSpawnedPos[w] = currentSnap.position;
                        lastPropIDs[w] = currentSnap.propID;
                        stackedCounts[w] = 0; // 重置重叠计数

                        spawnedAny = true;
                    }
                    else
                    {
                        // 如果距离太近（原地发呆），不生成模型，但增加重叠计数
                        stackedCounts[w]++;
                    }
                }
            }

            // 只要这一步生成了任何东西，就等待一段时间再生成下一个
            if (spawnedAny)
            {
                yield return new WaitForSeconds(spawnInterval);
            }
        }
    }

    
    private GameObject SpawnFootprint(TrailSnapshot trail, Color color)
    {
        if (footprintPrefab == null) return null;
        GameObject fp = Instantiate(footprintPrefab, trail.position + Vector3.up * 0.1f, trail.rotation);
        
        SetupFireflyVisual(fp, color);
        return fp; // 返回生成的对象
    }

   
    private GameObject SpawnGhost(TrailSnapshot trail, Color color)
    {
        GameObject ghostObj = null;

        if (trail.propID >= 0)
        {
            if (PropDatabase.Instance != null && PropDatabase.Instance.GetPropPrefab(trail.propID, out GameObject prefab))
            {
                ghostObj = Instantiate(prefab, trail.position, trail.rotation);
                CleanupGhostObject(ghostObj);
            }
        }
        else 
        {
            if (humanGhostPrefab != null)
            {
                ghostObj = Instantiate(humanGhostPrefab, trail.position, trail.rotation);
                CleanupGhostObject(ghostObj); 
            }
        }

        if (ghostObj != null)
        {
            SetupFireflyVisual(ghostObj, color);
        }

        return ghostObj; // 返回生成的对象
    }

    



    // private IEnumerator SpawnTrailsSequentially(WitchTrailGroup[] groups)
    // {
    //     if (groups.Length == 0) yield break;

    //     // 找到所有女巫中最长的一条轨迹长度
    //     int maxTrails = 0;
    //     foreach (var group in groups)
    //     {
    //         if (group.trails.Length > maxTrails)
    //             maxTrails = group.trails.Length;
    //     }

    //     // 按时间顺序（从最老的点到最新的点）逐个遍历
    //     for (int i = 0; i < maxTrails; i++)
    //     {
    //         bool spawnedAny = false;

    //         // 同时遍历所有女巫，确保她们的痕迹是同步向前推进的
    //         foreach (var group in groups)
    //         {
    //             // 如果这个女巫在当前时间节点有痕迹，则生成
    //             if (i < group.trails.Length)
    //             {
    //                 if (currentMode == ScanMode.Footprints)
    //                     SpawnFootprint(group.trails[i], group.trailColor);
    //                 else
    //                     SpawnGhost(group.trails[i], group.trailColor);

    //                 spawnedAny = true;
    //             }
    //         }

    //         // 只要这一步生成了任何东西，就等待一段时间再生成下一个
    //         if (spawnedAny)
    //         {
    //             // 越靠近最新的点，间隔可以越短，表现出追踪的紧迫感（可选）
    //             yield return new WaitForSeconds(spawnInterval);
    //         }
    //     }
    // }

    // private void SpawnFootprint(TrailSnapshot trail, Color color)
    // {
    //     if (footprintPrefab == null) return;
    //     GameObject fp = Instantiate(footprintPrefab, trail.position + Vector3.up * 0.1f, trail.rotation);
        
    //     SetupFireflyVisual(fp, color);
    // }

    // private void SpawnGhost(TrailSnapshot trail, Color color)
    // {
    //     GameObject ghostObj = null;

    //     if (trail.propID >= 0)
    //     {
    //         if (PropDatabase.Instance != null && PropDatabase.Instance.GetPropPrefab(trail.propID, out GameObject prefab))
    //         {
    //             ghostObj = Instantiate(prefab, trail.position, trail.rotation);
    //             CleanupGhostObject(ghostObj);
    //         }
    //     }
    //     else 
    //     {
    //         if (humanGhostPrefab != null)
    //         {
    //             ghostObj = Instantiate(humanGhostPrefab, trail.position, trail.rotation);
    //             CleanupGhostObject(ghostObj); 
    //         }
    //     }

    //     if (ghostObj != null)
    //     {
    //         SetupFireflyVisual(ghostObj, color);
    //     }
    // }

    private void CleanupGhostObject(GameObject obj)
    {
        foreach (var c in obj.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (var rb in obj.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
        foreach (var script in obj.GetComponentsInChildren<MonoBehaviour>()) Destroy(script);
        foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>()) Destroy(ps);
        foreach (var anim in obj.GetComponentsInChildren<Animator>()) Destroy(anim);
        
        obj.layer = LayerMask.NameToLayer("Ignore Raycast");
        foreach(Transform t in obj.GetComponentsInChildren<Transform>()) 
            t.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
    }

    private void SetupFireflyVisual(GameObject obj, Color fireflyColor)
    {
        // 1. 替换为黑影材质
        if (ghostMaterial != null)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                Material[] newMats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < newMats.Length; i++) newMats[i] = ghostMaterial;
                r.sharedMaterials = newMats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // 2. 动态添加特效脚本，并把粒子预制体传给它
        TrailFireflyEffect effect = obj.AddComponent<TrailFireflyEffect>();
        effect.Setup(fireflyColor, visualDuration, fireflyParticlePrefab); // 【修改】多传一个参数
    }


}