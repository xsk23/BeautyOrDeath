using UnityEngine;
using Mirror;

public class WitchSkill_Mist : SkillBase
{
    [Header("技能参数")]
    public GameObject mistPrefab; // 迷雾预制体
    public float spawnOffset = 1.0f; // 在身后多少米生成

    protected override void OnCast()
    {
        if (mistPrefab == null)
        {
            Debug.LogError("[WitchSkill_Mist] Mist Prefab 未赋值！");
            return;
        }

        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used Mist!</color>");

        // 1. 计算生成位置：在女巫身后
        // 注意使用 -transform.forward
        Vector3 spawnPos = ownerPlayer.transform.position - ownerPlayer.transform.forward * spawnOffset;
        
        // 稍微抬高一点，防止生成在地板下
        spawnPos.y += 0.5f;

        // 2. 生成实例
        GameObject mist = Instantiate(mistPrefab, spawnPos, Quaternion.identity);

        // 3. 网络同步
        NetworkServer.Spawn(mist);
    }
}