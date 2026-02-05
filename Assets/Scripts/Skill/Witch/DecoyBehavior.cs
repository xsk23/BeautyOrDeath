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
    [Header("Visual References")]
    public GameObject humanVisualRoot; // 在 Inspector 中拖入预制体的人形模型根物体
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
        // 服务器端初始化：如果是人形态，立即校准一次物理中心
        if (propID == -1 && humanVisualRoot != null)
        {
            UpdateColliderDimensions(humanVisualRoot);
        }
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
        // 1. 清理旧的变身模型 (保留人形根物体和FX)
        foreach (Transform child in transform) {
            if (child.gameObject != humanVisualRoot && child.name != "FX")
                Destroy(child.gameObject);
        }

        if (newID == -1)
        {
            // --- 恢复人形态 ---
            if (humanVisualRoot != null)
            {
                humanVisualRoot.SetActive(true);
                UpdateColliderDimensions(humanVisualRoot);
            }
        }
        else
        {
            // --- 变身形态 ---
            if (humanVisualRoot != null) humanVisualRoot.SetActive(false);

            if (PropDatabase.Instance != null && PropDatabase.Instance.GetPropPrefab(newID, out GameObject prefab))
            {
                GameObject visual = Instantiate(prefab, transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                
                foreach(var c in visual.GetComponentsInChildren<Collider>()) c.enabled = false;

                var pt = GetComponent<PropTarget>();
                if (pt != null) pt.ManualInit(newID, visual);

                UpdateColliderDimensions(visual);
            }
        }
    }

    // 【新增】辅助方法：调整碰撞体大小
    private void UpdateColliderDimensions(GameObject visualModel)
    {
        if (cc == null) cc = GetComponent<CharacterController>();

        // 1. 获取模型下所有的渲染器（包括人形态和变身形态）
        Renderer[] rs = visualModel.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;

        // 2. 关键：计算本地坐标系的包围盒
        // 我们要找到模型相对于分身根节点(Pivot)的最高点和最低点
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        float maxSide = 0f;

        bool foundRenderer = false;
        foreach (var r in rs)
        {
            if (r is ParticleSystemRenderer) continue; // 忽略粒子
            
            // 将世界空间的 Bounds 转为本地空间的相对坐标
            // 使用 transform.InverseTransformPoint 确保不受生成位置影响
            Bounds b = r.bounds;
            Vector3 localMin = transform.InverseTransformPoint(b.min);
            Vector3 localMax = transform.InverseTransformPoint(b.max);

            minY = Mathf.Min(minY, localMin.y);
            maxY = Mathf.Max(maxY, localMax.y);
            
            // 计算半径
            float sideX = Mathf.Max(Mathf.Abs(localMin.x), Mathf.Abs(localMax.x));
            float sideZ = Mathf.Max(Mathf.Abs(localMin.z), Mathf.Abs(localMax.z));
            maxSide = Mathf.Max(maxSide, sideX, sideZ);
            
            foundRenderer = true;
        }

        if (!foundRenderer) return;

        // 3. 计算物理参数
        float height = maxY - minY;
        // 中心点应该在 minY 和 maxY 的正中间
        float centerY = (minY + maxY) / 2f;

        // 4. 安全应用参数
        cc.enabled = false; // 修改前必须禁用
        
        cc.height = height;
        cc.center = new Vector3(0, centerY, 0);
        cc.radius = Mathf.Clamp(maxSide, 0.2f, 0.5f); // 限制半径范围
        
        // 自动调整踏步高度，防止小动物卡在小石子上
        cc.stepOffset = Mathf.Min(0.3f, height * 0.3f);

        cc.enabled = true;
        
        Debug.Log($"[Decoy] Adjusting CC: Height={height}, CenterY={centerY}, Morphed={propID != -1}");
    }
}