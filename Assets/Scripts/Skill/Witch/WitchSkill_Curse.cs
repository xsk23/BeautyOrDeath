using UnityEngine;
using Mirror;

public class WitchSkill_Curse : SkillBase
{
    public float range = 10f;
    public LayerMask treeLayer; // 确保树在这个 Layer

    protected override void OnCast()
    {
        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used skill: Curse! Attempting to curse a tree.</color>");
        // 射线检测
        Ray ray = new Ray(ownerPlayer.transform.position + Vector3.up, ownerPlayer.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, range, treeLayer))
        {
            PropTarget prop = hit.collider.GetComponentInParent<PropTarget>();
            // 只能诅咒普通树，不能诅咒古树
            if (prop != null && !prop.isAncientTree)
            {
                // 动态添加组件
                if (prop.gameObject.GetComponent<CursedTreeTrigger>() == null)
                {
                    var curse = prop.gameObject.AddComponent<CursedTreeTrigger>();
                    curse.casterNetId = ownerPlayer.netId;
                    // 不需要 NetworkServer.Spawn，因为这是添加组件，但要注意 Mirror 对于动态组件的支持有限
                    // 更好的做法是生成一个不可见的 Hitbox Prefab 罩住树
                    // 简易做法：利用 Rpc 通知客户端显示特效
                    RpcCurseEffect(prop.transform.position);
                }
            }
        }
    }

    [ClientRpc]
    void RpcCurseEffect(Vector3 pos)
    {
        // 播放一点紫色的粒子特效，提示女巫诅咒成功
    }
}