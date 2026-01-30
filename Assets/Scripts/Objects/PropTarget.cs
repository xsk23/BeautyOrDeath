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
    // 不需要手动拖，代码会自动找
    public Renderer targetRenderer; 
    
    [Header("Highlight Settings")]
    [SerializeField] private Material outlineMaterialSource; 
    private Material outlineInstance;
    private bool isHighlighted = false;

    private Material[] originalMaterials; // 保存初始材质数组
    private Material[] highlightedMaterials; // 预存高亮时的材质数组

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
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>();
        }
        InitMaterials();
    }

    private void InitMaterials()
    {
        if (targetRenderer == null) return;

        // 1. 记录初始材质
        originalMaterials = targetRenderer.sharedMaterials;

        // 2. 预热高亮材质
        if (outlineMaterialSource != null)
        {
            if (outlineInstance == null) 
            {
                outlineInstance = new Material(outlineMaterialSource);
                outlineInstance.color = Color.yellow; 
            }

            highlightedMaterials = new Material[originalMaterials.Length + 1];
            for (int i = 0; i < originalMaterials.Length; i++)
            {
                highlightedMaterials[i] = originalMaterials[i];
            }
            highlightedMaterials[highlightedMaterials.Length - 1] = outlineInstance;
        }
    }
    
    // 【新增】供女巫变身后手动初始化
    public void ManualInit(int id, Renderer r)
    {
        this.propID = id;
        this.targetRenderer = r;
        InitMaterials();
    }


    private void Awake()
    {

    }

    public void SetHighlight(bool active)
    {
        if (targetRenderer == null || highlightedMaterials == null) return;
        if (isHighlighted == active) return;
        // 【修复】增加 enabled 检查，防止给隐藏的物体高亮
        if (!targetRenderer.enabled) return;
        isHighlighted = active;

        // 直接整体替换数组，效率最高且避免引用问题
        targetRenderer.materials = active ? highlightedMaterials : originalMaterials;
    }

    void OnDestroy()
    {
        if (outlineInstance != null) Destroy(outlineInstance);
    }
}