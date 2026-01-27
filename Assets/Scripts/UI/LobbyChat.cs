using UnityEngine;
using UnityEngine.UI; // Button 还是用这个
using TMPro;          // 【关键】引用 TMP
using Mirror;

public class LobbyChat : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField chatBoxField; // 【关键】改为 TMP_InputField
    public Button sendButton;           // 发送按钮
    public Transform messageLayout;     // MessageLayout (Content 父物体)
    public GameObject messageGroupPrefab; // MessageGroup 预制体
    public ScrollRect scrollRect;       // (可选) 用于自动滚动

    private void Start()
    {
        // 绑定按钮点击
        if(sendButton) sendButton.onClick.AddListener(OnSendClicked);

        // 绑定回车发送 (TMP_InputField 的事件)
        if(chatBoxField) chatBoxField.onSubmit.AddListener(OnSubmit);
    }

    private void OnSendClicked()
    {
        SendMessageToServer(chatBoxField.text);
    }

    private void OnSubmit(string text)
    {
        SendMessageToServer(text);
        
        // 发送后保持输入框焦点，并清空，方便连续输入
        chatBoxField.ActivateInputField();
        chatBoxField.text = "";
    }

    private void SendMessageToServer(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return;

        // 获取本地玩家进行发送
        if (NetworkClient.connection != null && NetworkClient.connection.identity != null)
        {
            var localPlayer = NetworkClient.connection.identity.GetComponent<PlayerScript>();
            if (localPlayer != null)
            {
                localPlayer.CmdSendChatMessage(msg);
            }
        }
        
        // 清空输入框
        if(chatBoxField) chatBoxField.text = "";
    }

    // 供 PlayerScript 接收到消息后调用
    public void AppendMessage(string playerName, string message, Color color)
    {
        if (messageGroupPrefab == null || messageLayout == null) return;

        // 生成新消息
        GameObject newMsg = Instantiate(messageGroupPrefab, messageLayout);
        MessageItem item = newMsg.GetComponent<MessageItem>();
        
        if (item != null)
        {
            item.Setup(playerName, message, color);
        }

        // 【关键】强制刷新布局系统
        // 有时候 Content Size Fitter 需要一帧的时间来计算高度，强制刷新可以立即生效
        LayoutRebuilder.ForceRebuildLayoutImmediate(messageLayout.GetComponent<RectTransform>());

        // 滚动到底部
        StartCoroutine(ScrollToBottom());
    }
    System.Collections.IEnumerator ScrollToBottom()
    {
        // 等待一帧让UI布局刷新
        yield return new WaitForEndOfFrame();
        if(scrollRect) scrollRect.verticalNormalizedPosition = 0f;
    }
}