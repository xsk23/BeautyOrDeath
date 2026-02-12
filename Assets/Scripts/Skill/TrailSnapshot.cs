using UnityEngine;

[System.Serializable]
public struct TrailSnapshot
{
    public Vector3 position;
    public Quaternion rotation;
    public int propID; // -1 代表人类形态，>=0 代表变身物品ID
    // 注意：Mirror在序列化结构体时不需要继承NetworkBehaviour，但字段必须是基本类型
}