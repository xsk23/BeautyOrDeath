using UnityEngine;
using Mirror;


public class HunterSkill_Trap : SkillBase
{
    public GameObject trapPrefab;
    [Header("放置设置")]
    public float yOffset = 0.05f; // 【新增】用于微调模型高度，防止没入地面
    protected override void OnCast()
    {
        HunterPlayer hunter = ownerPlayer as HunterPlayer;
        Debug.Log($"<color=green>[Hunter] {ownerPlayer.playerName} used skill: Place Trap!</color>");
        if (trapPrefab == null)
        {
            // 路径相对于 Resources 文件夹，不需要后缀名
            trapPrefab = Resources.Load<GameObject>("Prefabs/HunterTrap");
        }
        Vector3 spawnPos = hunter.transform.position + hunter.transform.forward * 1.5f;
        // 贴地
        if (Physics.Raycast(spawnPos + Vector3.up, Vector3.down, out RaycastHit hit, 5f))
        {
            // 【关键修改】在碰撞点的高度基础上，向上抬升 yOffset
            spawnPos = hit.point + Vector3.up * yOffset;
        }
        try
        {
            GameObject trap = Instantiate(trapPrefab, spawnPos, trapPrefab.transform.rotation);
            NetworkServer.Spawn(trap);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Exception during Instantiate: {e.Message}");
        }
    }
}