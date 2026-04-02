using UnityEngine;
using Mirror;

public class HoneyPuddleBehavior : NetworkBehaviour
{
  public float slowAmount = 0.5f; // 减速到 50%

  [ServerCallback]
  private void OnTriggerStay(Collider other)
  {
    // 只有服务器处理逻辑
    WitchPlayer witch = other.GetComponent<WitchPlayer>() ?? other.GetComponentInParent<WitchPlayer>();

    if (witch != null && !witch.isPermanentDead && !witch.isInvulnerable)
    {
      // 施加减速：持续时间给 0.2 秒，只要站在里面就会一直刷新
      witch.ServerApplySlow(slowAmount, 0.2f);
    }
  }
}