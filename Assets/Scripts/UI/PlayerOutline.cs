using UnityEngine;
using System.Collections.Generic;

public class PlayerOutline : MonoBehaviour
{
    [SerializeField] private Renderer targetRenderer; 
    [SerializeField] private Material outlineMaterialSource; 
    // 新增：需要排除的对象（比如名字文本物体）
    [SerializeField] private GameObject nameTextObject; 
    private Material outlineInstance;
    private bool isVisible = false;

    void Awake()
    {
        // 自动查找逻辑增强
        if (targetRenderer == null) 
        {
            // 尝试获取模型上的 Renderer，而不是随便找一个
            // 假设你的模型在名为 "Model" 或 "Visual" 的子物体下
            var allRenderers = GetComponentsInChildren<Renderer>();
            foreach (var r in allRenderers)
            {
                // 排除名字文本的 Renderer
                if (nameTextObject != null && r.transform.IsChildOf(nameTextObject.transform)) continue;
                // 排除 UI 或 TextMeshPro 的 Renderer
                if (r.gameObject.name.Contains("Name") || r.gameObject.name.Contains("Text")) continue;

                targetRenderer = r;
                break;
            }
        }

        if (outlineMaterialSource != null)
        {
            outlineInstance = new Material(outlineMaterialSource);
        }
    }

    public void SetOutline(bool active, Color color)
    {
        if (targetRenderer == null || outlineInstance == null) return;

        // 检查材质是否丢失
        bool materialLost = active && !System.Array.Exists(targetRenderer.sharedMaterials, m => m == outlineInstance);

        // --- 修改这里：即使isVisible没变，但只要是激活状态，就应该更新颜色 ---
        if (active)
        {
            // 总是更新颜色，防止状态切换（如：队友状态 -> 被抓状态）时颜色不刷新
            outlineInstance.SetColor("_OutlineColor", color);
            
            // 如果状态变了或者是材质丢了，才去操作材质列表
            if (!isVisible || materialLost)
            {
                isVisible = true;
                AddMaterial(outlineInstance);
            }
        }
        else
        {
            // 如果当前是可见的，现在要关闭，才执行移除
            if (isVisible)
            {
                isVisible = false;
                RemoveMaterial(outlineInstance);
            }
        }
    }

    private void AddMaterial(Material mat)
    {
        if (targetRenderer == null || mat == null) return;
        
        // 使用 sharedMaterials 避开 Prefab 访问限制
        Material[] currentShared = targetRenderer.sharedMaterials;
        List<Material> matsList = new List<Material>(currentShared);

        if (!matsList.Contains(mat))
        {
            matsList.Add(mat);
            targetRenderer.materials = matsList.ToArray(); // 赋值给 .materials 会处理实例化
        }
    }

    private void RemoveMaterial(Material mat)
    {
        if (targetRenderer == null) return;
        Material[] currentShared = targetRenderer.sharedMaterials;
        List<Material> matsList = new List<Material>(currentShared);

        if (matsList.Contains(mat))
        {
            matsList.Remove(mat);
            targetRenderer.materials = matsList.ToArray();
        }
    }

    public void RefreshRenderer(Renderer newRenderer)
    {
        if (newRenderer == null) return;
        
        // 增加一个安全检查：确保新传入的不是名字物体
        if (nameTextObject != null && newRenderer.transform.IsChildOf(nameTextObject.transform)) return;

        if (isVisible && targetRenderer != null)
        {
            RemoveMaterial(outlineInstance);
        }

        targetRenderer = newRenderer;

        if (isVisible)
        {
            AddMaterial(outlineInstance);
        }
    }

    void OnDestroy()
    {
        if (outlineInstance != null) Destroy(outlineInstance);
    }
}