using UnityEngine;
using Mirror;
public class WitchSkill_Decoy : SkillBase
{
    public GameObject decoyPrefab; 
    [HideInInspector] public int spawnCount = 1; // 默认生成1个
    
    protected override void OnCast()
    {
        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used skill: Decoy!</color>");
        WitchPlayer witch = ownerPlayer as WitchPlayer;
        if (witch == null) return;

        GameManager.Instance?.ServerPlay3DAt("pop_sound", ownerPlayer.transform.position);
        
        // 记录当前的变身状态
        int idToCopy = witch.isMorphed ? witch.morphedPropID : -1; 
        
        // 动态计算防卡墙偏移量
        float spawnOffset = 1.0f;
        CharacterController cc = witch.GetComponent<CharacterController>();
        if (cc != null) 
        {
            spawnOffset = cc.radius + 0.5f; // 根据当前的半径向外推
        }

        for (int i = 0; i < spawnCount; i++)
        {
            Vector3 randomOffset = new Vector3(Random.Range(-0.5f, 0.5f), 0, Random.Range(-0.5f, 0.5f));
            Vector3 spawnPosition = witch.transform.position + witch.transform.forward * spawnOffset + randomOffset;
            
            // 地面探测：从稍高处向下发射射线，确保分身贴地
            if (Physics.Raycast(spawnPosition + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, witch.groundLayer))
            {
                spawnPosition = hit.point + Vector3.up * 0.1f; 
            }

            GameObject decoy = Instantiate(decoyPrefab, spawnPosition, witch.transform.rotation);

            decoy.SetActive(true);

            DecoyBehavior db = decoy.GetComponent<DecoyBehavior>();

            
            // 先调用 ServerSetup 处理好物理和模型，然后再 Spawn
            db.ServerSetup(idToCopy);
            
            // 然后再把完全准备好的分身发布到网络上给所有客户端
            NetworkServer.Spawn(decoy);            
        }
    }
}

// public class WitchSkill_Decoy : SkillBase
// {
//     public GameObject decoyPrefab; 
//     [HideInInspector] public int spawnCount = 1; // 默认生成1个
//     protected override void OnCast()
//     {
//         Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used skill: Decoy! Summoning a decoy.</color>");
//         WitchPlayer witch = ownerPlayer as WitchPlayer;
//         if (witch == null) return;

//         GameManager.Instance?.ServerPlay3DAt("pop_sound", ownerPlayer.transform.position);
        
//         // 如果没变身，就复制人类 (或者禁止使用)
//         // 这里假设复制当前的 morphedPropID
//         int idToCopy = witch.isMorphed ? witch.morphedPropID : -1; // -1 表示没变身
//         for (int i = 0; i < spawnCount; i++)
//         {
//             // 在玩家前方一個身位的位置生成
//             Vector3 spawnPosition = witch.transform.position + witch.transform.forward * 1.0f;
//             // 2. 地面探测：从上方发射射线，确保分身生成在地面高度
//             if (Physics.Raycast(spawnPosition + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, witch.groundLayer))
//             {
//                 spawnPosition = hit.point + Vector3.up * 0.05f; // 贴地并微抬防止卡入
//             }
//             GameObject decoy = Instantiate(decoyPrefab, spawnPosition, witch.transform.rotation);
//             DecoyBehavior db = decoy.GetComponent<DecoyBehavior>();
//             db.propID = idToCopy;

//             NetworkServer.Spawn(decoy);            
//         }

//     }
// }

