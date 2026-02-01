using UnityEngine;
using Mirror;
using System.Diagnostics;
using Controller; // 确保引用了动物控制器的命名空间
using System.Collections.Generic; // 引用 List

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
    [Header("复活赛设置")]
    public int frogPropID = 1; // 假设 PropDatabase 中 ID 1 是青蛙
    public float frogHealth = 20f; // 小动物形态血量

    // ========================================================================
    // 【新增】多人共乘（抢方向盘）核心变量
    // ========================================================================
    [Header("Multi-Witch Control")]
    // 自身携带的 PropTarget 组件，用于变身后让别人瞄准
    private PropTarget myPropTarget; 
    
    // 当前我是谁的乘客？(0 表示自己是独立的)
    [SyncVar(hook = nameof(OnHostNetIdChanged))]
    public uint hostNetId = 0; 
    
    // 只有宿主才用这个列表：记录谁在我的车上
    public readonly SyncList<uint> passengerNetIds = new SyncList<uint>();

    // 宿主用来同步所有乘客的总输入向量 (X, Z)
    [SyncVar]
    private Vector2 combinedPassengerInput; 
    // ========================================================================

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
        // 【新增】给玩家挂载 PropTarget，但默认禁用
        myPropTarget = GetComponent<PropTarget>();
        if (myPropTarget == null) myPropTarget = gameObject.AddComponent<PropTarget>();
        myPropTarget.enabled = false; // 还没变身，不可被当做道具

    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        lastPosition = transform.position;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // moveSpeed = 5f;
        // mouseSensitivity = 2f;
        // manaRegenRate = 5f;
    }

    public override void Update()
    {
        // 如果永久死亡，跳过所有交互逻辑，只保留基础移动（基类 HandleMovement）
        if (isPermanentDead)
        {
            base.Update(); // 允许观察者移动
            return; 
        }
        // =========================================================
        // 【新增】乘客逻辑：如果我是乘客，我不需要跑物理移动
        // =========================================================
        if (isLocalPlayer && hostNetId != 0)
        {
            HandlePassengerLogic();
            HandleMorphInput();     // 【新增】处理长按左键下车 (复用变身输入的进度条逻辑)
            return; // 乘客不执行后续的 base.Update() (不跑物理移动)
        }       
        // =========================================================
        // 【新增】宿主逻辑：更新变身后的 PropTarget 可视状态
        // =========================================================
        if (isMorphed && myPropTarget != null && currentVisualProp != null)
        {
            // 修改前：if (myPropTarget.targetRenderer == null)
            // 修改后：使用我们刚才在 PropTarget 里加的属性
            if (!myPropTarget.IsInitialized)
            {
                myPropTarget.ManualInit(morphedPropID, currentVisualProp);
            }
        }
        // =========================================================
        // 如果变身了，根据按键实时更新基础移动速度
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

        HandleInteraction(); // 只有非乘客才进行射线检测
        HandleMorphInput();  // 处理变身/还原输入


    }

    // =========================================================
    // 【修改】重写 HandleMovementOverride 实现“抢方向盘”
    // =========================================================
    protected override void HandleMovementOverride(Vector2 inputOverride)
    {
        // 1. 获取本地输入 (来自 GamePlayer 传进来的参数)
        Vector2 finalInput = inputOverride;

        // 2. 如果是宿主，叠加乘客输入
        if (passengerNetIds.Count > 0)
        {
            finalInput += combinedPassengerInput; 
            // 限制最大合力，防止速度过快
            finalInput = Vector2.ClampMagnitude(finalInput, 1.2f); 
        }

        // 3. 调用基类逻辑，但此时我们要把叠加后的输入传给基类的计算逻辑
        // 【注意】因为基类的 HandleMovementOverride 内部是拿不到我们这里的 finalInput 的
        // 这是一个递归设计问题。
        // 修正方案：我们不调用 base.HandleMovementOverride，而是复制其逻辑，或者修改基类结构。
        // 鉴于你的 GamePlayer.cs 实现，base.HandleMovementOverride 只是用传入的参数计算。
        
        // 调用基类，传入修改后的 Input
        base.HandleMovementOverride(finalInput);
    }

    // =========================================================
    // 【新增】乘客逻辑
    // =========================================================
    private void HandlePassengerLogic()
    {
        // 1. 发送输入给宿主
        if (!isChatting && Cursor.lockState == CursorLockMode.Locked)
        {
            float x = Input.GetAxis("Horizontal");
            float z = Input.GetAxis("Vertical");
            if (Mathf.Abs(x) > 0.01f || Mathf.Abs(z) > 0.01f)
            {
                CmdSendInputToHost(new Vector2(x, z));
            }
            else
            {
                CmdSendInputToHost(Vector2.zero);
            }
        }

        // 2. 视角跟随宿主
        if (NetworkClient.spawned.TryGetValue(hostNetId, out NetworkIdentity hostIdentity))
        {
            // 强制将我的位置设置在宿主位置（防止网络剔除问题）
            transform.position = hostIdentity.transform.position;
            
            // 相机跟随
            Camera.main.transform.SetParent(null); // 解除父子关系防止跟随旋转晕车
            // 简单的第三人称跟随
            Vector3 targetPos = hostIdentity.transform.position + Vector3.up * 2f - hostIdentity.transform.forward * 4f;
            Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, targetPos, Time.deltaTime * 10f);
            Camera.main.transform.LookAt(hostIdentity.transform.position + Vector3.up * 1f);
        }

        // // 3. 处理退出 (空格键跳车)
        // if (Input.GetKeyDown(KeyCode.Space))
        // {
        //     CmdLeaveHost();
        // }
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
        if (isInSecondChance) return; // 复活赛期间锁死形态，不能通过长按左键恢复
        // 定义当前状态
        bool isPassenger = hostNetId != 0; // 是否是乘客
        bool isHost = isMorphed && !isPassenger; // 是否是宿主
        // --- 处理左键按下 ---
        if (Input.GetMouseButton(0))
        {
            lmbHoldTimer += Time.deltaTime;
            
            // 【修改】如果是 变身状态(Host) 或者 乘客状态(Passenger)，都显示进度条
            if (isHost || isPassenger)
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
                        UnityEngine.Debug.Log("Long press complete.");
                        lmbHoldTimer = 0f;
                        
                        if (sceneScript != null) sceneScript.UpdateRevertUI(0, false); 
                        
                        // 【核心分支】
                        if (isPassenger)
                        {
                            // 乘客长按 -> 下车
                            CmdLeaveHost();
                        }
                        else if (isHost)
                        {
                            // 宿主长按 -> 变回人形
                            CmdRevert();
                        }
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
            // 【注意】乘客不能触发短按变身，必须是非乘客 (!isPassenger)
            if (!isPassenger && lmbHoldTimer > 0.01f && lmbHoldTimer < 0.3f && !isMorphed && currentFocusProp != null)
            {
                // 检查这个 PropTarget 是否属于另一个 WitchPlayer
                WitchPlayer otherWitch = currentFocusProp.GetComponent<WitchPlayer>();
                if (otherWitch != null && otherWitch != this)
                {
                    // 加入它！
                    CmdJoinWitch(otherWitch.netId);
                }
                else
                {
                    // 普通变身
                    CmdMorph(currentFocusProp.propID);
                }
            }

            lmbHoldTimer = 0f; 
        }
    }

    // ----------------------------------------------------
    // 网络同步：变身
    // ----------------------------------------------------

    [Command]
    private void CmdJoinWitch(uint targetNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity targetIdentity)) return;
        
        WitchPlayer targetWitch = targetIdentity.GetComponent<WitchPlayer>();
        if (targetWitch == null || !targetWitch.isMorphed) return; // 只能加入已变身的女巫

        // 1. 设置状态
        hostNetId = targetNetId;
        
        // 2. 通知宿主添加乘客
        targetWitch.ServerAddPassenger(netId);

        // 3. 隐藏我自己
        RpcSetVisible(false);
    }

    [Command]
    private void CmdLeaveHost()
    {
        if (hostNetId == 0) return;

        if (NetworkServer.spawned.TryGetValue(hostNetId, out NetworkIdentity hostIdentity))
        {
            WitchPlayer hostWitch = hostIdentity.GetComponent<WitchPlayer>();
            if (hostWitch != null)
            {
                hostWitch.ServerRemovePassenger(netId);
            }
        }

        hostNetId = 0;
        RpcSetVisible(true);
        
        // 稍微弹开一点，防止卡住
        transform.position += Vector3.up + transform.forward;
    }

    [Command]
    private void CmdSendInputToHost(Vector2 input)
    {
        // 只有乘客才能发
        if (hostNetId == 0) return;
        
        // 找到宿主并更新
        if (NetworkServer.spawned.TryGetValue(hostNetId, out NetworkIdentity hostIdentity))
        {
            WitchPlayer hostWitch = hostIdentity.GetComponent<WitchPlayer>();
            if (hostWitch != null)
            {
                hostWitch.ServerUpdatePassengerInput(netId, input);
            }
        }
    }



    [Command]
    private void CmdMorph(int propID)
    {
        // // 1. 先在服务器修改同步变量
        isMorphed = true; 
        // // 2. 广播 Rpc 处理视觉
        // RpcMorph(propID);
        morphedPropID = propID; // 修改 SyncVar，自动触发所有人的钩子
    }

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

            // 8. 【新增】启用我的 PropTarget，允许别人瞄准我变身后的模型
            myPropTarget.enabled = true;
            // 修改这一行调用：传入整个 GameObject 而不是单个 Renderer
            myPropTarget.ManualInit(propID, currentVisualProp); 
            gameObject.layer = LayerMask.NameToLayer("Prop"); // 确保层级能被射线打到
        }
    }


    // =========================================================
    // 宿主专用服务器逻辑
    // =========================================================
    
    // 缓存每个乘客的当前帧输入 <netId, input>
    private Dictionary<uint, Vector2> passengerInputs = new Dictionary<uint, Vector2>();

    [Server]
    public void ServerAddPassenger(uint pid)
    {
        if (!passengerNetIds.Contains(pid))
        {
            passengerNetIds.Add(pid);
            passengerInputs[pid] = Vector2.zero;
        }
    }

    [Server]
    public void ServerRemovePassenger(uint pid)
    {
        if (passengerNetIds.Contains(pid))
        {
            passengerNetIds.Remove(pid);
            passengerInputs.Remove(pid);
            RecalculateCombinedInput();
        }
    }

    [Server]
    public void ServerUpdatePassengerInput(uint pid, Vector2 input)
    {
        if (passengerNetIds.Contains(pid))
        {
            passengerInputs[pid] = input;
            RecalculateCombinedInput();
        }
    }

    [Server]
    private void RecalculateCombinedInput()
    {
        Vector2 sum = Vector2.zero;
        foreach (var kvp in passengerInputs)
        {
            sum += kvp.Value;
        }
        combinedPassengerInput = sum; // 更新 SyncVar，所有客户端都会收到最新的合力
    }

    // =========================================================
    // 视觉处理
    // =========================================================

    [ClientRpc]
    private void RpcSetVisible(bool visible)
    {
        // 调用上面的本地方法
        SetLocalVisibility(visible);
    }
    
    // 钩子：当宿主ID变化时（乘客端执行）
    void OnHostNetIdChanged(uint oldId, uint newId)
    {
        if (isLocalPlayer)
        {
            if (newId != 0)
            {
                // 刚上车
                if (sceneScript != null && sceneScript.RunText != null)
                {
                    sceneScript.RunText.gameObject.SetActive(true);
                    sceneScript.RunText.text = "Press WASD to help move!\nPress SPACE to exit!";
                }
            }
            else
            {
                // 刚下车
                if (sceneScript != null && sceneScript.RunText != null)
                    sceneScript.RunText.gameObject.SetActive(false);
                
                // 恢复摄像机
                UpdateCameraView(); 
            }
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
        // 【关键修改】在变回人形之前，先在服务器端强制把所有乘客踢下去
        ServerKickAllPassengers();

        isMorphed = false; 
        morphedPropID = -1; // 这会触发客户端的视觉 Hook (ApplyRevert)
    }

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

        // 【新增修复】关闭 PropTarget 并踢出所有乘客
        if (myPropTarget != null) myPropTarget.enabled = false;
        // =========================================================
        // 【修复层级报错】安全地设置 Layer
        // =========================================================
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer == -1)
        {
            UnityEngine.Debug.LogWarning("Layer 'Player' not found! Defaulting to layer 0.");
            playerLayer = 0; // 如果找不到 Player 层，就设为 Default (0)
        }
        gameObject.layer = playerLayer; 
    }

// 【新增】服务器专用：强制踢出所有乘客
    [Server]
    private void ServerKickAllPassengers()
    {
        // 1. 复制列表，防止遍历时修改集合报错
        List<uint> passengersToKick = new List<uint>(passengerNetIds);
        
        foreach (uint pid in passengersToKick)
        {
            if (NetworkServer.spawned.TryGetValue(pid, out NetworkIdentity pIdentity))
            {
                WitchPlayer pWitch = pIdentity.GetComponent<WitchPlayer>();
                if (pWitch != null)
                {
                    // 修改乘客的 SyncVar，让它知道自己下车了
                    pWitch.hostNetId = 0; 
                    
                    // 恢复乘客的可见性
                    pWitch.RpcSetVisible(true); 
                    
                    // 强制客户端重置状态（位置、摄像机）
                    pWitch.TargetForceLeave(pIdentity.connectionToClient); 
                }
            }
        }
        
        // 2. 清空宿主的乘客列表
        passengerNetIds.Clear();
        combinedPassengerInput = Vector2.zero;
    }

    // 辅助 Rpc：用于强制乘客端重置状态 (可选，增加鲁棒性)
    [TargetRpc]
    public void TargetForceLeave(NetworkConnection target)
    {
        // 1. 恢复显示
        SetLocalVisibility(true);

        // 2. 计算一个随机的弹射方向 (水平圆周上随机一点)
        // Random.insideUnitCircle 返回半径为1的圆内随机点，我们稍微处理一下保证有一定距离
        Vector2 randomCircle = Random.insideUnitCircle.normalized * 1.5f; // 1.5米半径散开
        Vector3 ejectOffset = new Vector3(randomCircle.x, 0.5f, randomCircle.y);

        // 3. 应用位置偏移 (宿主位置 + 随机水平偏移 + 向上偏移防穿地)
        // 注意：transform.position 此时已经是宿主的位置了（因为Update里一直在同步）
        transform.position += ejectOffset;

        // 4. 重置摄像机
        UpdateCameraView();
        
        // 5. [可选] 如果有速度变量，建议重置，防止落地滑行
        // velocity = Vector3.zero; // 如果 velocity 是 protected/public 的话可以加这行
    }

    // 【新增】本地辅助方法：只负责改状态，不涉及网络通信
    private void SetLocalVisibility(bool visible)
    {
        // 隐藏/显示所有渲染器
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            r.enabled = visible;
        }
        // 如果有 CharacterController 也需要处理，防止隐形碰撞
        if (controller != null) controller.enabled = visible;
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
    protected override void HandleDeath()
    {
        if (!isInSecondChance)
        {
            // --- 第一次死亡：进入复活赛 ---
            UnityEngine.Debug.Log($"{playerName} entered second chance mode!");
            isInSecondChance = true;
            
            // 恢复少量血量供逃跑
            currentHealth = frogHealth;
            
            // 强制变身为小动物
            morphedPropID = frogPropID;
            isMorphed = true;

            // 开启 3 秒无敌（仅在服务器执行）
            if (isServer)
            {
                StartCoroutine(ServerInvulnerabilityRoutine(3.0f));
            }
        }
        else
        {
            // --- 第二次死亡：彻底出局 ---
            UnityEngine.Debug.Log($"{playerName} is permanently dead!");
            isPermanentDead = true;
            // 死亡时确保提示文字消失
            if (isLocalPlayer && sceneScript != null && sceneScript.RunText != null)
                sceneScript.RunText.gameObject.SetActive(false);
        }
    }
    [Server]
    // 服务器端：处决玩家
    public void ServerGetExecuted(float damage)
    {
        // 1. 扣血
        ServerTakeDamage(damage);

        // 2. 强制解除禁锢 
        if (isTrappedByNet)
        {
            isStunned = false;
            isTrappedByNet = false;
            currentClicks = 0; // 重置挣扎次数
            UnityEngine.Debug.Log($"<color=red>{playerName} 被处决并强制释放！</color>");
        }
    }
    // 服务器端无敌协程
    [Server]
    private System.Collections.IEnumerator ServerInvulnerabilityRoutine(float duration)
    {
        isInvulnerable = true;
        UnityEngine.Debug.Log($"{playerName} is now invulnerable for {duration}s");
        
        yield return new WaitForSeconds(duration);
        
        isInvulnerable = false;
        UnityEngine.Debug.Log($"{playerName} is no longer invulnerable");
    }
    protected override void OnSecondChanceChanged(bool oldVal, bool newVal)
    {
        // 只有本地玩家且 SceneScript 存在时处理
        if (isLocalPlayer && sceneScript != null && sceneScript.RunText != null)
        {
            sceneScript.RunText.gameObject.SetActive(newVal);
            if (newVal)
            {
                sceneScript.RunText.text = "<color=red>YOU ARE HURT!</color>\nRUN TO THE PORTAL TO REVIVE!";
            }
        }
    }

    // 服务器端：由传送门调用
    [Server]
    public void ServerRevive()
    {
        if (!isInSecondChance || isPermanentDead) return;
        
        isInSecondChance = false;
        currentHealth = maxHealth;
        morphedPropID = -1; // 变回人类
        isMorphed = false;
        UnityEngine.Debug.Log($"{playerName} has been revived at the portal!");
    }
    protected override void OnPermanentDeadChanged(bool oldVal, bool newVal)
    {
        base.OnPermanentDeadChanged(oldVal, newVal);
        if (newVal)
        {
            SetPermanentDeath();
        }
    }

    private void SetPermanentDeath()
    {
        UnityEngine.Debug.Log($"[Client] {playerName} is now a spectator.");
        moveSpeed = 10f; // 允许观察者快速移动

        // 只有本地玩家且 SceneScript 存在时处理
        if (isLocalPlayer && sceneScript != null && sceneScript.RunText != null)
        {
            sceneScript.RunText.gameObject.SetActive(true);
            // 提示玩家他是观察者（Spectator）用英文写text
            sceneScript.RunText.text = "<color=yellow>You are now a spectator!</color>";
        }
        

        // 所有人不可见：禁用所有渲染器
        // 隐藏人类模型
        if (HideGroup != null) HideGroup.SetActive(false);
        // 隐藏可能存在的动物模型
        if (currentVisualProp != null) currentVisualProp.SetActive(false);
        // 隐藏原始渲染器
        if (myRenderer != null) myRenderer.enabled = false;
        // 隐藏名字
        if (nameText != null) nameText.gameObject.SetActive(false);

        // 2. 禁用交互：修改物理层级
        // 建议在 Unity 中创建一个 Layer 叫 "Spectator"，并在 Physics Matrix 中设置它不与 Player 碰撞
        gameObject.layer = LayerMask.NameToLayer("Ignore Raycast"); 

        // 3. 禁用碰撞体（针对非本地玩家直接禁用 CC）
        if (!isLocalPlayer)
        {
            if (controller != null) controller.enabled = false;
        }
        else
        {
            // 4. 本地玩家：作为观察者逻辑
            // 我们可以让本地玩家依然有碰撞，以便在场景中走动但不卡住别人
            // 或者你可以将 CC 的半径设为 0
            if (controller != null)
            {
                controller.radius = 0.01f; 
            }

            // 提示 UI
            if (sceneScript != null)
            {
                // 假设你在 SceneScript 中有一个提示文本
                // sceneScript.GoalText.text = "<color=red>YOU ARE ELIMINATED (SPECTATING)</color>";
            }
        }
        
        // 5. 确保不再触发变身或还原
        isMorphed = false;
        isMorphedIntoAnimal = false;
    }

}