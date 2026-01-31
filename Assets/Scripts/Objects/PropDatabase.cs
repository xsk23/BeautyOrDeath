using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor; // 必须在编辑器环境下
#endif

public class PropDatabase : MonoBehaviour
{
    public static PropDatabase Instance;

    [Header("可变身物品列表 (索引即为 ID)")]
    public List<GameObject> propPrefabs;
    
    [Header("仅限服务器自动生成的动物列表")]
    public List<GameObject> animalPrefabs;

    private Dictionary<int, PropTarget> runtimeProps = new Dictionary<int, PropTarget>();
    [Header("全局视觉设置")]
    public Material defaultHighlightMaterial; // <--- 在 Inspector 拖入你的 Mat_Outline
    private void Awake()
    {
        Instance = this;
    }

    // ========================================================
    // 【自动化工具】自动分配场景中所有物体的 PropID
    // ========================================================
    #if UNITY_EDITOR
    [ContextMenu("Update All Scene Prop IDs")]
    public void UpdateScenePropIDs()
    {
        // 1. 获取场景中所有的 PropTarget
        PropTarget[] allTargets = Object.FindObjectsOfType<PropTarget>(true);
        int updatedCount = 0;
        int warningCount = 0;

        foreach (var target in allTargets)
        {
            // 2. 找到该实例对应的 Prefab 资源物体
            GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(target.gameObject);
            
            if (prefabSource == null)
            {
                // 如果这个物体不是从 Prefab 拖出来的，或者 Prefab 链接断了
                Debug.LogWarning($"[PropDatabase] 物体 '{target.name}' 不是 Prefab 实例，无法自动分配 ID。", target);
                warningCount++;
                continue;
            }

            // 3. 在列表中寻找这个 Prefab 的索引
            int index = propPrefabs.IndexOf(prefabSource);

            if (index != -1)
            {
                // 4. 赋值并标记脏数据（确保保存场景时能存住）
                if (target.propID != index)
                {
                    Undo.RecordObject(target, "Auto Assign Prop ID");
                    target.propID = index;
                    EditorUtility.SetDirty(target);
                    updatedCount++;
                }
            }
            else
            {
                Debug.LogError($"[PropDatabase] 场景物体 '{target.name}' 的 Prefab 不在 propPrefabs 列表中！请先将其加入列表。", target);
                warningCount++;
            }
        }

        Debug.Log($"[PropDatabase] 自动分配完成！更新了 {updatedCount} 个物体，存在 {warningCount} 个异常，总计检查了 {allTargets.Length} 个物体。");
    }
    #endif

    // --- 原有逻辑保持不变 ---
    public void RegisterProp(int id, PropTarget prop)
    {
        if (!runtimeProps.ContainsKey(id)) runtimeProps.Add(id, prop);
        else runtimeProps[id] = prop;
    }

    public bool GetPropPrefab(int id, out GameObject prefab)
    {
        prefab = null;
        if (id < 0 || id >= propPrefabs.Count) return false;
        prefab = propPrefabs[id];
        return prefab != null;
    }

    public bool GetPropData(int id, out Mesh mesh, out Material[] materials, out Vector3 scale)
    {
        mesh = null; materials = null; scale = Vector3.one;
        if (runtimeProps.TryGetValue(id, out PropTarget prop))
        {
            // Renderer rd = prop.GetComponentInChildren<Renderer>();
            // 优先寻找名字里带 "LOD0" 的渲染器，如果没有，就取第一个
            Renderer rd = prop.GetComponentsInChildren<Renderer>()
                            .FirstOrDefault(r => r.name.Contains("LOD0")) 
                        ?? prop.GetComponentInChildren<Renderer>();
            if (rd != null)
            {
                materials = rd.sharedMaterials;
                scale = prop.transform.lossyScale;
                if (rd is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else {
                    MeshFilter mf = prop.GetComponentInChildren<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }
                return mesh != null;
            }
        }
        return false;
    }
}