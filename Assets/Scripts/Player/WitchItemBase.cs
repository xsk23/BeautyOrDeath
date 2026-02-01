using UnityEngine;
using Mirror;

public abstract class WitchItemBase : NetworkBehaviour
{
    [Header("道具通用设置")]
    public string itemName;
    public Sprite icon; // 用于UI显示
    public float cooldown = 0f; // 冷却时间 (对于被动道具可能为0或无限)

    protected float lastUseTime = -999f;

    // 是否冷却完毕
    public bool IsReady => Time.time >= lastUseTime + cooldown;

    // 道具激活入口 (主动道具用)
    public virtual void OnActivate() { }

    // 道具被动更新 (每帧调用)
    public virtual void OnPassiveUpdate(WitchPlayer witch) { }

    // 标记进入冷却
    protected void StartCooldown()
    {
        lastUseTime = Time.time;
    }
}