using UnityEngine;
using Mirror;
using System.Diagnostics;

public class WitchPlayer : GamePlayer
{
    [Header("Witch Skill Settings")]
    public float interactionDistance = 5f; 
    public LayerMask propLayer; 
    public float revertLongPressTime = 1.5f; // 长按多久恢复原状

    private PropTarget currentFocusProp; // 当前聚焦的道具物体
    private MeshFilter myMeshFilter;
    private Renderer myRenderer;
    public GameObject HideGroup;//隐藏物体组
    private MeshCollider myMeshCollider;//玩家身上的网格碰撞器

    // --- 还原备份数据 ---
    private Mesh originalMesh;
    private Material[] originalMaterials;
    private Vector3 originalScale;
    private float originalCCHeight;
    private float originalCCRadius;
    private Vector3 originalCCCenter;
    private float lmbHoldTimer = 0f; // 左键按住计时器

    private void Awake()
    {
        goalText = "Get Your Own Tree And Assemble at the Gates!";
        myMeshFilter = GetComponentInChildren<MeshFilter>();
        myRenderer = GetComponentInChildren<Renderer>();

        // 1. 备份初始人类数据
        if (myMeshFilter != null) originalMesh = myMeshFilter.sharedMesh;
        if (myRenderer != null)
        {
            originalMaterials = myRenderer.sharedMaterials;
            originalScale = myRenderer.transform.localScale;
            
            myMeshCollider = myRenderer.GetComponent<MeshCollider>();
            if (myMeshCollider == null)
                myMeshCollider = myRenderer.gameObject.AddComponent<MeshCollider>();
            myMeshCollider.enabled = false; 
        }

        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            originalCCHeight = cc.height;
            originalCCRadius = cc.radius;
            originalCCCenter = cc.center;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        moveSpeed = 5f;
        // mouseSensitivity = 2f;
        manaRegenRate = 5f;
    }

    public override void Update()
    {
        base.Update();
        if (!isLocalPlayer) return;

        // 如果正在聊天或暂停，不处理交互
        if (isChatting || Cursor.lockState != CursorLockMode.Locked) return;

        HandleInteraction();
        HandleMorphInput();
    }

    public override void HandleInput()
    {
        
    }

    // 处理射线检测和高亮
    private void HandleInteraction()
    {
        Ray ray;
        if (sceneScript != null && sceneScript.Crosshair != null)
        {
            ray = Camera.main.ScreenPointToRay(sceneScript.Crosshair.transform.position);
        }
        else
        {
            ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        }

        RaycastHit hit;
        PropTarget hitProp = null;

        // 1. 射线检测
        if (Physics.Raycast(ray, out hit, interactionDistance, propLayer))
        {
            // 只有打中带 PropTarget 的物体才算有效
            hitProp = hit.collider.GetComponentInParent<PropTarget>();
        }

        // 2. 状态切换逻辑
        if (hitProp != currentFocusProp)
        {
            // 取消旧物体的光效
            if (currentFocusProp != null)
            {
                currentFocusProp.SetHighlight(false);
            }

            // 赋值新物体
            currentFocusProp = hitProp;

            // 开启新物体的光效
            if (currentFocusProp != null)
            {
                currentFocusProp.SetHighlight(true);
            }
        }
    }

    // 处理变身输入
    private void HandleMorphInput()
    {
        // --- 处理左键按下 ---
        if (Input.GetMouseButton(0))
        {
            lmbHoldTimer += Time.deltaTime;
            
            // 只有在变身状态下长按，才更新 UI
            if (isMorphed)
            {
                float progress = Mathf.Clamp01(lmbHoldTimer / revertLongPressTime);
                if (progress > 0.1f)
                {
                    if (sceneScript != null)
                    {
                        // 显示并更新进度条
                        sceneScript.UpdateRevertUI(progress, true);
                    }

                    if (lmbHoldTimer >= revertLongPressTime)
                    {
                        UnityEngine.Debug.Log("Long press complete: Reverting...");
                        lmbHoldTimer = 0f;
                        
                        if (sceneScript != null) sceneScript.UpdateRevertUI(0, false); // 完成后立刻隐藏
                        
                        isMorphed = false; 
                        CmdRevert();
                    }                    
                }

            }
        }

        // --- 处理左键松开 ---
        if (Input.GetMouseButtonUp(0))
        {
            // 只要松开手，立刻隐藏进度条
            if (sceneScript != null)
            {
                sceneScript.UpdateRevertUI(0, false);
            }

            // 短按逻辑：变身
            if (lmbHoldTimer > 0.01f && lmbHoldTimer < 0.3f && !isMorphed && currentFocusProp != null)
            {
                CmdMorph(currentFocusProp.propID);
            }

            lmbHoldTimer = 0f; 
        }
    }

    // ----------------------------------------------------
    // 网络同步：变身
    // ----------------------------------------------------

    [Command]
    private void CmdMorph(int propID)
    {
        // 1. 先在服务器修改同步变量
        isMorphed = true; 
        // 2. 广播 Rpc 处理视觉
        RpcMorph(propID);
    }

    [ClientRpc]
    private void RpcMorph(int propID)
    {
        if (PropDatabase.Instance != null)
        {
            Mesh mesh;
            Material[] mats;
            Vector3 scale;
            
            // 调用更新后的获取数据方法
            if (PropDatabase.Instance.GetPropData(propID, out mesh, out mats, out scale))
            {
                isMorphed = true;
                ApplyMorph(mesh, mats, scale);
            }
        }
    }

    // 执行变身 (纯视觉)
    private void ApplyMorph(Mesh mesh, Material[] mats, Vector3 targetScale)
    {
        if (myMeshFilter == null || myRenderer == null) return;

        myMeshFilter.mesh = mesh;
        myRenderer.materials = mats;
        myRenderer.transform.localScale = targetScale;

        if (HideGroup != null) HideGroup.SetActive(false);
        
        // 【进阶可选】如果你希望碰撞盒也随之改变大小：
        UpdateCollider(mesh, targetScale);

        // 关键：如果你变身时没有更换 Renderer 节点，只需要调用这个
        var outline = GetComponent<PlayerOutline>();
        if (outline != null)
        {
            // 方案 A：如果变身后的模型还是同一个 Renderer 挂载点
            // 我们在第一步改了 SetOutline 逻辑，这里甚至不需要手动调用，TeamVision 下一秒会自动补上
            
            // 方案 B：为了立刻生效，不闪烁，手动刷一下
            outline.RefreshRenderer(myRenderer); 
        }
    }

    // ----------------------------------------------------
    // 网络同步：恢复原状
    // ----------------------------------------------------
    [Command]
    private void CmdRevert() {
        isMorphed = false; // 服务器修改
        RpcRevert(); 
    }

    [ClientRpc]
    private void RpcRevert()
    {
        isMorphed = false;
        ApplyRevert();
    }
    private void ApplyRevert()
    {
        if (myMeshFilter == null || myRenderer == null) return;

        // 1. 恢复模型和材质
        myMeshFilter.mesh = originalMesh;
        myRenderer.materials = originalMaterials;
        myRenderer.transform.localScale = originalScale;


        // 2. 显示人类隐藏组件
        if (HideGroup != null) HideGroup.SetActive(true);

        // 3. 恢复 CharacterController 参数
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            cc.height = originalCCHeight;
            cc.radius = originalCCRadius;
            cc.center = originalCCCenter;
            cc.enabled = true;
        }

        // 4. 禁用精准 MeshCollider (人类形态通常用 CC)
        if (myMeshCollider != null) myMeshCollider.enabled = false;

        // 关键：如果你变身时没有更换 Renderer 节点，只需要调用这个
        var outline = GetComponent<PlayerOutline>();
        if (outline != null)
        {
            // 方案 A：如果变身后的模型还是同一个 Renderer 挂载点
            // 我们在第一步改了 SetOutline 逻辑，这里甚至不需要手动调用，TeamVision 下一秒会自动补上
            
            // 方案 B：为了立刻生效，不闪烁，手动刷一下
            outline.RefreshRenderer(myRenderer); 
        }
    }

    private void UpdateCollider(Mesh mesh, Vector3 scale)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc == null) return;

        // 1. 暂时禁用 CC 以便安全修改属性
        cc.enabled = false;

        float newHeight = mesh.bounds.size.y * scale.y;

        // 2. 将 CharacterController 设为一个“细长柱子”
        // 半径设为极小（比如 0.05），防止它在移动时因为栅栏太长而卡住墙壁
        cc.radius = 0.05f; 
        cc.height = newHeight;
        cc.center = new Vector3(0, newHeight * 0.5f, 0);
        cc.stepOffset = Mathf.Min(0.3f, cc.height * 0.4f);

        // 3. 更新 MeshCollider
        if (myMeshCollider != null)
        {
            myMeshCollider.enabled = false; // 切换 Mesh 时先禁用
            myMeshCollider.sharedMesh = mesh;
            
            // 必须设为 Convex，否则移动物体上的 MeshCollider 不会起作用
            myMeshCollider.convex = true; 
            
            // 如果你希望猎人通过 Raycast (射线) 攻击，此项开启
            // 如果你希望猎人通过触发器检测，此项设为 true
            myMeshCollider.isTrigger = false; 

            myMeshCollider.enabled = true;
        }

        // 4. 微调位置防止入地
        transform.position += Vector3.up * 0.1f;
        cc.enabled = true;
    }


    // 重写基类的抽象方法
    protected override void Attack()
    {
        // 这里是服务器端运行的代码 (因为被 CmdAttack 调用)
        // Debug.Log($"<color=purple>【女巫】{playerName} 释放了技能：扔毒药！</color>");
        UnityEngine.Debug.Log($"<color=purple>[Witch] {playerName} used skill: Throw Poison!</color>");
        
        // 在这里写具体的实例化药水逻辑...
        // GameObject potion = Instantiate(potionPrefab, ...);
        // NetworkServer.Spawn(potion);
    }

}