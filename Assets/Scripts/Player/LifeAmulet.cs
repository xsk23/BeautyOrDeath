using UnityEngine;
using Mirror;

public class LifeAmulet : WitchItemBase
{
    [Header("护符设置")]
    public float protectionWindow = 30f; // 激活后持续30秒有效

    private bool hasUsed = false; // 记录本局是否已经使用过
    private void Awake()
    {
        itemName = "Life Amulet";
        isActive = true;
        cooldown = 999f;
    }
    public override void OnActivate()
    {
        // 1. 检查是否已经使用过
        if (hasUsed)
        {
            Debug.Log("生命护符本局已失效。");
            return;
        }

        // 2. 获取女巫组件
        WitchPlayer player = GetComponentInParent<WitchPlayer>();
        if (player == null) return;

        // 3. 检查女巫当前状态（如果是幽灵或小动物复活赛状态，通常不能用）
        if (player.isPermanentDead || player.isInSecondChance) return;

        // 4. 发送命令激活
        player.CmdActivateAmulet(protectionWindow);

        // 5. 标记为已使用
        hasUsed = true;

        // 更新冷却（虽然只能用一次，但为了防止连点，还是设置一下）
        UpdateCooldown();
    }
}