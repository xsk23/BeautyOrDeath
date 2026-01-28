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

    // 根据 ID 获取模型数据
    public bool GetPropData(int id, out Mesh mesh, out Material[] materials, out Vector3 scale)
    {
        mesh = null;
        materials = null;
        scale = Vector3.one;

        if (id < 0 || id >= propPrefabs.Count || propPrefabs[id] == null) return false;

        GameObject prefab = propPrefabs[id];
        MeshFilter mf = prefab.GetComponentInChildren<MeshFilter>();
        Renderer rd = prefab.GetComponentInChildren<Renderer>();

        if (mf != null && rd != null)
        {
            mesh = mf.sharedMesh;
            materials = rd.sharedMaterials;
            // 获取 Prefab 身上 Renderer 所在物体的缩放
            scale = rd.transform.localScale; 
            return true;
        }
        return false;
    }
}