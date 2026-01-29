using UnityEngine;
using Mirror;
using System.Diagnostics;
using Controller; // 确保引用了动物控制器的命名空间

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
    
    [Header("Morph Animation Settings")]
    public Transform propContainer; // 玩家预制体下的一个空物体，用于装载变身后的模型
    private GameObject currentVisualProp; // 当前生成的动物模型实例
    private Animator propAnimator; // 变身后获取的动画组件引用
    private string currentVerticalParam = "Speed"; // 默认值
    private string currentStateParam = "State"; 
    [Header("Morphed Stats")]
    private float morphedWalkSpeed = 5f;
    private float morphedRunSpeed = 8f;
    private float originalHumanSpeed = 5f; // 备份人类速度
    private bool isMorphedIntoAnimal = false; // 记录当前变身的是否为有动画的动物
    private Vector3 lastPosition;

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

    public override void OnStartClient()
    {
        base.OnStartClient();
        lastPosition = transform.position;
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
        // 1. 如果变身了，根据按键实时更新基础移动速度
        if (isLocalPlayer && isMorphed)
        {
            bool isRunning = Input.GetKey(KeyCode.LeftShift);
            float targetSpeed = isRunning ? morphedRunSpeed : morphedWalkSpeed;

            // 只有当速度发生变化时才发送命令，节省带宽
            if (Mathf.Abs(moveSpeed - targetSpeed) > 0.01f)
            {
                moveSpeed = targetSpeed; // 本地先变，保证手感
                CmdUpdateMoveSpeed(targetSpeed); // 通知服务器变
            }
        }


        base.Update();

        // ----------------------------------------------------------------
        // 【核心修复】计算速度并同步动画参数
        // ----------------------------------------------------------------
        if (isMorphed && isMorphedIntoAnimal && propAnimator != null)
        {
            float speedMagnitude;

            if (isLocalPlayer)
            {
                speedMagnitude = new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
            }
            else
            {
                // 远程玩家：使用位置差推算
                float distance = Vector3.Distance(transform.position, lastPosition);
                speedMagnitude = distance / Time.deltaTime;
                // 只有当距离变化超过一个小阈值才认为在移动，防止抖动
                if (distance < 0.001f) speedMagnitude = 0;
            }

            lastPosition = transform.position;

            // 只要有位移，Vert 就给 1
            float animVert = speedMagnitude > 0.05f ? 1.0f : 0.0f;
            propAnimator.SetFloat(currentVerticalParam, animVert);
            
            // 通过 moveSpeed (SyncVar) 判断远程玩家是否在按 Shift
            bool isRunning = (moveSpeed >= morphedRunSpeed - 0.1f) && speedMagnitude > 0.1f;
            propAnimator.SetFloat(currentStateParam, isRunning ? 1f : 0f);
        }

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
        UnityEngine.Debug.DrawRay(ray.origin, ray.direction * interactionDistance, Color.green);
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
        // // 1. 先在服务器修改同步变量
        isMorphed = true; 
        // // 2. 广播 Rpc 处理视觉
        // RpcMorph(propID);
        morphedPropID = propID; // 修改 SyncVar，自动触发所有人的钩子
    }

    // [ClientRpc]
    // private void RpcMorph(int propID)
    // {
    //     if (PropDatabase.Instance != null)
    //     {
    //         Mesh mesh;
    //         Material[] mats;
    //         Vector3 scale;
            
    //         // 调用更新后的获取数据方法
    //         if (PropDatabase.Instance.GetPropData(propID, out mesh, out mats, out scale))
    //         {
    //             isMorphed = true;
    //             ApplyMorph(mesh, mats, scale);
    //         }
    //     }
    // }

    // 执行变身 (纯视觉)
    // private void ApplyMorph(Mesh mesh, Material[] mats, Vector3 targetScale)
    // {
    //     if (myMeshFilter == null || myRenderer == null) return;

    //     myMeshFilter.mesh = mesh;
    //     myRenderer.materials = mats;
    //     myRenderer.transform.localScale = targetScale;

    //     if (HideGroup != null) HideGroup.SetActive(false);
        
    //     // 【进阶可选】如果你希望碰撞盒也随之改变大小：
    //     UpdateCollider(mesh, targetScale);

    //     // 关键：如果你变身时没有更换 Renderer 节点，只需要调用这个
    //     var outline = GetComponent<PlayerOutline>();
    //     if (outline != null)
    //     {
    //         // 方案 A：如果变身后的模型还是同一个 Renderer 挂载点
    //         // 我们在第一步改了 SetOutline 逻辑，这里甚至不需要手动调用，TeamVision 下一秒会自动补上
            
    //         // 方案 B：为了立刻生效，不闪烁，手动刷一下
    //         outline.RefreshRenderer(myRenderer); 
    //     }
    // }

    // [ClientRpc]
    // private void RpcMorph(int propID)
    // {
    //     // 直接调用新的实例化逻辑
    //     isMorphed = true;
    //     ApplyMorph(propID);
    // }

    private void ApplyMorph(int propID)
    {
        // 1. 清理之前的模型
        if (currentVisualProp != null) Destroy(currentVisualProp);
        
        // 2. 隐藏人类模型
        if (HideGroup != null) HideGroup.SetActive(false);
        if (myRenderer != null) myRenderer.enabled = false;

        // 3. 生成新物体
        if (PropDatabase.Instance.GetPropPrefab(propID, out GameObject prefab))
        {
            // 检查容器是否存在
            if (propContainer == null)
            {
                UnityEngine.Debug.LogError("Prop Container is null! 请在 Inspector 中拖入一个子物体作为容器。");
                return;
            }

            currentVisualProp = Instantiate(prefab, propContainer);
            currentVisualProp.transform.localPosition = Vector3.zero;
            currentVisualProp.transform.localRotation = Quaternion.identity;

            // 【新增逻辑】获取动物原有的控制参数
            var animalMover = currentVisualProp.GetComponent<Controller.CreatureMover>();
            if (animalMover != null)
            {
                // 是动物：使用动物的速度设置
                isMorphedIntoAnimal = true;
                // 获取私有变量的值（如果变量是私有的，请去 CreatureMover.cs 将 m_WalkSpeed 改为 public）
                // 注意：CreatureMover 内部使用了 / 3.6f 转换，我们也需要同步转换以匹配数值
                morphedWalkSpeed = animalMover.m_WalkSpeed;
                morphedRunSpeed = animalMover.m_RunSpeed;
                // 获取动画参数名
                currentVerticalParam = animalMover.m_VerticalID; // 获取 "Vert"
                currentStateParam = animalMover.m_StateID;      // 获取 "State"
            }
            else
            {
                // 不是动物（如石头、树木）：将速度设为原始人类速度
                isMorphedIntoAnimal = false;
                morphedWalkSpeed = originalHumanSpeed;
                morphedRunSpeed = originalHumanSpeed; // 静态物体通常不提供跑步加成，设为一致
                // 如果不是动物（是石头等静态物体），重置回默认或空
                currentVerticalParam = "Speed"; 
            }

            // 4. 【核心修复】禁用脚本但保留动画
            // 遍历 Behaviour 能够同时覆盖 MonoBehaviour 和 Animator
            Behaviour[] allBehaviours = currentVisualProp.GetComponentsInChildren<Behaviour>();
            foreach (var comp in allBehaviours)
            {
                // 如果不是 Animator 且不是渲染器相关，就禁用它（比如禁用移动脚本、输入脚本）
                if (!(comp is Animator) && !(comp is Renderer))
                {
                    comp.enabled = false;
                }
            }

            // 禁用所有物理碰撞器，防止动物自身的碰撞器干扰玩家
            Collider[] allColliders = currentVisualProp.GetComponentsInChildren<Collider>();
            foreach (var c in allColliders) c.enabled = false;

            // 5. 获取并设置 Animator
            propAnimator = currentVisualProp.GetComponent<Animator>();
            
            // 6. 更新玩家自身的 CharacterController 大小
            // 尝试从新模型中找一个渲染器来计算大小
            SkinnedMeshRenderer smr = currentVisualProp.GetComponentInChildren<SkinnedMeshRenderer>();
            MeshFilter mf = currentVisualProp.GetComponentInChildren<MeshFilter>();
            
            Mesh targetMesh = null;
            if (smr != null) targetMesh = smr.sharedMesh;
            else if (mf != null) targetMesh = mf.sharedMesh;

            if (targetMesh != null)
            {
                UpdateCollider(targetMesh, currentVisualProp.transform.localScale);
            }

            // 7. 刷新轮廓
            var outline = GetComponent<PlayerOutline>();
            if (outline != null) outline.RefreshRenderer(currentVisualProp.GetComponentInChildren<Renderer>());
        }
    }

    // ----------------------------------------------------
    // 网络同步：恢复原状
    // ----------------------------------------------------

    // 重写基类的钩子函数
    protected override void OnMorphedPropIDChanged(int oldID, int newID)
    {
        if (newID >= 0)
        {
            isMorphed = true;
            ApplyMorph(newID);
        }
        else
        {
            isMorphed = false;
            ApplyRevert();
        }
    }


    [Command]
    void CmdUpdateMoveSpeed(float newSpeed)
    {
        // 服务器收到命令，修改 SyncVar，随后会自动同步给所有客户端
        moveSpeed = newSpeed;
    }

    [Command]
    private void CmdRevert() {
        isMorphed = false; // 服务器修改
        // RpcRevert(); 
        morphedPropID = -1; // 修改 SyncVar，自动触发所有人的还原
    }

    // [ClientRpc]
    // private void RpcRevert()
    // {
    //     isMorphed = false;
    //     ApplyRevert();
    // }
    private void ApplyRevert()
    {        
        if (currentVisualProp != null) Destroy(currentVisualProp);
        propAnimator = null;

        if (HideGroup != null) HideGroup.SetActive(true);
        if (myRenderer != null) myRenderer.enabled = true;

        // 恢复人类的基础移动速度
        moveSpeed = originalHumanSpeed;
        if (isLocalPlayer) CmdUpdateMoveSpeed(originalHumanSpeed);

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