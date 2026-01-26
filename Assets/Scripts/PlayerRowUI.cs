using UnityEngine;
using UnityEngine.UI;
using TMPro; // 如果你用TextMeshPro

public class PlayerRowUI : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI statusText;
    public Button actionButton;       // 对应 Prefab 里的按钮
    public TextMeshProUGUI actionButtonText;     // 对应按钮里面的文字 (用于显示 Ready / Cancel)

    // 更新这一行的显示内容
    public void UpdateInfo(string playerName, bool isReady, bool isLocalPlayer)
    {
        // 名字显示
        nameText.text = playerName + (isLocalPlayer ? " (You)" : "");
        // nameText.color = isLocalPlayer ? Color.green : Color.white;

        // 状态显示
        statusText.text = isReady ? "<color=green>READY</color>" : "<color=red>WAITING</color>";
        // 如果这行是本地玩家，我们需要更新按钮上的文字
        if (isLocalPlayer && actionButtonText != null)
        {
            actionButtonText.text = isReady ? "Cancel" : "Ready";
        }
    }
}