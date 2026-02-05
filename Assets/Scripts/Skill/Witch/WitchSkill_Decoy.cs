using UnityEngine;
using Mirror;


public class WitchSkill_Decoy : SkillBase
{
    public GameObject decoyPrefab; 

    protected override void OnCast()
    {
        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used skill: Decoy! Summoning a decoy.</color>");
        WitchPlayer witch = ownerPlayer as WitchPlayer;
        if (witch == null) return;

        // 如果没变身，就复制人类 (或者禁止使用)
        // 这里假设复制当前的 morphedPropID
        int idToCopy = witch.isMorphed ? witch.morphedPropID : -1; // -1 表示没变身

        // 在玩家前方一個身位的位置生成
        Vector3 spawnPosition = witch.transform.position + witch.transform.forward * 1.0f;
        GameObject decoy = Instantiate(decoyPrefab, spawnPosition, witch.transform.rotation);
        DecoyBehavior db = decoy.GetComponent<DecoyBehavior>();
        db.propID = idToCopy;

        NetworkServer.Spawn(decoy);
    }
}