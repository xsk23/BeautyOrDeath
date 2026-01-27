using UnityEngine;

public class WitchPlayer : GamePlayer
{
    // 重写基类的抽象方法
    protected override void Attack()
    {
        // 这里是服务器端运行的代码 (因为被 CmdAttack 调用)
        // Debug.Log($"<color=purple>【女巫】{playerName} 释放了技能：扔毒药！</color>");
        Debug.Log($"<color=purple>[Witch] {playerName} used skill: Throw Poison!</color>");
        
        // 在这里写具体的实例化药水逻辑...
        // GameObject potion = Instantiate(potionPrefab, ...);
        // NetworkServer.Spawn(potion);
    }
    public override void OnStartServer()
    {
        base.OnStartServer();

        moveSpeed = 5f;
        // mouseSensitivity = 2f;
        manaRegenRate = 5f;
    }
}