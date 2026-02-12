using UnityEngine;
using Mirror;
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
    public float visualDuration = 5f; 
    public ScanMode currentMode = ScanMode.Ghost; 

    [Header("渐变设置")]
    [Range(0f, 1f)] public float minAlpha = 0.1f; 
    [Range(0f, 1f)] public float maxAlpha = 0.6f; 

    [Header("视觉资源")]
    public GameObject footprintPrefab;
    public GameObject humanGhostPrefab;
    public Material ghostMaterial;

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
        // 1. 创建一个组的列表，而不是点的列表
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
                        // --- 核心修改：生成唯一颜色 ---
                        // 这里尝试获取玩家脚本上的颜色，如果没有，就根据 NetID 算一个随机色
                        // 这样即使没有同步颜色变量，同一个女巫的颜色也是固定的
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

        Debug.Log($"[Server] Scan found {allGroups.Count} witch groups.");

        // 发送组数据
        NetworkConnection targetConn = ownerPlayer.connectionToClient;
        if (targetConn != null)
        {
            TargetShowTrails(targetConn, allGroups.ToArray());
        }
        else if (ownerPlayer.isLocalPlayer) 
        {
            ShowTrailsLocal(allGroups.ToArray());
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
        ShowTrailsLocal(groups);
    }

    private void ShowTrailsLocal(WitchTrailGroup[] groups)
    {
        if (groups.Length == 0) return;
        Debug.Log($"[Client] Displaying trails for {groups.Length} witches.");
        
        // --- 双层循环 ---
        // 外层：遍历不同的女巫
        foreach (var group in groups)
        {
            TrailSnapshot[] trails = group.trails;
            Color groupColor = group.trailColor;

            // 内层：遍历该女巫的轨迹点
            for (int i = 0; i < trails.Length; i++)
            {
                // 核心修复：透明度计算现在是基于“当前女巫的轨迹长度”
                // 这样每个女巫最新的点都是最清晰的 (maxAlpha)
                float t = (trails.Length > 1) ? (float)i / (trails.Length - 1) : 1f;
                float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);

                // 生成时传入 颜色 和 透明度
                if (currentMode == ScanMode.Footprints)
                {
                    SpawnFootprint(trails[i], groupColor, alpha);
                }
                else
                {
                    SpawnGhost(trails[i], groupColor, alpha);
                }
            }
        }
    }

    private void SpawnFootprint(TrailSnapshot trail, Color color, float alpha)
    {
        if (footprintPrefab == null) return;
        GameObject fp = Instantiate(footprintPrefab, trail.position + Vector3.up * 0.1f, trail.rotation);
        
        ApplyGhostMaterial(fp, color, alpha);
        
        Destroy(fp, visualDuration);
    }

    private void SpawnGhost(TrailSnapshot trail, Color color, float alpha)
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
            ApplyGhostMaterial(ghostObj, color, alpha);
            Destroy(ghostObj, visualDuration);
        }
    }

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

    // 【修改】现在接受 Color 参数，而不仅仅是 alpha
    private void ApplyGhostMaterial(GameObject obj, Color baseColor, float alphaValue)
    {
        if (ghostMaterial == null) return;

        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();

        foreach (var r in renderers)
        {
            Material[] newMats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < newMats.Length; i++) newMats[i] = ghostMaterial;
            r.sharedMaterials = newMats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            r.GetPropertyBlock(propBlock);

            // 使用传入的 baseColor (区分女巫) 并应用 alpha (区分新旧)
            Color finalColor = baseColor;
            finalColor.a = alphaValue;

            propBlock.SetColor(ColorPropID, finalColor);
            propBlock.SetColor(BaseColorPropID, finalColor);

            r.SetPropertyBlock(propBlock);
        }
    }
}