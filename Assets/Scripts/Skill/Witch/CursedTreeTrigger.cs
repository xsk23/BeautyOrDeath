using UnityEngine;
using Mirror;

public class CursedTreeTrigger : NetworkBehaviour
{
    [SyncVar] public uint casterNetId;

    // 当这棵树受到伤害时调用 (需要修改 WeaponBase 或 GunWeapon 来检测这个组件)
    [Server]
    public void OnHitByHunter(HunterPlayer hunter)
    {
        // 触发惩罚：致盲 hunter
        hunter.TargetBlindEffect(hunter.connectionToClient, 3f);
        
        // 触发后移除诅咒 (是一次性的)
        Destroy(this);
    }
}