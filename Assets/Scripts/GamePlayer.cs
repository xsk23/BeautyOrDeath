using UnityEngine;
using Mirror;
using TMPro;

public enum PlayerRole
{
    None,
    Witch,
    Hunter
}

// 抽象基类：不能直接挂载，必须由 Witch 或 Hunter 继承
public abstract class GamePlayer : NetworkBehaviour
{
    [Header("组件")]
    [SerializeField] protected CharacterController controller;
    [SerializeField] protected TextMeshPro nameText; // 头顶名字

    [Header("同步属性")]
    [SyncVar(hook = nameof(OnNameChanged))] public string playerName;
    [SyncVar] public float currentHealth = 100f;

    [Header("移动参数")]
    public float moveSpeed = 6f;
    public float gravity = -9.81f;
    
    private Vector3 velocity;
    // 场景脚本引用
    private SceneScript sceneScript;
    // 鼠标锁定状态
    private bool isCursorLocked = true;
    // 在类字段区域新增或修改
    private bool isFirstPerson = true;           // 默认第一人称

    // 【抽象方法】强制子类必须实现 Attack
    protected abstract void Attack();

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
        }
        // 1. 绑定摄像机到头部
        Camera.main.transform.SetParent(transform);
        Camera.main.transform.localPosition = new Vector3(0,0.529999971f,0.170000002f); // 假设眼睛高度
        Camera.main.transform.localRotation = Quaternion.identity;
        
        // 2. 锁定鼠标
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        // 只有本地玩家能控制移动
        if (!isLocalPlayer) return;
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
        HandleMovement();
        HandleInput();
    }
    // 新增方法：根据视角更新相机位置
    private void UpdateCameraView()
    {
        if (isFirstPerson)
        {
            // 第一人称
            Camera.main.transform.localPosition = new Vector3(0, 0.53f, 0.17f);
            Camera.main.transform.localRotation = Quaternion.identity;

        }
        else
        {
            //position:Vector3(0,2,-3.26999998)
            //rotation:Vector3(20,0,0)
            // 第三人称（你注释里提到的位置，可自行微调）
            Camera.main.transform.localPosition = new Vector3(0, 2f, -3.27f);
            Camera.main.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            // 可选：让相机朝向玩家
            // Camera.main.transform.LookAt(transform.position + Vector3.up * 1.2f);
        }
    }
    protected virtual void HandleMovement()
    {
        // 获取输入
        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");

        // 计算移动方向（基于角色当前朝向）
        Vector3 move = transform.right * x + transform.forward * z;
        controller.Move(move * moveSpeed * Time.deltaTime);

        // 重力处理
        if (controller.isGrounded && velocity.y < 0) velocity.y = -2f;
        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);

        // 鼠标旋转视角 (左右旋转角色)
        float mouseX = Input.GetAxis("Mouse X") * 2f;
        transform.Rotate(Vector3.up * mouseX);
        
        // (上下旋转摄像机通常需要限制角度，这里简化略过，直接用 Camera 代码处理或后续添加)
    }

    protected virtual void HandleInput()
    {
        if (Input.GetMouseButtonDown(0)) // 左键攻击
        {
            CmdAttack();
        }
    }

    // 客户端请求攻击 -> 服务器执行
    [Command]
    public void CmdAttack()
    {
        Attack(); // 调用子类的具体实现
    }

    void OnNameChanged(string oldName, string newName)
    {
        if (nameText != null) nameText.text = newName;
    }
}