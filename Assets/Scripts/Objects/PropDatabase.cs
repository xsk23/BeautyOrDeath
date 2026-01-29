using UnityEngine;
using System.Collections.Generic;

public class PropDatabase : MonoBehaviour
{
    public static PropDatabase Instance;

    [Header("可变身物品列表")]
    // 这里拖入你的 Prefab (或者场景里用到的每种模型的一个样本)
    // 索引 0 就是 ID 0 的物品
    public List<GameObject> propPrefabs;

    private void Awake()
    {
        Instance = this;
    }

    public bool GetPropPrefab(int id, out GameObject prefab)
    {
        prefab = null;
        if (id < 0 || id >= propPrefabs.Count) return false;
        prefab = propPrefabs[id];
        return prefab != null;
    }

    // 根据 ID 获取模型数据
    public bool GetPropData(int id, out Mesh mesh, out Material[] materials, out Vector3 scale)
    {
        mesh = null;
        materials = null;
        scale = Vector3.one;

        // 基础检查
        if (id < 0 || id >= propPrefabs.Count || propPrefabs[id] == null) return false;

        GameObject prefab = propPrefabs[id];
        
        // 1. 获取渲染器（不管是哪种）
        Renderer rd = prefab.GetComponentInChildren<Renderer>();
        if (rd == null) return false;

        materials = rd.sharedMaterials;
        scale = rd.transform.localScale;

        // 2. 核心修改：尝试获取网格
        // 优先尝试静态网格 (MeshFilter)
        MeshFilter mf = prefab.GetComponentInChildren<MeshFilter>();
        if (mf != null)
        {
            mesh = mf.sharedMesh;
        }
        else
        {
            // 如果没有 MeshFilter，尝试获取皮肤网格 (SkinnedMeshRenderer，例如鸡)
            SkinnedMeshRenderer smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null)
            {
                mesh = smr.sharedMesh;
            }
        }

        return mesh != null;
    }
}