using UnityEngine;
using Mirror;

public class MagicBroom : WitchItemBase
{
    [Header("魔法扫帚设置")]
    public float doubleJumpForceMultiplier = 2.0f; // 二段跳力度倍率（相对于普通跳跃）
    public void Awake()
    {
        isActive = false;
        itemName = "Magic Broom";
        cooldown = 5f;
    }
    public override void OnActivate()
    {
    }
}