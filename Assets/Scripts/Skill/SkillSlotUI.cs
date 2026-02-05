using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillSlotUI : MonoBehaviour
{
    [Header("UI References")]
    public Image skillIcon;        // 技能图标
    public Image cooldownOverlay;  // 冷却遮罩 (Fill Type = Radial 360)
    public TextMeshProUGUI keyText; // 按键提示 (Q, E, R)

    public float transparency = 0.5f;

    public void Setup(Sprite icon, string key)
    {
        //Debug.Log($"[UI Debug] Setup called for Key: {key}.");
        if (icon != null) skillIcon.sprite = icon;
        if (keyText != null) keyText.text = key;
        if (cooldownOverlay != null) 
        {
            // 1. 設置與圖標相同的圖片，這樣遮罩的形狀才會跟技能圖標一致
            cooldownOverlay.sprite = skillIcon.sprite;
            
            // 2. 設置顏色為黑色，並調整 Alpha 值 (透明度)
            // Color(R, G, B, A) -> 數值範圍是 0 到 1
            // 0.5f 代表 50% 的透明度
            cooldownOverlay.color = new Color(0f, 0f, 0f, transparency); 

            // 3. 設置填充模式
            cooldownOverlay.type = Image.Type.Filled;
            cooldownOverlay.fillMethod = Image.FillMethod.Radial360;
            cooldownOverlay.fillOrigin = (int)Image.Origin360.Top; // 從正上方開始轉
            
            // 4. 初始化填充比例（0 = 沒冷卻，1 = 全黑遮擋）
            cooldownOverlay.fillAmount = 0;
        }
    }

    public void UpdateCooldown(float ratio)
    {
        if (cooldownOverlay != null)
        {
            cooldownOverlay.fillAmount = ratio;
        }
    }
}