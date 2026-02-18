using UnityEngine;

public enum RewardCategory { Attribute, Skill, Extra }

[System.Serializable]
public struct RewardOption
{
    public string title;
    public string description;
    public RewardCategory category;
    public string rewardKey; // 用于标识具体的逻辑，例如 "MaxHP", "DecoyCount"
    public float value;      // 奖励数值
    public int id;           // 传递给服务器的唯一索引
}