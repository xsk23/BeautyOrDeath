using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class UIButtonEffects : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private Vector3 initialScale;
    public float hoverScale = 1.05f;    // 悬浮时放大倍数
    public float pressScale = 0.95f;    // 按下时缩小倍数
    
    [Header("Color Tint (Optional)")]
    public Image targetImage;           // 按钮的背景图
    public Color hoverColor = Color.white;
    public Color pressColor = new Color(0.7f, 0.7f, 0.7f);
    private Color originalColor;

    private void Awake()
    {
        initialScale = transform.localScale;
        if (targetImage == null) targetImage = GetComponent<Image>();
        if (targetImage != null) originalColor = targetImage.color;
    }

    // 鼠标移入
    public void OnPointerEnter(PointerEventData eventData)
    {
        transform.localScale = initialScale * hoverScale;
        // 如果想做发光效果，可以在这里开启一个隐藏的 Glow 图片
    }

    // 鼠标移出
    public void OnPointerExit(PointerEventData eventData)
    {
        transform.localScale = initialScale;
        if (targetImage != null) targetImage.color = originalColor;
    }

    // 鼠标按下
    public void OnPointerDown(PointerEventData eventData)
    {
        transform.localScale = initialScale * pressScale;
        if (targetImage != null) targetImage.color = pressColor;
    }

    // 鼠标抬起
    public void OnPointerUp(PointerEventData eventData)
    {
        transform.localScale = initialScale * hoverScale;
        if (targetImage != null) targetImage.color = hoverColor;
    }
}