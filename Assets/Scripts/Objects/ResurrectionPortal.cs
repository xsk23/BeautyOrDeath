using UnityEngine;
using Mirror;

public class ResurrectionPortal : MonoBehaviour 
{
    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        WitchPlayer witch = other.GetComponentInParent<WitchPlayer>();
        if (witch == null) return;

        // 逻辑 A：原有的小动物复活
        if (witch.isInSecondChance && !witch.isPermanentDead)
        {
            witch.ServerRevive();
        }

        // 逻辑 B：【新增】检测带回古树
        // 只有驾驶员 (possessedTreeNetId != 0) 且还没完成过任务的能触发
        if (witch.possessedTreeNetId != 0 && !witch.hasDeliveredTree)
        {
            UnityEngine.Debug.Log($"[Server] Driver {witch.playerName} reached the portal with a tree!");
            witch.ServerOnReachPortal();
        }
    }

}