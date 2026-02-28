using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class UIButtonRotate : MonoBehaviour
{
    [Header("旋转设置")]
    public float duration = 0.5f; // 旋转一圈需要的时间
    private bool isRotating = false;

    // 这个方法绑定到 Button 的 OnClick 事件
    public void StartRotate()
    {
        if (!isRotating)
        {
            StartCoroutine(RotateRoutine());
        }
    }

    private IEnumerator RotateRoutine()
    {
        isRotating = true;
        float elapsed = 0f;
        
        // 记录初始旋转
        Quaternion startRotation = transform.localRotation;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float percent = elapsed / duration;
            
            // 顺时针旋转一圈 (从0度到-360度)
            // 如果想逆时针，把 -360 改成 360
            float zRotation = Mathf.Lerp(0, -360f, percent);
            transform.localRotation = Quaternion.Euler(0, 0, zRotation);
            
            yield return null;
        }

        // 确保最后旋转角度精准回到0
        transform.localRotation = startRotation;
        isRotating = false;
    }
}