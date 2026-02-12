using UnityEngine;
using Mirror;

public class HunterSkill_Trap : SkillBase
{
    public GameObject trapPrefab;
    
    [Header("放置设置")]
    public float yOffset = 0.05f; 
    public float placeDistance = 1.5f; // 将距离提取为变量
    public float maxGroundCheckDistance = 10f; // 射线向下检测的最大距离

    protected override void OnCast()
    {
        HunterPlayer hunter = ownerPlayer as HunterPlayer;
        if (hunter == null) return;

        Debug.Log($"<color=green>[Hunter] {ownerPlayer.playerName} used skill: Place Trap!</color>");

        if (trapPrefab == null)
        {
            trapPrefab = Resources.Load<GameObject>("Prefabs/HunterTrap");
        }

        // 1. 计算目标水平坐标 (忽略 Y 轴的变化，只取 X 和 Z)
        // 这样即使猎人抬头看天，陷阱也不会试图放到天上，而是水平前方
        Vector3 forwardFlat = hunter.transform.forward;
        forwardFlat.y = 0; 
        forwardFlat.Normalize();
        
        // 初始目标点（此时 Y 值依然是猎人的脚底高度，如果在空中，这个 Y 很高）
        Vector3 potentialPos = hunter.transform.position + forwardFlat * placeDistance;

        // 2. 准备射线检测
        // 从目标点上方一点开始向下射，确保能覆盖略微不平的地面
        // 如果在空中跳跃，startPos 的 Y 会很高，向下射 50 米通常能碰到地
        Vector3 rayStart = potentialPos + Vector3.up * 1.0f; 
        
        // 【Debug】在 Scene 窗口画出射线 (红色=未命中，绿色=命中)
        // 游戏运行时去 Scene 窗口看一眼，能不能看到这条红线
        Debug.DrawRay(rayStart, Vector3.down * maxGroundCheckDistance, Color.red, 3.0f);

        // 3. 进行射线检测
        // 注意：建议在 Inspector 中检查 hunter.groundLayer，确保它【不包含】Player 层
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, maxGroundCheckDistance, hunter.groundLayer))
        {
            // --- 情况 A：检测到地面 ---
            
            // 再次确认没有打到自己（如果 GroundLayer 设置得当，这步其实是多余的，但为了保险）
            if (hit.collider.gameObject == hunter.gameObject)
            {
                Debug.LogWarning("Trap placement failed: Raycast hit the player itself. Check GroundLayer!");
                return;
            }

            // 修正生成位置为打击点 + 偏移
            Vector3 finalSpawnPos = hit.point + Vector3.up * yOffset;

            // 【Debug】画出命中位置
            Debug.DrawLine(rayStart, hit.point, Color.green, 3.0f);

            try
            {
                // 保持陷阱水平旋转（不随地面倾斜），或者你可以使用 Quaternion.FromToRotation 让陷阱贴合斜坡
                Quaternion trapRotation = Quaternion.Euler(0, hunter.transform.eulerAngles.y, 0);
                
                GameObject trap = Instantiate(trapPrefab, finalSpawnPos, trapRotation);
                NetworkServer.Spawn(trap);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Exception during Instantiate: {e.Message}");
            }
        }
        else
        {
            // --- 情况 B：未检测到地面 (悬崖外或跳得太高超过检测距离) ---
            Debug.LogWarning("无法放置陷阱：下方未检测到地面 (Too high or void)");
            
            // 这里我们直接 return，不再生成陷阱，从而彻底解决“浮空陷阱”的问题
            // 如果你希望即使在空中也生成（类似于丢出去），则在这里写 else 逻辑，但通常陷阱需要贴地。
        }
    }
}