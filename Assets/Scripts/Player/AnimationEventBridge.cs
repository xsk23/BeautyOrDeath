using UnityEngine;

/// <summary>
/// 动画事件桥接器：挂载在包含 Animator 组件的模型节点上
/// 负责接收动画帧事件，并转发给父节点的核心控制脚本
/// </summary>
[RequireComponent(typeof(Animator))]
public class AnimationEventBridge : MonoBehaviour
{
    private HunterPlayer hunterPlayer;

    void Awake()
    {
        // 自动在父节点中寻找 HunterPlayer 脚本
        hunterPlayer = GetComponentInParent<HunterPlayer>();
        
        if (hunterPlayer == null)
        {
            Debug.LogError("AnimationEventBridge: 在父节点中找不到 HunterPlayer 脚本！");
        }
    }

    /// <summary>
    /// 在 Shoot_Single 动画的第 11 帧添加 Event，并选择此函数！
    /// </summary>
    public void OnShootHitPoint()
    {
        if (hunterPlayer != null)
        {
            // 触发真正的攻击特效和逻辑
            hunterPlayer.ExecuteAttackEffect();
        }
    }
}