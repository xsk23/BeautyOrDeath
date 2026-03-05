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

    [Header("全图追踪设置")]
    public float globalTrackTime = 3f; // 前t秒即使找不到也会往大致方向跑
    public float directionNoiseAngle = 20f; // 大致方向的角度偏移

    [Header("视觉设置")]
    public int segments = 50;        // 圆的平滑度（段数）
    public float lineWidth = 0.2f;   // 线条宽度
    public Color circleColor = Color.red; // 线条颜色

    public float stoppingDistance = 2.0f;

    private CreatureMover mover;
    private bool hasFoundWitch = false;
    private Transform targetWitch;
    
    // LineRenderer 引用
    private LineRenderer lineRenderer;

    private float trackTimer = 0f;
    private float updateDirTimer = 0f;
    private Vector3 trackDirection;

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

        trackTimer += Time.deltaTime;

        if (targetWitch != null)
        {
            // 在侦测范围内找到了！精确追踪
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
            // 没找到，检查是否在 t 秒内执行大致方向追踪
            if (trackTimer <= globalTrackTime)
            {
                updateDirTimer -= Time.deltaTime;
                // 每隔1秒计算一次带噪音的方向，防止目标移动过快丢失
                if (updateDirTimer <= 0f)
                {
                    Transform nearestWitch = GetNearestWitchGlobal();
                    if (nearestWitch != null)
                    {
                        Vector3 dir = (nearestWitch.position - transform.position).normalized;
                        dir.y = 0;
                        if (dir == Vector3.zero) dir = transform.forward;
                        
                        // 加入噪音偏移
                        float noise = UnityEngine.Random.Range(-directionNoiseAngle, directionNoiseAngle);
                        trackDirection = Quaternion.Euler(0, noise, 0) * dir;
                    }
                    else
                    {
                        trackDirection = transform.forward; // 场上没女巫时直走
                    }
                    updateDirTimer = 1.0f; // 重置1秒倒计时
                }
                
                // 朝着带噪音的大致方向跑并看向那里
                lookTarget = transform.position + trackDirection * 5f;
                inputAxis = new Vector2(0, 1f);
                isRun = true;
            }
            else
            {
                // 时间到了也没找到，就普通的往前走
                lookTarget = transform.position + transform.forward * 5f;
                inputAxis = new Vector2(0, 1f); 
                isRun = true;
            }
        }

        mover.SetInput(inputAxis, lookTarget, isRun, false);
    }

    [Server]
    private Transform GetNearestWitchGlobal()
    {
        float minDist = float.MaxValue;
        Transform bestTarget = null;
        foreach (var player in GamePlayer.AllPlayers)
        {
            if (player is WitchPlayer witch && !witch.isPermanentDead && !witch.isInvulnerable)
            {
                float d = Vector3.Distance(transform.position, witch.transform.position);
                if (d < minDist)
                {
                    minDist = d;
                    bestTarget = witch.transform;
                }
            }
        }
        return bestTarget;
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
        AudioManager.Instance?.Play3D("dogBarking", pos);
        Debug.Log("Dog: Bark! Found Witch!");
    }

    private void SetupLineRenderer()
    {
        lineRenderer.useWorldSpace = true; // 使用世界坐标，防止狗歪了圈也歪了
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.positionCount = segments + 1; // +1 是为了闭合圆
        lineRenderer.loop = true;
        
        // 设置材质颜色 
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
            float x = Mathf.Sin(Mathf.Deg2Rad * angle) * detectRadius;
            float z = Mathf.Cos(Mathf.Deg2Rad * angle) * detectRadius;

            // 以狗的中心为原点，加上偏移量
            Vector3 pos = new Vector3(x, 0.2f, z) + transform.position;

            lineRenderer.SetPosition(i, pos);

            angle += angleStep;
        }
    }
}