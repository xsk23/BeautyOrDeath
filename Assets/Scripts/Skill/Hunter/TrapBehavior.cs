using UnityEngine;
using Mirror;
using System.Collections;

[RequireComponent(typeof(Rigidbody))] // 确保有刚体
public class TrapBehavior : NetworkBehaviour
{
    [Header("视觉/高亮设置")]
    public PlayerOutline outlineScriptopen; 
    public PlayerOutline outlineScriptclose; 
    public Color hunterHighlightColor = new Color(0.5f, 0f, 0f);

    [Header("模型切换设置")]
    public GameObject openModel;   
    public GameObject closedModel; 
    public Animator trapAnimator;  

    [Header("设置")]
    public float destroyDelay = 5.0f; 

    [SyncVar(hook = nameof(OnTriggeredChanged))]
    public bool isTriggered = false;

    private Renderer[] myRenderers;
    private Rigidbody rb; // 引用刚体

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        myRenderers = GetComponentsInChildren<Renderer>(true);
        
        // 初始化刚体状态
        if (rb != null)
        {
            rb.isKinematic = true; // 建议放置时先设为 Kinematic，防止被撞飞
            rb.useGravity = false;
        }

        if (openModel != null)
        {
            openModel.SetActive(true);
            Collider childCol = openModel.GetComponent<Collider>();
            if (childCol != null)
            {
                childCol.isTrigger = true;
                if (childCol is MeshCollider meshCol) meshCol.convex = true; 
            }
        }

        if (closedModel != null) closedModel.SetActive(false);
    }

    public override void OnStartClient()
    {
        UpdateModelState(isTriggered);
        RefreshVisibility();
    }

    private void OnTriggeredChanged(bool oldVal, bool newVal)
    {
        UpdateModelState(newVal);
        RefreshVisibility();
    }

    private void UpdateModelState(bool triggered)
    {
        if (openModel != null) openModel.SetActive(!triggered);
        if (closedModel != null) 
        {
            bool wasActive = closedModel.activeSelf;
            closedModel.SetActive(triggered);
            if (triggered && !wasActive && trapAnimator != null)
            {
                trapAnimator.SetTrigger("Snap");
            }
        }
    }

    private void RefreshVisibility()
    {
        GamePlayer localPlayer = NetworkClient.localPlayer?.GetComponent<GamePlayer>();
        if (localPlayer == null) return;
        bool isHunter = (localPlayer.playerRole == PlayerRole.Hunter);
        foreach (var r in myRenderers) r.enabled = isTriggered || isHunter;

        if (outlineScriptopen)
        {
            if (isTriggered) outlineScriptopen.SetOutline(false, Color.clear);
            else if (isHunter) outlineScriptopen.SetOutline(true, hunterHighlightColor);
        }
        if (outlineScriptclose)
        {
            if (!isTriggered) outlineScriptclose.SetOutline(false, Color.clear);
            else if (isHunter) outlineScriptclose.SetOutline(true, hunterHighlightColor);
        }
    }

    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        if (isTriggered) return;

        WitchPlayer witch = other.GetComponent<WitchPlayer>() ?? other.GetComponentInParent<WitchPlayer>();
        
        if (witch != null && !witch.isPermanentDead && !witch.isInvulnerable)
        {
            isTriggered = true; 

            // --- 物理层面移动方案 ---
            Vector3 targetPos = witch.transform.position;

            // 1. 先把刚体设为 Kinematic，这样它就不会被物理引擎推走或卡住
            rb.isKinematic = true; 
            
            // 2. 使用 rb.position 强制更改物理坐标
            rb.position = targetPos;
            
            // 3. 同时更改 transform.position (双重保险)
            transform.position = targetPos;

            // 4. 调用 ClientRpc，确保所有客户端立即看到瞬移效果
            RpcSnapToPosition(targetPos);

            // --- 游戏逻辑 ---
            witch.ServerGetTrappedByTrap(this.netId); 
            
            if (witch.isMorphed)
            {
                witch.isMorphed = false;
                witch.morphedPropID = -1;
            }
            StartCoroutine(DestroyAfterDelay());
        }
    }

    // 通过 RPC 强制客户端同步物理位置
    [ClientRpc]
    private void RpcSnapToPosition(Vector3 newPos)
    {
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.position = newPos;
        }
        transform.position = newPos;
    }

    [Server]
    private System.Collections.IEnumerator DestroyAfterDelay()
    {
        yield return new WaitForSeconds(destroyDelay);
        NetworkServer.Destroy(gameObject);
    }
}