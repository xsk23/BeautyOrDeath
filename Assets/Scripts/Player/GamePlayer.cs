using UnityEngine;
using Mirror;
using TMPro;
using System.Collections.Generic;
using kcp2k;

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

    [Header("挣脱设置")]
    public int requiredClicks = 2; // 需要按多少次空格才能挣脱
    public float maxTrapTime = 6.0f; // 6秒后还没挣脱就释放

    [SyncVar]
    public int currentClicks = 0; // 当前挣扎次数
    private float trapTimer = 0f;// 计时器

    [Header("同步属性")]
    [SyncVar(hook = nameof(OnStunChanged))]
    public bool isStunned = false; // 是否被禁锢
    [SyncVar(hook = nameof(OnNameChanged))] 
    public string playerName;
    [SyncVar(hook = nameof(OnHealthChanged))]// 血量变化钩子
    public float currentHealth = 100f;
    [SyncVar(hook = nameof(OnMaxHealthChanged))]
    public float maxHealth = 100f;
    public float manaRegenRate = 5f;
    [SyncVar(hook = nameof(OnManaChanged))]
    public float currentMana = 100f;
    [SyncVar(hook = nameof(OnMaxManaChanged))]
    public float maxMana = 100f;

    [SyncVar(hook = nameof(OnMorphChanged))] 
    public bool isMorphed = false; // 当前是否处于变身状态 
    [SyncVar(hook = nameof(OnMorphedPropIDChanged))]
    public int morphedPropID = -1; // -1 表示没变身，>=0 表示对应的 PropID

    [SyncVar] 
    public PlayerRole playerRole = PlayerRole.None;

    [SyncVar(hook = nameof(OnSecondChanceChanged))]
    public bool isInSecondChance = false; // 是否在小动物逃跑状态

    [SyncVar(hook = nameof(OnPermanentDeadChanged))]
    public bool isPermanentDead = false; // 是否永久死亡
    [SyncVar]
    public bool isInvulnerable = false; // 是否无敌

    [Header("移动参数")]
    [SyncVar(hook = nameof(OnMoveSpeedChanged))] // 添加 SyncVar 和钩子
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
        // 【核心修复】服务器启动时也加入列表
        if (!AllPlayers.Contains(this)) AllPlayers.Add(this);
        if (this is WitchPlayer) playerRole = PlayerRole.Witch;
        else if (this is HunterPlayer) playerRole = PlayerRole.Hunter;
        else playerRole = PlayerRole.None;
    }
    public override void OnStopServer()
    {
        // 【核心修复】服务器断开时移除
        if (AllPlayers.Contains(this)) AllPlayers.Remove(this);
        base.OnStopServer();
    }
    // 客户端初始化
    public override void OnStartClient()
    {
        base.OnStartClient();
        // 加入全局列表
        if (!AllPlayers.Contains(this)) AllPlayers.Add(this);
        // 只要有新玩家加入，刷新计数
        RefreshSceneUI();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        // 移除全局列表
        if (AllPlayers.Contains(this)) AllPlayers.Remove(this);
        // 只要有玩家离开，刷新计数
        RefreshSceneUI();
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
            //硬直或禁锢下禁止移动
            if (isStunned)
            {
                if (Input.GetKeyDown(KeyCode.Space))
                {
                    CmdStruggle(); // 发送挣扎命令
                }
                return; // 阻止移动
            }

            // 按 T 切换第一人称 / 第三人称
            if (Input.GetKeyDown(KeyCode.T))
            {
                isFirstPerson = !isFirstPerson;
                UpdateCameraView();
            }

            // 【修改】始终调用 HandleMovement，在方法内部判断是否处理输入
            // 这样即使 Cursor 解锁了，重力代码依然会运行
            // HandleMovement();
            Vector2 input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            HandleMovementOverride(input);


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
    public void UpdateCameraView()
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

    // 将原来的 HandleMovement 改名为 HandleMovementOverride 并接受参数
    protected virtual void HandleMovementOverride(Vector2 inputOverride)
    {
        // ... 原有代码 ...
        // 唯一的区别是：把里面所有的 Input.GetAxis("Horizontal") 替换为 inputOverride.x
        // 把 Input.GetAxis("Vertical") 替换为 inputOverride.y
        

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
        x = inputOverride.x; 
        z = inputOverride.y;
        
        // if (!isInputLocked) { x = Input.GetAxis("Horizontal"); z = Input.GetAxis("Vertical"); }
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

    // 虚方法，让女巫类去实现具体的变身逻辑
    protected virtual void HandleDeath()
    {
        // 默认死亡逻辑（比如猎人被打死，暂时直接重置或出局）
        isPermanentDead = true;
        UnityEngine.Debug.Log($"{playerName} has died.");
    }


    private void RefreshSceneUI()
    {
        // 尝试寻找场景脚本并刷新
        SceneScript ss = FindObjectOfType<SceneScript>();
        if (ss != null)
        {
            ss.UpdateAlivePlayerCount();
        }
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

    // 计时器逻辑修改
    [ServerCallback]
    void LateUpdate()
    {
        if (isStunned)
        {
            trapTimer += Time.deltaTime;

            // ★ 修改点：超时 = 自动释放 (而不是处决)
            if (trapTimer >= maxTrapTime)
            {
                ServerEscape();
            }
        }
    }
    // 服务器端兜网抓住
    [Server]
    public void ServerGetTrapped()
    {
        if (isStunned) return; // 已经被抓了就不重复抓
        isStunned = true; // 继承基类的禁止移动
        trapTimer = 0f;
        currentClicks = 0;

        Debug.Log("被抓住了！开始计时！");
    }

    // 客户端按空格 -> 呼叫服务器
    [Command]
    void CmdStruggle()
    {
        currentClicks++;

        // 判定：点击次数够了 -> 成功挣脱
        if (currentClicks >= requiredClicks)
        {
            ServerEscape();
        }
    }

    [Server]
    void ServerEscape()
    {
        isStunned = false;
        Debug.Log("成功挣脱！");
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

    // 受伤函数
    [Server]
    public void ServerTakeDamage(float amount)
    {
        // 如果无敌或永久死亡，不处理伤害
        if (isInvulnerable || isPermanentDead) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        //改成英文debug
        Debug.Log($"{playerName} took {amount} damage, current health: {currentHealth}");
        if (currentHealth <= 0)
        {
            HandleDeath();
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
    void OnStunChanged(bool oldValue, bool newValue)
    {
        // 可以在这里添加被禁锢时的视觉效果或音效
        if (newValue)
        {
            Debug.Log($"{playerName} is stunned!");
        }
        else
        {
            Debug.Log($"{playerName} is no longer stunned!");
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

    // 建议添加一个钩子函数用于调试（可选）
    protected virtual void OnMoveSpeedChanged(float oldSpeed, float newSpeed)
    {
        // 可以在这里打印日志查看速度是否真的同步过来了
        // Debug.Log($"Speed synced: {newSpeed}");
    }

    protected virtual void OnMorphedPropIDChanged(int oldID, int newID)
    {
        // 这个钩子在所有客户端运行（包括新加入的）
        // 子类 WitchPlayer 会重写这个逻辑
    }

    // 增加一个钩子方便客户端处理 UI（比如显示“快跑！”）
    protected virtual void OnSecondChanceChanged(bool oldVal, bool newVal) { }
    // 添加虚方法供子类重写
    protected virtual void OnPermanentDeadChanged(bool oldVal, bool newVal)
    {
        if (newVal)
        {
            // 通用的死亡逻辑（隐藏名字等）
            if (nameText != null) nameText.gameObject.SetActive(false);
        }
        // 只要有人永久死亡，刷新计数
        RefreshSceneUI();
    }

    protected void OnMaxHealthChanged(float oldValue, float newValue)
    {
        if (isLocalPlayer && sceneScript != null)
        {
            sceneScript.HealthSlider.maxValue = newValue;
        }
    }
    protected void OnMaxManaChanged(float oldValue, float newValue)
    {
        if (isLocalPlayer && sceneScript != null)
        {
            sceneScript.ManaSlider.maxValue = newValue;
        }
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