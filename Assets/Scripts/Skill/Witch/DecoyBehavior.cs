using UnityEngine;
using Mirror;

[RequireComponent(typeof(CharacterController))]
public class DecoyBehavior : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float lifeTime = 10f; // 分身存活时间
    public float moveSpeed = 5f; // 移动速度（最好和女巫走路速度一致）
    public float gravity = -9.81f; // 重力

    [Header("Sync Settings")]
    [SyncVar(hook = nameof(OnPropIDChanged))]
    public int propID = -1;

    // 内部变量
    private CharacterController cc;
    private Vector3 moveDir;
    private float verticalVelocity; // 垂直速度（处理重力）
    private float jitterTimer = 0f; // 随机转向计时器

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    public override void OnStartServer()
    {
        // 服务器端初始化
        // 初始方向：就是生成的朝向
        moveDir = transform.forward;
        
        // 销毁计时
        Destroy(gameObject, lifeTime);
    }

    [ServerCallback]
    private void Update()
    {
        if (cc == null) return;

        // 1. 处理随机转向 (模拟玩家的不规则移动)
        jitterTimer += Time.deltaTime;
        if (jitterTimer > 1.0f) // 每秒可能微调一次方向
        {
            // 随机偏转 -45 到 45 度，模拟玩家转弯
            float jitter = Random.Range(-45f, 45f);
            Quaternion turn = Quaternion.AngleAxis(jitter, Vector3.up);
            moveDir = turn * moveDir;
            jitterTimer = 0;
        }

        // 2. 处理重力
        if (cc.isGrounded && verticalVelocity < 0)
        {   
            verticalVelocity = -2f; // 贴地力
        }
        else
        {   
            Debug.Log("Applying gravity");
            verticalVelocity += gravity * Time.deltaTime;
        }

        // 3. 最终移动向量
        // 水平速度
        Vector3 finalMove = moveDir.normalized * moveSpeed;
        // 叠加垂直速度
        finalMove.y = verticalVelocity;

        // 4. 执行移动 (利用 CharacterController 的碰撞处理)
        cc.Move(finalMove * Time.deltaTime);

        // 5. 让模型朝向移动方向
        // 只取水平方向，防止分身朝向地面或天空
        Vector3 faceDir = new Vector3(moveDir.x, 0, moveDir.z);
        if (faceDir != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(faceDir), Time.deltaTime * 5f);
        }
    }

    // --- 视觉同步逻辑 (保持不变) ---
    void OnPropIDChanged(int oldID, int newID)
    {
        // 先清空旧模型 (如果有)
        foreach (Transform child in transform) {
            Destroy(child.gameObject);
        }

        if (PropDatabase.Instance != null && PropDatabase.Instance.GetPropPrefab(newID, out GameObject prefab))
        {
            GameObject visual = Instantiate(prefab, transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            
            // 禁用碰撞体，防止分身卡住自己
            foreach(var c in visual.GetComponentsInChildren<Collider>()) c.enabled = false;

            // 初始化 PropTarget 用于被猎人选中
            // 这里的 propID 和 visual 传进去，确保猎人射线打中分身能高亮
            var pt = GetComponent<PropTarget>();
            if (pt != null) pt.ManualInit(newID, visual);

            UpdateColliderDimensions(visual);
             
        }
    }

    // 【新增】辅助方法：调整碰撞体大小
    private void UpdateColliderDimensions(GameObject visualModel)
    {
        if (cc == null) cc = GetComponent<CharacterController>();
        
        // 尝试获取渲染器边界
        Renderer r = visualModel.GetComponentInChildren<Renderer>();
        if (r != null)
        {
            Bounds bounds = r.bounds;
            // 将世界坐标的 Bounds 转为相对于自身的高度
            float objectHeight = bounds.size.y;
            
            // 调整 CC 参数
            cc.height = objectHeight;
            cc.radius = Mathf.Min(bounds.size.x, bounds.size.z) * 0.4f; // 半径取宽度的40%
            cc.center = new Vector3(0, objectHeight * 0.5f, 0); // 中心上移一半高度
        }
        else
        {
            // 如果没变身（人形态），恢复默认值
            cc.height = 2.0f;
            cc.radius = 0.5f;
            cc.center = new Vector3(0, 1f, 0);
        }
    }
}