using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class NetBullet : MonoBehaviour
{
    [ServerCallback] // 只在服务器运行物理
    private void OnTriggerEnter(Collider other)
    {
        // 当网子碰到 CharacterController 时，other 就是该控制器
        // 使用 GetComponentInParent 是最保险的，因为脚本可能在根部
        GamePlayer target = other.GetComponent<GamePlayer>();
        
        if (target == null)
            target = other.GetComponentInParent<GamePlayer>();

        if (target != null)
        {
            target.ServerGetTrapped();
            UnityEngine.Debug.Log($"[NetBullet] Trapped {target.playerName}");
            Destroy(gameObject); // 抓到后销毁
        }
        // 如果碰到墙壁或地面也销毁
        else if (other.gameObject.layer == LayerMask.NameToLayer("Default"))
        {
             Destroy(gameObject);
        }
    }
}
