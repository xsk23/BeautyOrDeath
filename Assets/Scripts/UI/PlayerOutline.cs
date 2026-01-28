using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class PlayerOutline : MonoBehaviour
{
    [Header("设置")]
    // 拖入你的模型渲染器 (SkinnedMeshRenderer)
    [SerializeField] private Renderer targetRenderer; 
    // 拖入刚才创建的 Mat_Outline
    [SerializeField] private Material outlineMaterialSource; 

    private Material outlineInstance;
    private bool isVisible = false;

    void Awake()
    {
        // 自动查找渲染器（如果没拖的话）
        if (targetRenderer == null) 
            targetRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
        if (targetRenderer == null) 
            targetRenderer = GetComponentInChildren<MeshRenderer>();

        if (outlineMaterialSource != null)
        {
            // 创建材质实例，防止所有玩家共用一个颜色
            outlineInstance = new Material(outlineMaterialSource);
        }
    }

    // 外部调用的唯一接口
    public void SetOutline(bool active, Color color)
    {
        if (targetRenderer == null || outlineInstance == null) return;

        // 检查高亮材质是否还在（防止被 WitchPlayer.ApplyMorph 覆盖掉）
        bool materialLost = active && !targetRenderer.sharedMaterials.Contains(outlineInstance);
        bool colorChanged = (active && outlineInstance.GetColor("_OutlineColor") != color);

        // 如果状态、颜色都没变，且材质也没丢，才返回
        if (isVisible == active && !colorChanged && !materialLost) return;

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
    // 建议增加一个方法，允许 WitchPlayer 在变身后手动调用
    public void RefreshRenderer(Renderer newRenderer)
    {
        // 如果变身换了 GameObject，需要更新引用
        if(newRenderer != null) targetRenderer = newRenderer;
        
        // 强制重新触发一次材质添加
        if (isVisible) 
        {
            AddMaterial(outlineInstance);
        }
    }

    // 给材质数组追加一个轮廓材质
    private void AddMaterial(Material mat)
    {
        var mats = targetRenderer.materials.ToList();
        if (!mats.Contains(mat))
        {
            mats.Add(mat);
            targetRenderer.materials = mats.ToArray();
        }
    }

    // 从材质数组移除轮廓材质
    private void RemoveMaterial(Material mat)
    {
        var mats = targetRenderer.materials.ToList();
        if (mats.Contains(mat))
        {
            mats.Remove(mat);
            targetRenderer.materials = mats.ToArray();
        }
    }

    void OnDestroy()
    {
        if (outlineInstance != null) Destroy(outlineInstance);
    }
}