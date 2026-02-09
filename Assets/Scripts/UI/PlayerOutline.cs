using UnityEngine;
using System.Collections.Generic;

public class PlayerOutline : MonoBehaviour
{
    [SerializeField] private Renderer targetRenderer; 
    [SerializeField] private Material outlineMaterialSource; 

    private Material outlineInstance;
    private bool isVisible = false;

    void Awake()
    {
        if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();

        if (outlineMaterialSource != null)
        {
            outlineInstance = new Material(outlineMaterialSource);
        }
    }

    public void SetOutline(bool active, Color color)
    {
        if (targetRenderer == null || outlineInstance == null) return;

        bool materialLost = active && !System.Array.Exists(targetRenderer.sharedMaterials, m => m == outlineInstance);
        if (isVisible == active && !materialLost) return;

        isVisible = active;

        if (active)
        {
            outlineInstance.SetColor("_OutlineColor", color);
            AddMaterial(outlineInstance);
        }
        else
        {
            RemoveMaterial(outlineInstance);
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
        if(newRenderer != null) targetRenderer = newRenderer;
        if (isVisible) AddMaterial(outlineInstance);
    }

    void OnDestroy()
    {
        if (outlineInstance != null) Destroy(outlineInstance);
    }
}