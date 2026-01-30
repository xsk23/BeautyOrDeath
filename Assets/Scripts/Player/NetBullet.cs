using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class NetBullet : MonoBehaviour
{
    [HideInInspector] public PlayerRole ownerRole; // 发射者的阵营
    [ServerCallback] // 只在服务器运行物理
    private void OnTriggerEnter(Collider other)
    {
        // 当网子碰到 CharacterController 时，other 就是该控制器
        // 使用 GetComponentInParent 是最保险的，因为脚本可能在根部
        GamePlayer target = other.GetComponent<GamePlayer>() ?? other.GetComponentInParent<GamePlayer>();

        if (target != null)
        {
            // --- 【队友伤害检查逻辑】 ---
            bool isSameTeam = (target.playerRole == ownerRole);
            bool canTrap = !isSameTeam || GameManager.Instance.FriendlyFire;
           if (canTrap)
            {
                target.ServerGetTrapped();
                UnityEngine.Debug.Log($"[NetBullet] Trapped {target.playerName}");
                Destroy(gameObject); // 抓到后销毁
            }
        }
        // 如果碰到墙壁或地面也销毁
        else if (other.gameObject.layer == LayerMask.NameToLayer("Default"))
        {
             Destroy(gameObject);
        }
    }
}
