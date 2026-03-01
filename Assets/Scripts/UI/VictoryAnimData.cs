using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct GroupDanceConfig
{
    public string danceName; // 新增：方便在编辑器里辨认（如 "Witch Party A"）
    public int playerCount; 
    public RuntimeAnimatorController[] individualAnimators; 
    public AudioClip victoryMusic; // <--- 新增：该人数舞蹈对应的背景音乐
}

[CreateAssetMenu(fileName = "VictoryAnimData", menuName = "Game/Victory Animation Data")]
public class VictoryAnimData : ScriptableObject
{
    [Header("相机配置资源")]
    public CameraData cameraSettings; // <--- 关键修改：直接拖入你的 CameraData 资源

    [Header("群舞配置列表")]
    public List<GroupDanceConfig> groupDances;

    // 【核心修改】由服务器调用：查找匹配人数的所有索引，并随机选一个
    public int GetRandomConfigIndex(int count)
    {
        List<int> matchingIndices = new List<int>();

        for (int i = 0; i < groupDances.Count; i++)
        {
            if (groupDances[i].playerCount == count)
            {
                matchingIndices.Add(i);
            }
        }

        if (matchingIndices.Count > 0)
        {
            // 随机选择一个匹配项的索引
            return matchingIndices[Random.Range(0, matchingIndices.Count)];
        }

        return -1; // 未找到匹配项
    }

    // 供 RPC 调用：根据索引获取特定配置
    public GroupDanceConfig GetConfigByIndex(int index)
    {
        if (index >= 0 && index < groupDances.Count)
            return groupDances[index];
        
        return groupDances.Count > 0 ? groupDances[0] : default;
    }
}