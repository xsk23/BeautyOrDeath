using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Mirror;

public class PropTarget : NetworkBehaviour
{
    [Header("Identity")]
    [SyncVar]
    public int propID; 
    [SyncVar(hook = nameof(OnAncientStatusChanged))] 
    public bool isAncientTree = false;
    public int runtimeID;
    [Header("Possession State")]
    [SyncVar(hook = nameof(OnPossessedChanged))]
    public bool isHiddenByPossession = false; // 树是否因为被附身而隐藏
    [Header("Tree Manager Settings")]
    public bool isStaticTree = false; // 在 Inspector 中勾选此项
    [SyncVar(hook = nameof(OnScoutedChanged))]
    public bool isScouted = false; // 是否已被女巫发现
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

    // 当服务器同步 isAncientTree 状态到客户端时调用
    void OnAncientStatusChanged(bool oldVal, bool newVal)
    {
        // 如果渲染器已经获取到了，重新初始化材质数组以应用绿色高亮
        if (IsInitialized)
        {
            // 先销毁旧的实例，防止内存泄漏
            if (outlineInstance != null) Destroy(outlineInstance);
            outlineInstance = null;
            
            InitMaterials();
        }
    }

    // 只有古树需要同步这个 Hook
    void OnPossessedChanged(bool oldVal, bool newVal)
    {
        // 禁用/启用树的所有视觉效果和碰撞
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = !newVal;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = !newVal;
        
        // 如果是古树，还需要关闭名字显示（如果有的话）
        // if (nameText != null) nameText.gameObject.SetActive(!newVal);
    }  
    [Server]
    public void ServerSetHidden(bool hidden)
    {
        isHiddenByPossession = hidden;
    }
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

        // 【核心修改】选择源材质：如果是古树，使用古树材质，否则使用默认材质
        Material sourceMat = outlineMaterialSource;
        if (sourceMat == null && PropDatabase.Instance != null)
        {
            sourceMat = isAncientTree ? 
                PropDatabase.Instance.ancientHighlightMaterial : 
                PropDatabase.Instance.defaultHighlightMaterial;
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
                    // 【关键】如果是古树，强制设为绿色；否则设为黄色
                    Color highlightColor = isAncientTree ? Color.green : Color.yellow;
                    
                    if(outlineInstance.HasProperty("_OutlineColor"))
                        outlineInstance.SetColor("_OutlineColor", highlightColor);
                    else if(outlineInstance.HasProperty("_BaseColor")) // 兼容不同 Shader
                        outlineInstance.SetColor("_BaseColor", highlightColor);
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

    public void SetHighlight(bool active)
    {
        if (allLODRenderers == null) return;

        // 获取本地玩家身份
        var localPlayer = NetworkClient.localPlayer?.GetComponent<GamePlayer>();
        bool isWitch = localPlayer != null && localPlayer.playerRole == PlayerRole.Witch;

        // 判定逻辑：
        // 女巫看到高亮的情况：准星正指着 (active) OR 已经被发现 (isScouted)
        // 猎人看到高亮的情况：仅准星正指着 (active)
        bool shouldShow = active || (isWitch && isScouted);

        if (isHighlighted == shouldShow) 
        {
            // 状态没变时，如果是女巫且高亮着，刷新一次属性（防止从 active 切换到 permanent 时颜色没变）
            if (shouldShow && isWitch) UpdateColorAndZTest(active);
            return;
        }

        isHighlighted = shouldShow;
        
        // 应用材质球切换
        for (int i = 0; i < allLODRenderers.Length; i++)
        {
            var renderer = allLODRenderers[i];
            if (renderer == null) continue;
            renderer.materials = shouldShow ? highlightedMaterialsList[i] : originalMaterialsList[i];
        }

        if (shouldShow) UpdateColorAndZTest(active);
    }

    // public void SetHighlight(bool active)
    // {
    //     // 【增强安全检查】
    //     if (allLODRenderers == null || 
    //         highlightedMaterialsList.Count != allLODRenderers.Length || 
    //         originalMaterialsList.Count != allLODRenderers.Length) 
    //     {
    //         return;
    //     }
        
    //     if (isHighlighted == active) return;
    //     isHighlighted = active;

    //     for (int i = 0; i < allLODRenderers.Length; i++)
    //     {
    //         var renderer = allLODRenderers[i];
    //         // 即使渲染器没激活也要切换材质，否则 LOD 切换后显示的是旧材质
    //         if (renderer == null) continue;
            
    //         // 现在这里绝对不会报错了，因为列表长度是强制对齐的
    //         renderer.materials = active ? highlightedMaterialsList[i] : originalMaterialsList[i];
    //     }
    // }
    void OnDestroy()
    {
        if (outlineInstance != null) Destroy(outlineInstance);
    }
    // 当服务器同步侦察状态时，通知本地女巫刷新视觉
    void OnScoutedChanged(bool oldVal, bool newVal)
    {
        // 获取本地玩家并通知 TeamVision 刷新
        var localPlayer = NetworkClient.localPlayer?.GetComponent<GamePlayer>();
        if (localPlayer != null && localPlayer.playerRole == PlayerRole.Witch)
        {
            localPlayer.GetComponent<TeamVision>()?.ForceUpdateVisuals();
        }
    }

    private void UpdateColorAndZTest(bool isActiveByCrosshair)
    {
        if (outlineInstance == null) return;

        Color finalColor = Color.yellow;
        float zTestMode = 8f; // 默认为 Always (穿透)
        float outlineWidth = 0.03f; // 默认宽度（对应你Shader里的默认值）

        if (isAncientTree)
        {
            // ================= 古树逻辑 =================
            finalColor = Color.green;
            zTestMode = 8f; // 常驻穿透，方便女巫远距离看到目标
            outlineWidth = 0.05f;  // 古树可以稍微加粗，显示重要性
        }
        else
        {
            // ================= 普通树逻辑 =================
            if (isActiveByCrosshair && !isScouted)
            {
                // 正在被检视，但还没完成
                finalColor = Color.yellow;
                zTestMode = 8f; // 检视时穿透，方便看清轮廓
                outlineWidth = 0.03f;
            }
            else if (isScouted)
            {
                // 检视完成：普通树常驻
                // 方案：亮银色 (R:0.8, G:0.8, B:1.0) 比灰色显眼得多
                finalColor = new Color(0.8f, 0.8f, 1.0f, 1.0f); 
                
                // 不穿透透视
                zTestMode = 4f; 
                
                // 【关键点】加粗轮廓！因为不透视，加粗可以防止被细小的枝叶完全盖住
                outlineWidth = 0.06f; 
            }
        }

        // 设置 Shader 参数
        outlineInstance.SetColor("_OutlineColor", finalColor);
        outlineInstance.SetFloat("_ZTestMode", zTestMode);
        // 动态修改轮廓粗细
        outlineInstance.SetFloat("_OutlineWidth", outlineWidth);
    }
}