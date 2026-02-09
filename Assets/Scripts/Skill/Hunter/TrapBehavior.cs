using UnityEngine;
using Mirror;
using System.Collections; // 确保这个 using 存在
public class TrapBehavior : NetworkBehaviour
{
    [Header("视觉/高亮设置")]
    public PlayerOutline outlineScript; 
    public Color hunterHighlightColor = new Color(0.5f, 0f, 0f);

    [Header("设置")]
    public float destroyDelay = 1.0f;

    [SyncVar(hook = nameof(OnTriggeredChanged))]
    private bool isTriggered = false;

    private Renderer myRenderer;

    private void Awake()
    {
        myRenderer = GetComponent<Renderer>();
    }

    public override void OnStartClient()
    {
        RefreshVisibility();
    }

    private void OnTriggeredChanged(bool oldVal, bool newVal)
    {
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        GamePlayer localPlayer = NetworkClient.localPlayer?.GetComponent<GamePlayer>();
        if (localPlayer == null) return;

        bool isHunter = (localPlayer.playerRole == PlayerRole.Hunter);

        if (isTriggered)
        {
            // 触发后：全员可见，但关闭描边高亮
            if (myRenderer) myRenderer.enabled = true;
            if (outlineScript) outlineScript.SetOutline(false, Color.clear);
        }
        else
        {
            // 未触发：猎人可见并带描边，女巫不可见
            if (myRenderer) myRenderer.enabled = isHunter;

            if (isHunter && outlineScript)
            {
                outlineScript.SetOutline(true, hunterHighlightColor);
            }
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
            // witch.ServerGetTrapped(); 
            // 【关键修改】调用带参数的方法，把自己的 netId 传给女巫
            witch.ServerGetTrappedByTrap(this.netId); 
            if (witch.isMorphed)
            {
                witch.isMorphed = false;
                witch.morphedPropID = -1;
            }
            
            // 触发后销毁
            StartCoroutine(DestroyAfterDelay());
        }
    }

    [Server]
    private System.Collections.IEnumerator DestroyAfterDelay()
    {
        yield return new WaitForSeconds(destroyDelay);
        NetworkServer.Destroy(gameObject);
    }
}