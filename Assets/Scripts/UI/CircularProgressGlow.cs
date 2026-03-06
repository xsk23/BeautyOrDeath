using UnityEngine;
using UnityEngine.UI;

public class CircularProgressGlow : MonoBehaviour
{
    [Header("References")]
    public Image fillImage;         // 拖入 Fill 物体
    public RectTransform headDot;   // 拖入 ProgressHead 物体

    [Header("Settings")]
    public float radius = 50f;      // 圆环的半径（根据你进度条的大小调整）

    public void UpdateProgress(float progress)
    {
        // 1. 设置进度条填充
        fillImage.fillAmount = progress;

        // 2. 计算末端圆点的位置
        // Unity 的 Radial Fill 0是从顶部(90度)顺时针开始
        float angle = progress * 360f;
        float rad = (90f - angle) * Mathf.Deg2Rad; // 转换为弧度

        float x = Mathf.Cos(rad) * radius;
        float y = Mathf.Sin(rad) * radius;

        // 3. 更新圆点位置
        if (headDot != null)
        {
            headDot.anchoredPosition = new Vector2(x, y);
            // 只有当进度 > 0 时才显示圆点，防止起始位置露出
            headDot.gameObject.SetActive(progress > 0.01f && progress < 0.99f);
        }
    }
}