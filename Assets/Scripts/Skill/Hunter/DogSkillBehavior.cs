using UnityEngine;
using Mirror;
using Controller; 

// 【新增】自动添加 LineRenderer 组件
[RequireComponent(typeof(CreatureMover))]
[RequireComponent(typeof(LineRenderer))] 
public class DogSkillBehavior : NetworkBehaviour
{
    [Header("设置")]
    public float detectRadius = 15f; // 检测半径
    public LayerMask targetLayer;    // 目标层级
    public float lifeTime = 10f;     // 存活时间

    [Header("视觉设置")]
    public int segments = 50;        // 圆的平滑度（段数）
    public float lineWidth = 0.2f;   // 线条宽度
    public Color circleColor = Color.red; // 线条颜色

    public float stoppingDistance = 2.0f;

    private CreatureMover mover;
    private bool hasFoundWitch = false;
    private Transform targetWitch;
    
    // 【新增】LineRenderer 引用
    private LineRenderer lineRenderer;

    private void Awake()
    {
        mover = GetComponent<CreatureMover>();
        lineRenderer = GetComponent<LineRenderer>();
        
        // 初始化 LineRenderer 样式
        SetupLineRenderer();
    }

    public override void OnStartServer()
    {
        // 服务器负责销毁
        Destroy(gameObject, lifeTime);
    }

    private void Update()
    {
        // --- 服务器逻辑：负责跑路和检测 ---
        if (isServer)
        {
            ServerUpdateLogic();
        }

        // --- 客户端逻辑：负责画圈圈 ---
        // 只要是客户端（包括 Host 主机）都执行
        if (isClient) 
        {
            DrawDetectionCircle();
        }
    }

    // 将原来的 Update 逻辑提取出来，保持整洁
    [Server]
    private void ServerUpdateLogic()
    {
        if (mover == null) return;

        if (!hasFoundWitch)
        {
            DetectWitch();
        }

        Vector2 inputAxis = Vector2.zero;
        Vector3 lookTarget = transform.position + transform.forward * 5f; 
        bool isRun = false;

        if (targetWitch != null)
        {
            float dist = Vector3.Distance(transform.position, targetWitch.position);
            lookTarget = targetWitch.position;

            if (dist > stoppingDistance) 
            {
                inputAxis = new Vector2(0, 1f); 
                isRun = true;
            }
            else
            {
                inputAxis = Vector2.zero;
                isRun = false;
                
                if(!hasFoundWitch) 
                {
                   hasFoundWitch = true; 
                   RpcBarkEffect(targetWitch.position);
                }
            }
        }
        else
        {
            inputAxis = new Vector2(0, 1f); 
            isRun = true;
        }

        mover.SetInput(inputAxis, lookTarget, isRun, false);
    }

    [Server]
    void DetectWitch()
    {
        // 使用 OverlapSphere 检测周围
        Collider[] hits = Physics.OverlapSphere(transform.position, detectRadius, targetLayer);
        float minDist = float.MaxValue;
        Transform bestTarget = null;

        foreach (var hit in hits)
        {
            // 使用 GetComponentInParent 防止遗漏
            WitchPlayer witch = hit.GetComponent<WitchPlayer>() ?? hit.GetComponentInParent<WitchPlayer>();
            
            if (witch != null && !witch.isPermanentDead && !witch.isInvulnerable)
            {
                float d = Vector3.Distance(transform.position, witch.transform.position);
                if (d < minDist)
                {
                    minDist = d;
                    bestTarget = witch.transform;
                }
            }
        }
        
        if (bestTarget != null)
        {
            targetWitch = bestTarget;
        }
    }

    [ClientRpc]
    void RpcBarkEffect(Vector3 pos)
    {
        // 这里可以播放音效
        Debug.Log("Dog: Bark! Found Witch!");
    }

    // =========================================================
    // 【新增】画圈圈的核心逻辑
    // =========================================================
    
    private void SetupLineRenderer()
    {
        lineRenderer.useWorldSpace = true; // 使用世界坐标，防止狗歪了圈也歪了
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.positionCount = segments + 1; // +1 是为了闭合圆
        lineRenderer.loop = true;
        
        // 设置材质颜色 (如果没有材质，可能会显示粉色方块，后面步骤会教你设置)
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = circleColor;
        lineRenderer.endColor = circleColor;
        
        // 禁用阴影，纯视觉
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
    }

    private void DrawDetectionCircle()
    {
        if (lineRenderer == null) return;

        float angle = 0f;
        float angleStep = 360f / segments;

        for (int i = 0; i < segments + 1; i++)
        {
            // 1. 计算圆周上的点 (局部坐标)
            // x = sin(angle) * r
            // z = cos(angle) * r
            float x = Mathf.Sin(Mathf.Deg2Rad * angle) * detectRadius;
            float z = Mathf.Cos(Mathf.Deg2Rad * angle) * detectRadius;

            // 2. 转换为世界坐标
            // 以狗的中心为原点，加上偏移量
            // Y 轴设为 transform.position.y + 0.2f，稍微离地一点点，防止和地面穿插（Z-Fighting）
            Vector3 pos = new Vector3(x, 0.2f, z) + transform.position;

            lineRenderer.SetPosition(i, pos);

            angle += angleStep;
        }
    }
}