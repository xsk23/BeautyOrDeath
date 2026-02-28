using UnityEngine;

[CreateAssetMenu(fileName = "VictoryCameraData", menuName = "Game/Camera Data")]
public class CameraData : ScriptableObject
{
    public Vector3 position;
    public Vector3 eulerRotation; // 使用欧拉角方便在 Inspector 调整
}