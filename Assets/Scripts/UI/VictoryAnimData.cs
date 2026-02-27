using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct GroupDanceConfig
{
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

    // 获取特定人数的完整配置
    public GroupDanceConfig GetConfigForCount(int count)
    {
        foreach (var dance in groupDances)
        {
            if (dance.playerCount == count) return dance;
        }
        // 兜底返回第一个
        return groupDances.Count > 0 ? groupDances[0] : default;
    }

    // 为了兼容你现有的代码，保留这个方法（可选）
    public RuntimeAnimatorController[] GetAnimatorsForCount(int count)
    {
        return GetConfigForCount(count).individualAnimators;
    }
}