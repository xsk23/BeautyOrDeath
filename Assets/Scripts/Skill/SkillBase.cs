using UnityEngine;
using Mirror;

public abstract class SkillBase : NetworkBehaviour
{
    [Header("Skill Settings")]
    public string skillName;
    // public Sprite icon;
    public float cooldownTime = 5f;
    public float manaCost = 20f;
    public KeyCode triggerKey;

    
    
    [SyncVar]
    private double lastUseTime;

    protected GamePlayer ownerPlayer;

    public float CooldownRatio
    {
        get
        {
            float duration = (float)(NetworkTime.time - lastUseTime);
            if (duration >= cooldownTime) return 0f;
            return 1f - (duration / cooldownTime);
        }
    }

    public bool IsReady => (NetworkTime.time - lastUseTime) >= cooldownTime;

    public void Init(GamePlayer player)
    {
        ownerPlayer = player;
        lastUseTime = -cooldownTime; // 初始就绪
    }

    // 客户端尝试释放技能
    public void TryCast()
    {
        if (IsReady && ownerPlayer.currentMana >= manaCost)
        {
            CmdCast();
        }
        else if (ownerPlayer.currentMana < manaCost)
        {
            Debug.Log("<color=red>Mana not enough!</color>");
        }
    }

    [Command]
    private void CmdCast()
    {
        if (!IsReady || ownerPlayer.currentMana < manaCost) return;
        
        // 扣除法力
        ownerPlayer.currentMana -= manaCost;
        
        // 记录时间 (NetworkTime 用于同步)
        lastUseTime = NetworkTime.time;

        // 执行具体逻辑
        OnCast();
    }

    // 子类实现具体的技能逻辑 (服务器端执行)
    protected abstract void OnCast();

    [Server]
    public void ServerReduceCooldown(float timeToReduce)
    {
        // 如果技能本身就已经冷却好了，直接跳过
        if (IsReady) return;

        // 把“上一次使用的时间”往更早的过去推移
        lastUseTime -= (double)timeToReduce;

        // 防溢出保护：如果推得太早，导致算出来的冷却时间大于基础冷却，直接把它设为刚好冷却完毕
        if (NetworkTime.time - lastUseTime > cooldownTime)
        {
            lastUseTime = NetworkTime.time - cooldownTime;
        }
    }

}