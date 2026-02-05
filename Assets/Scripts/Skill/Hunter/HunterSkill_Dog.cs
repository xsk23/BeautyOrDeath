using UnityEngine;
using Mirror;

public class HunterSkill_Dog : SkillBase
{
    [Header("技能设置")]
    public GameObject dogPrefab; // 拖入刚才做好的 HunterDog
    public float spawnDistance = 1.5f; // 生成在猎人前方多少米

    protected override void OnCast()
    {
        if (dogPrefab == null) return;

        Debug.Log($"<color=green>[Hunter] {ownerPlayer.playerName} used skill: Summon Dog!</color>");

        // 1. 计算生成位置：猎人面前一点点，防止卡在猎人身体里
        Vector3 spawnPos = ownerPlayer.transform.position + ownerPlayer.transform.forward * spawnDistance;

        // 2. 计算朝向：非常重要！
        // 猎人的 transform.rotation 是包含 Y 轴旋转的，直接用这个就可以
        // 这样猎人看向哪里，狗就面朝哪里
        Quaternion spawnRot = ownerPlayer.transform.rotation;

        // 3. 生成实例
        GameObject dog = Instantiate(dogPrefab, spawnPos, spawnRot);
        
        // 4. 网络生成
        NetworkServer.Spawn(dog);
    }
}