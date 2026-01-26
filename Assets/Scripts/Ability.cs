using UnityEngine;

public abstract class Ability : ScriptableObject
{
    [Header("技能信息")]
    public string abilityName = "新技能";
    [Min(0)] public float cooldown = 2f;
    public Sprite icon;

    /// <summary>
    /// 执行技能逻辑
    /// 子类重写实现具体功能，如古树鉴定、诅咒、电锯冲刺等
    /// </summary>
    public abstract void Execute(PlayerBase player);
}