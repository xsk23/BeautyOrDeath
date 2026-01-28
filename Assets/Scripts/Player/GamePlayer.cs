using UnityEngine;
using Mirror;
using TMPro;
using System.Collections.Generic;

public enum PlayerRole
{
    None,
    Witch,
    Hunter
}

// 抽象基类：不能直接挂载，必须由 Witch 或 Hunter 继承
public abstract class GamePlayer : NetworkBehaviour
{
    // ==========================================
    // 静态全局列表：方便 TeamVision 访问所有玩家
    // ==========================================
    public static List<GamePlayer> AllPlayers = new List<GamePlayer>();
    [Header("组件")]
    [SerializeField] protected CharacterController controller;
    [SerializeField] public TextMeshPro nameText; // 头顶名字

    [Header("同步属性")]
    [SyncVar(hook = nameof(OnNameChanged))] public string playerName;
    [SyncVar(hook = nameof(OnHealthChanged))]// 血量变化钩子
    public float currentHealth = 100f;

    public float maxHealth = 100f;

    public float manaRegenRate = 5f;
    [SyncVar(hook = nameof(OnManaChanged))]
    public float currentMana = 100f;
    public float maxMana = 100f;

    [SyncVar(hook = nameof(OnMorphChanged))] 
    public bool isMorphed = false; // 当前是否处于变身状态 

    [SyncVar] 
    public PlayerRole playerRole = PlayerRole.None;

    [Header("移动参数")]
    public float moveSpeed = 6f;
    public float gravity = -9.81f;
    [Header("跳跃参数")]
    public float jumpHeight = 2.0f; // 跳跃高度 (建议改小一点，50太高了会飞出地图)
    public float groundCheckDistance = 1.1f; // 射线长度：胶囊体高度的一半(1.0) + 缓冲(0.1)
    public LayerMask groundLayer; // 地面层级，防止检测到自己
    // 【新增】空中控制力 (0 = 完全无法在空中变向，10 = 空中变向也很灵活)
    // 建议设置为 1.0f 到 5.0f 之间，既有惯性又能微调
    public float airControl = 2.0f; 
    [Header("Mouse Look")]
    public float mouseSensitivity = 2f;
    float xRotation = 0f;

    public GameObject crosshairUI;
    private Vector3 velocity;
    // 场景脚本引用
    public SceneScript sceneScript;
    // 【修改】这里定义一次，子类直接使用，不要在子类重复定义
    [HideInInspector] // 可选：不在Inspector显示，防止乱改
    public string goalText; 
    // 在类字段区域新增或修改
    private bool isFirstPerson = true;           // 默认第一人称


    [Header("Chat State")]
    public bool isChatting = false; // 用于禁止移动
    // 新增一个变量缓存 ChatUI
    private GameChatUI gameChatUI;

    // 【抽象方法】强制子类必须实现 Attack
    protected abstract void Attack();


    // --------------------------------------------------------
    // 生命周期
    // --------------------------------------------------------

    // 服务器初始化角色
    public override void OnStartServer()
    {
        base.OnStartServer();
        if (this is WitchPlayer) playerRole = PlayerRole.Witch;
        else if (this is HunterPlayer) playerRole = PlayerRole.Hunter;
        else playerRole = PlayerRole.None;
    }

    // 客户端初始化
    public override void OnStartClient()
    {
        base.OnStartClient();
        // 加入全局列表
        if (!AllPlayers.Contains(this)) AllPlayers.Add(this);
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        // 移除全局列表
        if (AllPlayers.Contains(this)) AllPlayers.Remove(this);
    }

    // 当本地玩家控制这个物体时调用
    public override void OnStartLocalPlayer()
    {

        // ---------------------------------------------------------
        // 【新增】名字同步逻辑 (仿照 PlayerScript)
        // ---------------------------------------------------------
        if (PlayerSettings.Instance != null && !string.IsNullOrWhiteSpace(PlayerSettings.Instance.PlayerName))
        {
            // 如果本地存了名字，立刻告诉服务器覆盖掉那个默认的 "Hunter (Late)"
            CmdUpdateName(PlayerSettings.Instance.PlayerName);
        }
        else
        {
            // 如果没存名字（极其罕见），就告诉服务器用个随机名或者保持默认
            // CmdUpdateName("Player " + Random.Range(100, 999));
        }
        // ---------------------------------------------------------

        // 设置场景 UI 显示角色和名字
        sceneScript = FindObjectOfType<SceneScript>();
        // 【新增】获取 ChatUI 引用
        gameChatUI = FindObjectOfType<GameChatUI>();
        if (sceneScript != null)
        {
            // 用子类的类名作为角色名（最简单方式）
            string roleName = GetType().Name.Replace("Player", "");
            sceneScript.RoleText.text = $"Role: {roleName}";
            sceneScript.NameText.text = $"Name: {playerName}";
            sceneScript.HealthSlider.maxValue = maxHealth;
            sceneScript.HealthSlider.value = currentHealth;
            sceneScript.ManaSlider.maxValue = maxMana;
            sceneScript.ManaSlider.value = currentMana;
            // 【核心修改】直接使用 goalText，不需要判断类型转换了
            // 因为 goalText 已经在子类的 Awake/Start 中被赋值了
            if (sceneScript.GoalText != null)
            {
                sceneScript.GoalText.text = goalText;
            }
            crosshairUI = sceneScript.Crosshair;
        }
        xRotation = 0f;
        UpdateCameraView(); // 初始化相机位置

        // 【修改】初始锁定鼠标
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }


    // --------------------------------------------------------
    // 逻辑循环
    // --------------------------------------------------------
    

    public virtual void Update()
    {
        // 只有本地玩家能控制移动
        if (isLocalPlayer)
        {
            // 【新增】如果引用为空，尝试再次查找（防空指针）
            if (sceneScript == null) sceneScript = FindObjectOfType<SceneScript>();
            if (gameChatUI == null) gameChatUI = FindObjectOfType<GameChatUI>();

            // 【修改】按下 Esc 呼叫 UI，而不是自己改鼠标状态
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // 优先级 1: 如果聊天正在进行，按 Esc 只是关闭聊天
                if (isChatting) 
                {
                    if (gameChatUI != null) gameChatUI.SetChatState(false);
                }
                // 优先级 2: 如果没在聊天，按 Esc 打开暂停菜单
                else 
                {
                    if (sceneScript != null) sceneScript.TogglePauseMenu();
                }
                // 只要按了 Esc，本帧剩下的逻辑（移动等）就不跑了
                return; 
            }
            // 1. 如果正在聊天，阻止移动
            if (isChatting) 
            {
                return; 
            }
            // 按 T 切换第一人称 / 第三人称
            if (Input.GetKeyDown(KeyCode.T))
            {
                isFirstPerson = !isFirstPerson;
                UpdateCameraView();
            }

            // 【修改】始终调用 HandleMovement，在方法内部判断是否处理输入
            // 这样即使 Cursor 解锁了，重力代码依然会运行
            HandleMovement();

            // 攻击输入还是只有锁定时才允许
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                HandleInput();
            }
            // 测试用输入
            if (Input.GetKeyDown(KeyCode.K)) CmdTakeDamage(10f); // 测试用
            if (Input.GetKeyDown(KeyCode.J)) CmdUseMana(15f);    // 测试用
      
        }
        if (isServer)
        {
            ServerRegenerateMana();
        }
    }

    // --------------------------------------------------------
    // 功能函数
    // --------------------------------------------------------


    // 新增方法：根据视角更新相机位置
    private void UpdateCameraView()
    {
        if (isFirstPerson)
        {
            Camera.main.transform.SetParent(transform);
            Camera.main.transform.localPosition = new Vector3(0, 0.53f, 0.17f);
            Camera.main.transform.localRotation = Quaternion.identity;
        }
        else
        {
            Camera.main.transform.SetParent(transform);
            Camera.main.transform.localPosition = new Vector3(0, 2f, -3.27f);
            Camera.main.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        }
    }

    protected virtual void HandleMovement()
    {
        // 1. 更加精准的状态检测
        // 射线起点稍微高一点（从膝盖位置发射），长度稍微长一点
        float rayLength = (controller.height * 0.5f) + 0.3f; 
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f; 
        bool isHit = Physics.Raycast(rayOrigin, Vector3.down, rayLength, groundLayer);
        
        // 结合 Controller 的状态，防止在斜坡上判定丢失
        bool actuallyOnGround = isHit || controller.isGrounded;

        // 2. 输入锁定
        bool isInputLocked = isChatting || (sceneScript != null && Cursor.lockState != CursorLockMode.Locked);

        // 3. 获取输入方向
        float x = 0f; float z = 0f;
        if (!isInputLocked) { x = Input.GetAxis("Horizontal"); z = Input.GetAxis("Vertical"); }
        Vector3 inputDir = (transform.right * x + transform.forward * z);
        if (inputDir.magnitude > 1f) inputDir.Normalize();

        // 4. 计算目标水平速度
        Vector3 targetVelocity = inputDir * moveSpeed;

        // 5. 【核心修改】找回惯性的速度计算
        // 这里的参数决定了惯性的强弱：
        // groundAccel: 地面启动速度 (越大启动越快)
        // groundDecel: 地面摩擦力 (越大停得越快，设置小一点就有溜冰感)
        float groundAccel = 8f; 
        float groundDecel = 12f; 
        
        // 选择当前的加速度
        float currentAccel;
        if (actuallyOnGround)
        {
            // 如果有输入，用加速度；没输入（想停下来），用摩擦力
            currentAccel = (inputDir.magnitude > 0) ? groundAccel : groundDecel;
        }
        else
        {
            // 空中加速度（airControl），通常很小，产生巨大的惯性
            currentAccel = airControl; 
        }

        // 平滑改变速度 (不再乘以 10f，让变化过程肉眼可见)
        velocity.x = Mathf.MoveTowards(velocity.x, targetVelocity.x, currentAccel * Time.deltaTime * moveSpeed);
        velocity.z = Mathf.MoveTowards(velocity.z, targetVelocity.z, currentAccel * Time.deltaTime * moveSpeed);

        // 6. 重力处理 (修复出生漂浮)
        if (actuallyOnGround && velocity.y < 0)
        {
            // 已经在地面时，保持一个小小的下压力
            velocity.y = -2f; 
        }
        else
        {
            // 只要不在地面，重力就会一直累加，确保哪怕出生在 0.1米高度也会掉下去
            velocity.y += gravity * Time.deltaTime;
        }

        // 7. 跳跃逻辑
        if (actuallyOnGround && !isInputLocked && Input.GetButtonDown("Jump"))
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            actuallyOnGround = false; // 瞬间起跳，脱离地面判定
        }

        // 8. 执行最终移动
        controller.Move(velocity * Time.deltaTime);

        // 9. 旋转视角 (保持不变)
        if (!isInputLocked)
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * 100f * Time.deltaTime;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * 100f * Time.deltaTime;
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -80f, 80f);
            Camera.main.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
            transform.Rotate(Vector3.up * mouseX);
        }

        // 调试射线：绿色代表判定为地面，红色代表空中
        Debug.DrawRay(rayOrigin, Vector3.down * rayLength, actuallyOnGround ? Color.green : Color.red);
    }
    public virtual void HandleInput()
    {
        if (Input.GetMouseButtonDown(0)) CmdAttack();
    }


    // --------------------------------------------------------
    // 网络同步与命令
    // --------------------------------------------------------

    // 【新增】命令：更新名字
    [Command]
    public void CmdUpdateName(string newName)
    {
        // 简单的验证
        if (string.IsNullOrWhiteSpace(newName)) return;
        if (newName.Length > 16) newName = newName.Substring(0, 16);
        
        // 修改 SyncVar，自动同步给所有人
        playerName = newName;
        
        // 服务器日志
        Debug.Log($"[Server] Player {connectionToClient.connectionId} updated name to: {newName}");
    }

    [Command] public void CmdAttack() => Attack();

    [Command]
    public void CmdTakeDamage(float amount)
    {
        currentHealth = Mathf.Max(0, currentHealth - amount);
    }
    [Command]
    public void CmdUseMana(float amount)
    {
        if (currentMana >= amount) currentMana -= amount;
    }
    //自动恢复蓝量的函数
    [Server]
    void ServerRegenerateMana()
    {
        if (currentMana < maxMana)
        {
            currentMana = Mathf.Clamp(currentMana + manaRegenRate * Time.deltaTime, 0, maxMana);
        }
    }


    // Hook 函数：当名字在服务器改变并同步到客户端时调用
    void OnNameChanged(string oldName, string newName)
    {
        // 1. 更新头顶的 3D 文字 (给别人看的)
        if (nameText != null) nameText.text = newName;

        // 2. 【核心修复】如果这是“我自己”，顺便更新左上角的 UI (给自己看的)
        if (isLocalPlayer)
        {
            // 确保引用存在
            if (sceneScript == null) sceneScript = FindObjectOfType<SceneScript>();
            
            if (sceneScript != null)
            {
                sceneScript.NameText.text = $"Name: {newName}";
            }
        }
    }
    void OnHealthChanged(float oldValue, float newValue)
    {
        float percent = newValue / maxHealth;

        if (isLocalPlayer && sceneScript != null)
        {
            sceneScript.HealthSlider.value = newValue;
        }
    }
    void OnManaChanged(float oldValue, float newValue)
    {
        float percent = newValue / maxMana;

        if (isLocalPlayer && sceneScript != null)
        {
            sceneScript.ManaSlider.value = newValue;
        }
    }

    // 增加钩子，当状态改变时通知视觉系统
    void OnMorphChanged(bool oldVal, bool newVal)
    {
        // 强制调用 TeamVision 的刷新逻辑（如果有必要）
        // 或者仅仅依靠 TeamVision 的协程检测
    }

    // ---------------------------------------------------
    // 聊天网络逻辑
    // ---------------------------------------------------
    [Command]
    public void CmdSendGameMessage(string message, ChatChannel channel)
    {
        // 简单防刷校验
        if (string.IsNullOrWhiteSpace(message)) return;
        if (message.Length > 100) message = message.Substring(0, 100);

        // 调用 Rpc 分发给所有客户端
        RpcReceiveGameMessage(playerName, message, channel, playerRole);
    }

    [ClientRpc]
    private void RpcReceiveGameMessage(string senderName, string msg, ChatChannel channel, PlayerRole senderRole)
    {
        // 1. 获取本地玩家
        GamePlayer localPlayer = null;
        foreach (var p in AllPlayers)
        {
            if (p.isLocalPlayer) { localPlayer = p; break; }
        }
        if (localPlayer == null) return;

        // 2. 判断是否应该显示该消息
        bool shouldShow = false;

        if (channel == ChatChannel.All)
        {
            shouldShow = true; // 全局消息谁都看
        }
        else if (channel == ChatChannel.Team)
        {
            // 只有队友或者是发送者自己才看得到
            if (localPlayer.playerRole == senderRole || localPlayer.playerName == senderName)
            {
                shouldShow = true;
            }
        }

        // 3. 显示消息
        if (shouldShow)
        {
            GameChatUI chatUI = FindObjectOfType<GameChatUI>();
            if (chatUI != null)
            {
                // 根据角色决定名字颜色
                Color roleColor = (senderRole == PlayerRole.Witch) ? Color.magenta : 
                                  (senderRole == PlayerRole.Hunter) ? Color.cyan : Color.white;
                
                chatUI.AppendMessage(senderName, msg, channel, roleColor);
            }
        }
    }
}