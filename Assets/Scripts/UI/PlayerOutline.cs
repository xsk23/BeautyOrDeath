using UnityEngine;
using System.Collections.Generic;

public class PlayerOutline : MonoBehaviour
{
    [SerializeField] private Renderer targetRenderer; 
    [SerializeField] private Material outlineMaterialSource; // 你的 Mat_TeamOutline
    [SerializeField] private GameObject nameTextObject; 
    [SerializeField] private Shader maskShaderSource; 
    private Material outlineInstance; // 描边材质
    private Material maskInstance;    // 遮罩材质 (代码自动生成)
    private bool isVisible = false;

    void Awake()
    {
        // 自动查找逻辑增强
        if (targetRenderer == null) 
        {
            // 【核心修改 1】：优先查找 SkinnedMeshRenderer (人物身体几乎都是这个组件，而武器通常是普通的 MeshRenderer)
            var skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var r in skinnedRenderers)
            {
                // 排除 UI 或不需要的对象
                if (nameTextObject != null && r.transform.IsChildOf(nameTextObject.transform)) continue;
                
                targetRenderer = r;
                break; // 找到第一个身体蒙皮就停止
            }

            // 【核心修改 2】：如果万一没找到身体，再使用兜底逻辑找普通 Renderer，并严格排除武器
            if (targetRenderer == null)
            {
                var allRenderers = GetComponentsInChildren<Renderer>(true);
                foreach (var r in allRenderers)
                {
                    if (nameTextObject != null && r.transform.IsChildOf(nameTextObject.transform)) continue;
                    if (r.gameObject.name.Contains("Name") || r.gameObject.name.Contains("Text")) continue;
                    
                    // 强制排除名字里带有 weapon 的物体（忽略大小写）
                    if (r.gameObject.name.ToLower().Contains("weapon")) continue;

                    targetRenderer = r;
                    break;
                }
            }
        }

        if (outlineMaterialSource != null)
        {
            // 1. 实例化描边材质
            outlineInstance = new Material(outlineMaterialSource);
            outlineInstance.SetFloat("_ZTestMode", 8f);

            // 2. 动态创建遮罩材质 (解决 URP 无法多 Pass 的痛点)
            // 修改这里：不再使用 Shader.Find
            if (maskShaderSource != null)
            {
                maskInstance = new Material(maskShaderSource);
                maskInstance.SetFloat("_ZTestMode", 8f);
            }
            else
            {
                Debug.LogError("PlayerOutline: Mask Shader 未在 Inspector 中赋值！");
            }
        }
    }

    public void SetOutline(bool active, Color color)
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            active = false;
        }
        
        if (targetRenderer == null || outlineInstance == null) return;

        bool materialLost = active && !System.Array.Exists(targetRenderer.sharedMaterials, m => m == outlineInstance);

        if (active)
        {
            // 更新颜色和宽度
            outlineInstance.SetColor("_OutlineColor", color);
            outlineInstance.SetFloat("_ZTestMode", 8f); // 8 = Always 穿墙透视
            outlineInstance.SetFloat("_OutlineWidth", 0.02f); 

            if (maskInstance != null) maskInstance.SetFloat("_ZTestMode", 8f);

            if (!isVisible || materialLost)
            {
                isVisible = true;
                AddMaterials();
            }
        }
        else
        {
            if (isVisible)
            {
                isVisible = false;
                RemoveMaterials();
            }
        }
    }

    private void AddMaterials()
    {
        if (targetRenderer == null) return;
        
        List<Material> matsList = new List<Material>(targetRenderer.sharedMaterials);

        // 核心逻辑：必须先添加 Mask，再添加 Outline！顺序决定了 URP 的渲染顺序。
        if (maskInstance != null && !matsList.Contains(maskInstance)) matsList.Add(maskInstance);
        if (outlineInstance != null && !matsList.Contains(outlineInstance)) matsList.Add(outlineInstance);

        targetRenderer.sharedMaterials = matsList.ToArray(); 
    }

    private void RemoveMaterials()
    {
        if (targetRenderer == null) return;
        
        List<Material> matsList = new List<Material>(targetRenderer.sharedMaterials);

        if (maskInstance != null && matsList.Contains(maskInstance)) matsList.Remove(maskInstance);
        if (outlineInstance != null && matsList.Contains(outlineInstance)) matsList.Remove(outlineInstance);

        targetRenderer.sharedMaterials = matsList.ToArray();
    }

    public void RefreshRenderer(Renderer newRenderer)
    {
        if (newRenderer == null) return;
        if (nameTextObject != null && newRenderer.transform.IsChildOf(nameTextObject.transform)) return;

        if (isVisible && targetRenderer != null)
        {
            try { RemoveMaterials(); } catch { }
        }

        // 【核心修改】：如果传入的是普通 MeshRenderer，尝试在同级或子级看有没有 SkinnedMeshRenderer
        // 这能防止 WitchPlayer 传入道具模型时，我们依然能找到旁边的身体模型
        Renderer finalRenderer = newRenderer;
        
        // 如果传入的是个小东西（比如扫帚），尝试在它的父物体（通常是模型根节点）下找皮肤
        SkinnedMeshRenderer smr = newRenderer.GetComponentInParent<SkinnedMeshRenderer>() 
                            ?? newRenderer.transform.parent?.GetComponentInChildren<SkinnedMeshRenderer>();

        if (smr != null)
        {
            finalRenderer = smr;
        }

        targetRenderer = finalRenderer;

        if (isVisible)
        {
            AddMaterials();
        }
    }

    void OnDestroy()
    {
        if (outlineInstance != null) Destroy(outlineInstance);
        if (maskInstance != null) Destroy(maskInstance);
    }
}