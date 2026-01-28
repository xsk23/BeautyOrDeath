using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PropTarget : MonoBehaviour
{
    [Header("Identity")]
    public int propID; 

    [Header("Visuals")]
    // 不需要手动拖，代码会自动找
    private Renderer targetRenderer; 
    
    [Header("Highlight Settings")]
    [SerializeField] private Material outlineMaterialSource; 
    private Material outlineInstance;
    private bool isHighlighted = false;

    private Material[] originalMaterials; // 保存初始材质数组
    private Material[] highlightedMaterials; // 预存高亮时的材质数组

    private void Awake()
    {
        targetRenderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>();

        if (targetRenderer == null) return;

        // 1. 记录初始材质 (注意用 sharedMaterials 以免在 Awake 就触发实例化)
        originalMaterials = targetRenderer.sharedMaterials;

        // 2. 预热高亮材质数组
        if (outlineMaterialSource != null)
        {
            outlineInstance = new Material(outlineMaterialSource);
            outlineInstance.color = Color.yellow; // 或者 SetColor("_OutlineColor", ...)

            // 创建一个比原数组多一个长度的新数组
            highlightedMaterials = new Material[originalMaterials.Length + 1];
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                highlightedMaterials[i] = originalMaterials[i];
            }
            highlightedMaterials[highlightedMaterials.Length - 1] = outlineInstance;
        }
    }

    public void SetHighlight(bool active)
    {
        if (targetRenderer == null || highlightedMaterials == null) return;
        if (isHighlighted == active) return;
        
        isHighlighted = active;

        // 直接整体替换数组，效率最高且避免引用问题
        targetRenderer.materials = active ? highlightedMaterials : originalMaterials;
    }

    void OnDestroy()
    {
        if (outlineInstance != null) Destroy(outlineInstance);
    }
}