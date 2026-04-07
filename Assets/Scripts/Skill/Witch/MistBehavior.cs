using UnityEngine;
using Mirror;
using System.Collections.Generic; // 引入字典

public class MistBehavior : NetworkBehaviour
{
    [Header("迷雾设置")]
    public float lifeTime = 5.0f;       // 迷雾存在时间
    public float blindRefreshRate = 1f; // 致盲刷新频率
    public float blindDuration = 2f;    // 单次致盲持续时间

    // 【核心修复】：为每个进入迷雾的猎人独立记录上次被眩晕的时间
    // uint 是玩家的 Network ID，float 是上次被攻击的时间
    private Dictionary<uint, float> hunterHitTimers = new Dictionary<uint, float>();

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true; 
        }
    }

    public override void OnStartServer()
    {
        Destroy(gameObject, lifeTime);
    }

    [ServerCallback]
    private void OnTriggerStay(Collider other)
    {
        // 1. 先判断碰到的到底是不是猎人
        HunterPlayer hunter = other.GetComponent<HunterPlayer>() ?? other.GetComponentInParent<HunterPlayer>();
        
        // 如果碰到的不是猎人（比如地面、树、女巫自己），直接忽略，不干扰计时器
        if (hunter == null) return;

        // 2. 判断该猎人是否处于冷却中
        uint hunterId = hunter.netId;
        if (hunterHitTimers.TryGetValue(hunterId, out float lastHitTime))
        {
            // 如果距离上次该猎人被眩晕还没过 0.5 秒，跳过
            if (Time.time < lastHitTime + blindRefreshRate) return;
        }

        // 3. 执行真正的眩晕/致盲逻辑
        if (!hunter.isPermanentDead && !hunter.isInvulnerable)
        {
            if (hunter.connectionToClient != null)
            {
                hunter.TargetBlindEffect(hunter.connectionToClient, blindDuration);
                Debug.Log($"[Mist] Blinding Hunter: {hunter.playerName}");
                
                // 4. 记录该猎人本次被眩晕的时间
                hunterHitTimers[hunterId] = Time.time;
            }
        }
    }
}