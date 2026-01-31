using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Mirror;

public class PropTarget : NetworkBehaviour
{
    [Header("Identity")]
    [SyncVar]
    public int propID; 
    public int runtimeID;

    [Header("Visuals")]
    // 修改：改为存储多个渲染器
    private Renderer[] allLODRenderers; 
    
    [Header("Highlight Settings")]
    [SerializeField] private Material outlineMaterialSource; 
    private Material outlineInstance;
    private bool isHighlighted = false;

    // 【新增属性】方便 WitchPlayer 判断是否需要初始化
    public bool IsInitialized => allLODRenderers != null && allLODRenderers.Length > 0;

    // private Material[] originalMaterials; // 保存初始材质数组
    // private Material[] highlightedMaterials; // 预存高亮时的材质数组
    private List<Material[]> originalMaterialsList = new List<Material[]>();
    private List<Material[]> highlightedMaterialsList = new List<Material[]>();


    public override void OnStartClient()
    {
        Register();
    }
     public override void OnStartServer()
    {
        Register();
    }   
    private void Register()
    {
        runtimeID = (int)netId; 
        if (PropDatabase.Instance != null)
        {
            PropDatabase.Instance.RegisterProp(runtimeID, this);
        }
        
        // 如果 targetRenderer 还没赋值（比如场景里的静态物体），尝试自动查找
        // if (targetRenderer == null)
        // {
        //     targetRenderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>();
        // }
        // 自动获取所有子物体的渲染器（包括 LOD0, LOD1）
        allLODRenderers = GetComponentsInChildren<Renderer>();
        InitMaterials();
    }

    // private void InitMaterials()
    // {
    //     if (targetRenderer == null) return;

    //     // 1. 记录初始材质
    //     originalMaterials = targetRenderer.sharedMaterials;

    //     // 2. 预热高亮材质
    //     if (outlineMaterialSource != null)
    //     {
    //         if (outlineInstance == null) 
    //         {
    //             outlineInstance = new Material(outlineMaterialSource);
    //             outlineInstance.color = Color.yellow; 
    //         }

    //         highlightedMaterials = new Material[originalMaterials.Length + 1];
    //         for (int i = 0; i < originalMaterials.Length; i++)
    //         {
    //             highlightedMaterials[i] = originalMaterials[i];
    //         }
    //         highlightedMaterials[highlightedMaterials.Length - 1] = outlineInstance;
    //     }
    // }
        
    private void InitMaterials()
    {
        if (allLODRenderers == null || allLODRenderers.Length == 0) return;

        originalMaterialsList.Clear();
        highlightedMaterialsList.Clear();

        // 【核心改动】决定使用哪个源材质
        Material sourceMat = outlineMaterialSource;
        
        // 如果本地没填，尝试从数据库获取全局默认材质
        if (sourceMat == null && PropDatabase.Instance != null)
        {
            sourceMat = PropDatabase.Instance.defaultHighlightMaterial;
        }

        foreach (var renderer in allLODRenderers)
        {
            if (renderer == null) continue;

            // 1. 记录初始材质
            Material[] originals = renderer.sharedMaterials;
            originalMaterialsList.Add(originals);

            // 2. 准备高亮材质
            if (sourceMat != null) // 使用上面判断后的 sourceMat
            {
                // 确保 outlineInstance 存在 (这里可以每个 PropTarget 共享一个，也可以每个生成实例)
                // 为了防止不同物体颜色干扰，建议保持 new Material 的逻辑
                if (outlineInstance == null) 
                {
                    outlineInstance = new Material(sourceMat);
                    // 可以在这里设置颜色，比如黄色
                    if(outlineInstance.HasProperty("_OutlineColor"))
                        outlineInstance.SetColor("_OutlineColor", Color.yellow);
                }

                Material[] highlighted = new Material[originals.Length + 1];
                for (int j = 0; j < originals.Length; j++) highlighted[j] = originals[j];
                highlighted[highlighted.Length - 1] = outlineInstance;
                highlightedMaterialsList.Add(highlighted);
            }
            else
            {
                // 如果连全局的都没有，则用原材质占位，防止越界
                highlightedMaterialsList.Add(originals);
            }
        }
    }

    // 【新增】供女巫变身后手动初始化
    public void ManualInit(int id, GameObject visualRoot)
    {
        this.propID = id;
        // 获取变身模型下所有的渲染器，这样变身后的物体也能支持多级 LOD 高亮
        this.allLODRenderers = visualRoot.GetComponentsInChildren<Renderer>();
        InitMaterials();
    }


    private void Awake()
    {

    }

    // public void SetHighlight(bool active)
    // {
    //     if (targetRenderer == null || highlightedMaterials == null) return;
    //     if (isHighlighted == active) return;
    //     // 【修复】增加 enabled 检查，防止给隐藏的物体高亮
    //     if (!targetRenderer.enabled) return;
    //     isHighlighted = active;

    //     // 直接整体替换数组，效率最高且避免引用问题
    //     targetRenderer.materials = active ? highlightedMaterials : originalMaterials;
    // }

    public void SetHighlight(bool active)
    {
        // 【增强安全检查】
        if (allLODRenderers == null || 
            highlightedMaterialsList.Count != allLODRenderers.Length || 
            originalMaterialsList.Count != allLODRenderers.Length) 
        {
            return;
        }
        
        if (isHighlighted == active) return;
        isHighlighted = active;

        for (int i = 0; i < allLODRenderers.Length; i++)
        {
            var renderer = allLODRenderers[i];
            // 即使渲染器没激活也要切换材质，否则 LOD 切换后显示的是旧材质
            if (renderer == null) continue;
            
            // 现在这里绝对不会报错了，因为列表长度是强制对齐的
            renderer.materials = active ? highlightedMaterialsList[i] : originalMaterialsList[i];
        }
    }
    void OnDestroy()
    {
        if (outlineInstance != null) Destroy(outlineInstance);
    }
}