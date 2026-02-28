using UnityEngine;
using TMPro;

public class VictoryNameFollow : MonoBehaviour
{
    public Transform targetBone; // 要跟随的骨骼（如 Head）
    public Vector3 offset = new Vector3(0, 0.1f, 0); // 头顶偏移量

    void LateUpdate()
    {
        if (targetBone != null)
        {
            // 每一帧同步骨骼位置
            transform.position = targetBone.position + offset;

            // 保持文字始终面向相机
            if (Camera.main != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
            }
        }
    }
}