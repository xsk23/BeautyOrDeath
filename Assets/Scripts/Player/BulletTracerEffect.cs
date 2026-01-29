using UnityEngine;
using System.Collections;

public class BulletTracerEffect : MonoBehaviour
{
    [Header("设置")]
    public LineRenderer lineRenderer;
    public float duration = 0.1f; // 弹道存在时间（非常短）

    public void Init(Vector3 startPos, Vector3 endPos)
    {
        lineRenderer.positionCount = 2;
        // 1. 设置线的起点和终点
        lineRenderer.SetPosition(0, startPos);
        lineRenderer.SetPosition(1, endPos);

        // 2. 开始消失协程
        StartCoroutine(FadeAndDestroy());
    }

    IEnumerator FadeAndDestroy()
    {
        float timer = 0f;
        float startWidth = lineRenderer.startWidth;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            // 计算进度 0.0 -> 1.0
            float progress = timer / duration;

            // 视觉效果：让线随着时间变得越来越细，直到看不见
            // Lerp(a, b, t) 是在 a 和 b 之间插值
            float currentWidth = Mathf.Lerp(startWidth, 0f, progress);

            lineRenderer.startWidth = currentWidth;
            lineRenderer.endWidth = currentWidth; // 尾部也变细

            // 或者你可以改颜色透明度：
            // Color c = lineRenderer.material.color;
            // c.a = Mathf.Lerp(1, 0, progress);
            // lineRenderer.material.color = c;

            yield return null; // 等待下一帧
        }

        // 3. 销毁这个特效物体
        Destroy(gameObject);
    }
}