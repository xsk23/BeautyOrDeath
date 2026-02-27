using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct GroupDanceConfig
{
    public int playerCount; 
    public RuntimeAnimatorController[] individualAnimators; 
}

[CreateAssetMenu(fileName = "VictoryAnimData", menuName = "Game/Victory Animation Data")]
public class VictoryAnimData : ScriptableObject
{
    [Header("相机配置资源")]
    public CameraData cameraSettings; // <--- 关键修改：直接拖入你的 CameraData 资源

    [Header("群舞配置列表")]
    public List<GroupDanceConfig> groupDances;

    public RuntimeAnimatorController[] GetAnimatorsForCount(int count)
    {
        foreach (var dance in groupDances)
        {
            if (dance.playerCount == count) return dance.individualAnimators;
        }
        if (groupDances.Count > 0) return groupDances[0].individualAnimators;
        return null;
    }
}