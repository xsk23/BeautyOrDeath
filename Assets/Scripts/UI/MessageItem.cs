using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class MessageItem : MonoBehaviour
{
    // 拖入预制体里的 Text 组件
    public TextMeshProUGUI messageText; 

    public void Setup(string playerName, string message, Color nameColor)
    {
        // 格式化信息，例如： [PlayerName]: Hello World
        // 这里用了富文本 (Rich Text) 来给名字上色
        string hexColor = ColorUtility.ToHtmlStringRGB(nameColor);
        messageText.text = $"<color=#{hexColor}><b>[{playerName}]</b></color>: {message}";
    }
}