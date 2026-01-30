using UnityEngine;
using Mirror;

public class ResurrectionPortal : MonoBehaviour 
{
    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        // 只有女巫能复活
        WitchPlayer witch = other.GetComponentInParent<WitchPlayer>();
        
        if (witch != null && witch.isInSecondChance && !witch.isPermanentDead)// 确保女巫处于小动物逃跑状态且未永久死亡
        {
            // 执行复活逻辑
            witch.ServerRevive();
            
            // 可以在这里加一个视觉特效的 Rpc 调用
            // RpcPlayReviveEffect(witch.transform.position);
        }
    }

}