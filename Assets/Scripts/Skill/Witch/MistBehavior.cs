using UnityEngine;
using Mirror;

public class MistBehavior : NetworkBehaviour
{
    [Header("迷雾设置")]
    public float lifeTime = 5.0f;       // 迷雾存在时间
    public float blindRefreshRate = 0.5f; // 致盲刷新频率（每0.5秒刷新一次致盲状态）
    public float blindDuration = 1.0f;    // 单次致盲持续时间（离开迷雾后多久恢复）

    private float nextCheckTime = 0f;

    public override void OnStartServer()
    {
        // 服务器端负责销毁
        Destroy(gameObject, lifeTime);
    }

    [ServerCallback]
    private void OnTriggerStay(Collider other)
    {
        // 性能优化：限制检测频率
        if (Time.time < nextCheckTime) return;

        // 获取目标
        HunterPlayer hunter = other.GetComponent<HunterPlayer>() ?? other.GetComponentInParent<HunterPlayer>();

        if (hunter != null)
        {
            // 确保猎人活着且没有无敌
            if (!hunter.isPermanentDead && !hunter.isInvulnerable)
            {
                // 获取连接并发送致盲 RPC
                // 注意：TargetBlindEffect 已经在 HunterPlayer.cs 中定义好了
                if (hunter.connectionToClient != null)
                {
                    hunter.TargetBlindEffect(hunter.connectionToClient, blindDuration);
                    Debug.Log($"[Mist] Blinding Hunter: {hunter.playerName}");
                }
            }
        }
        
        // 重置检测计时器（简单的频率限制，防止每帧调用 RPC 导致带宽爆炸）
        nextCheckTime = Time.time + blindRefreshRate;
    }
}