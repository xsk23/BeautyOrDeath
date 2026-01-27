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

    [SyncVar] 
    public PlayerRole playerRole = PlayerRole.None;

    [Header("移动参数")]
    public float moveSpeed = 6f;
    public float gravity = -9.81f;
    [Header("Mouse Look")]
    public float mouseSensitivity = 2f;
    float xRotation = 0f;

    public GameObject crosshairUI;
    private Vector3 velocity;
    // 场景脚本引用
    private SceneScript sceneScript;
    // 鼠标锁定状态
    private bool isCursorLocked = true;
    // 在类字段区域新增或修改
    private bool isFirstPerson = true;           // 默认第一人称


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
        // 设置场景 UI 显示角色和名字
        sceneScript = FindObjectOfType<SceneScript>();
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
        }
        xRotation = 0f;
        UpdateCameraView(); // 初始化相机位置

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }


    // --------------------------------------------------------
    // 逻辑循环
    // --------------------------------------------------------
    

    void Update()
    {
        // 只有本地玩家能控制移动
        if (isLocalPlayer)
        {
            // 按 Esc 切换鼠标锁定状态
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                isCursorLocked = !isCursorLocked;
                Cursor.lockState = isCursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !isCursorLocked;   // 解锁时显示鼠标指针
            }
            // 按 T 切换第一人称 / 第三人称
            if (Input.GetKeyDown(KeyCode.T))
            {
                isFirstPerson = !isFirstPerson;
                UpdateCameraView();
            }
            if (Input.GetKeyDown(KeyCode.K)) CmdTakeDamage(10f); // 测试用
            if (Input.GetKeyDown(KeyCode.J)) CmdUseMana(15f);    // 测试用
            HandleMovement();
            HandleInput();         
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
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        Vector3 move = transform.right * x + transform.forward * z;
        controller.Move(move * moveSpeed * Time.deltaTime);

        if (controller.isGrounded && velocity.y < 0) velocity.y = -2f;
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // 鼠标视角
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * 100f * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * 100f * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -80f, 80f);
        Camera.main.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    protected virtual void HandleInput()
    {
        if (Input.GetMouseButtonDown(0)) CmdAttack();
    }


    // --------------------------------------------------------
    // 网络同步与命令
    // --------------------------------------------------------

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


    void OnNameChanged(string oldName, string newName)
    {
        if (nameText != null) nameText.text = newName;
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
}