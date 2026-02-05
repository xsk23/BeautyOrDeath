using UnityEngine;
using Mirror;


public class HunterSkill_Trap : SkillBase
{
    public GameObject trapPrefab;

    protected override void OnCast()
    {
        //find trapprefab 
        //todo


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
            spawnPos = hit.point;
        }

        try
        {
            GameObject trap = Instantiate(trapPrefab, spawnPos, Quaternion.identity);
            NetworkServer.Spawn(trap);
            // if (trap == null)
            // {
            //     Debug.LogError("Instantiate failed to return an object!");
            //     return;
            // }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Exception during Instantiate: {e.Message}");
        }


        
        
    }
}