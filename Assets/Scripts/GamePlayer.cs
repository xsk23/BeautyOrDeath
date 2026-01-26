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

    // 【抽象方法】强制子类必须实现 Attack
    protected abstract void Attack();

    // 当本地玩家控制这个物体时调用
    public override void OnStartLocalPlayer()
    {
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

        HandleMovement();
        HandleInput();
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