using UnityEngine;
using Mirror;

public class TrapBehavior : NetworkBehaviour
{
    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        WitchPlayer witch = other.GetComponent<WitchPlayer>() ?? other.GetComponentInParent<WitchPlayer>();
        if (witch != null)
        {
            // 触发效果：定身
            witch.ServerGetTrapped(); // 复用网枪的逻辑
            
            // 显形
            if (witch.isMorphed)
            {
                witch.isMorphed = false;
                witch.morphedPropID = -1;
            }

            // 销毁陷阱
            NetworkServer.Destroy(gameObject);
        }
    }
    
    // 只有猎人能看到 (通过 TeamVision 类似的逻辑，或者简单的 Layer 设置)
    // 这里简单处理：对所有人隐形 (除了 Debug)
    public override void OnStartClient()
    {
        GetComponentInChildren<Renderer>().enabled = false; // 完全隐形
    }
}