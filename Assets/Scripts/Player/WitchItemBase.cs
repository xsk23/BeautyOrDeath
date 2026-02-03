using UnityEngine;
using Mirror;

public abstract class WitchItemBase : NetworkBehaviour
{
    [Header("道具通用设置")]
    public string itemName;
    public float cooldown = 0f; // 冷却时间 (对于被动道具可能为0或无限)
    // 内部冷却计时
    public float nextUseTime = 0f;
    // 判断是否冷却完毕
    public bool CanUse()
    {
        return Time.time >= nextUseTime;
    }

    // ★ 抽象方法：具体开火逻辑交给子类实现
    // origin: 射击起点（通常是摄像机位置）
    // direction: 射击方向（通常是摄像机正前方）
    public void UpdateCooldown()
    {
        nextUseTime = Time.time + cooldown;
    }


    // 道具激活入口 (主动道具用)
    public virtual void OnActivate() { }

    // 道具被动更新 (每帧调用)
    public virtual void OnPassiveUpdate(WitchPlayer witch) { }

}