using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public enum ChatChannel
{
    All,  // 全局
    Team  // 队伍
}

public class GameChatUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject chatPanel;        // 整个聊天界面的根物体 (含输入框)
    public TMP_InputField inputField;   // 输入框
    public Transform messageContent;    // ScrollView 的 Content
    public GameObject messagePrefab;    // 消息预制体
    public ScrollRect scrollRect;       // 用于自动滚动
    public TextMeshProUGUI channelText; // 显示当前频道 (比如 "[ALL]" 或 "[TEAM]")
    // 【新增】聊天背景图片的引用
    public Image chatBackgroundImage; 

    [Header("Settings")]
    public KeyCode openChatKey = KeyCode.Slash; // 按 '/' 打开
    public KeyCode closeChatKey = KeyCode.Escape;
    public KeyCode switchChannelKey = KeyCode.Tab; // 按 Tab 切换频道

    public bool isChatOpen = false;
    private ChatChannel currentChannel = ChatChannel.All;
    private GamePlayer localPlayer; // 缓存本地玩家引用

    private void Start()
    {
        // 初始关闭聊天输入栏，但保持消息显示区域可见（通常做法）
        // 或者你可以选择一开始全隐藏
        SetChatState(false);
        UpdateChannelUI();

        // 绑定输入框提交事件
        if (inputField != null)
            inputField.onSubmit.AddListener(OnSubmitMessage);
    }

    private void Update()
    {
        // 获取本地玩家引用（如果还没获取）
        if (localPlayer == null)
        {
            foreach (var p in GamePlayer.AllPlayers)
            {
                if (p.isLocalPlayer)
                {
                    localPlayer = p;
                    break;
                }
            }
        }

        // --- 按键监听 ---
        
        // 打开聊天
        if (!isChatOpen && Input.GetKeyDown(openChatKey))
        {
            SetChatState(true);
        }
        // // 关闭聊天
        // else if (isChatOpen && Input.GetKeyDown(closeChatKey))
        // {
        //     SetChatState(false);
        // }
        // 切换频道 (仅当聊天打开时)
        else if (isChatOpen && Input.GetKeyDown(switchChannelKey))
        {
            ToggleChannel();
        }
    }

    // 切换聊天状态
    public void SetChatState(bool isOpen)
    {
        isChatOpen = isOpen;
        
        if (chatPanel != null)
            chatPanel.SetActive(isOpen); // 控制输入框面板的显示/隐藏

        // 【新增】控制背景透明度
        if (chatBackgroundImage != null)
        {
            Color color = chatBackgroundImage.color;
            // 打开时 0.1 (微弱背景)，关闭时 0 (全透明)
            color.a = isOpen ? 0.1f : 0f; 
            chatBackgroundImage.color = color;
        }
        // 控制垂直滚动条
        // 2. 【核心修改】控制垂直滚动条的透明度与交互
        if (scrollRect != null && scrollRect.verticalScrollbar != null)
        {
            // A. 设置交互性：关闭时禁止拖动，防止误触
            scrollRect.verticalScrollbar.interactable = isOpen;

            // B. 设置透明度：获取滚动条下所有的 Image (背景槽和滑块Handle)
            Image[] scrollbarImages = scrollRect.verticalScrollbar.GetComponentsInChildren<Image>();
            foreach (var img in scrollbarImages)
            {
                Color c = img.color;
                // 这里假设滚动条完全显示时 alpha 为 1。如果你原本就是半透明，可以改为保存初始值。
                c.a = isOpen ? 1f : 0f; 
                img.color = c;
            }
        }

        if (isOpen)
        {
            // 打开：激活输入框，解锁鼠标
            inputField.ActivateInputField();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            
            // 告诉 GamePlayer 暂停移动输入 (可选，需要在 GamePlayer 里加个标志位)
            if (localPlayer != null) localPlayer.isChatting = true;
        }
        else
        {
            // 关闭：清空输入，锁定鼠标
            inputField.text = "";
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            
            // 恢复移动
            if (localPlayer != null) localPlayer.isChatting = false;
        }
    }

    // 切换频道逻辑
    private void ToggleChannel()
    {
        if (currentChannel == ChatChannel.All)
            currentChannel = ChatChannel.Team;
        else
            currentChannel = ChatChannel.All;

        UpdateChannelUI();
    }

    private void UpdateChannelUI()
    {
        if (channelText != null)
        {
            channelText.text = (currentChannel == ChatChannel.All) ? "[ALL]" : "[TEAM]";
            channelText.color = (currentChannel == ChatChannel.All) ? Color.white : Color.green;
        }
    }

    // 发送消息
    private void OnSubmitMessage(string text)
    {
        if (!string.IsNullOrWhiteSpace(text) && localPlayer != null)
        {
            localPlayer.CmdSendGameMessage(text, currentChannel);
        }

        // 发送完关闭聊天
        SetChatState(false);
        
        // 如果想发送完保持开启，可以注释上一行，改用：
        // inputField.text = ""; 
        // inputField.ActivateInputField();
    }

    // 供外部调用：显示接收到的消息
    public void AppendMessage(string senderName, string message, ChatChannel channel, Color roleColor)
    {
        if (messagePrefab == null || messageContent == null) return;

        // 1. 生成消息条目
        GameObject newMsg = Instantiate(messagePrefab, messageContent);
        
        // 【修改点】改为 GetComponentInChildren，防止 Text 在子物体上找不到
        TextMeshProUGUI tmp = newMsg.GetComponentInChildren<TextMeshProUGUI>(); 
        
        if (tmp != null)
        {
            // 2. 拼接频道信息
            string channelPrefix = (channel == ChatChannel.All) ? "[ALL]" : "[TEAM]";
            string channelColorHtml = (channel == ChatChannel.All) ? "#FFFFFF" : "#00FF00"; // 全局白色，队伍绿色
            
            // 3. 处理名字颜色
            string nameColorHtml = ColorUtility.ToHtmlStringRGB(roleColor);

            // 4. 组合最终字符串并赋值
            // 格式： [ALL] [PlayerName]: Hello World
            tmp.text = $"<color={channelColorHtml}>{channelPrefix}</color> <color=#{nameColorHtml}>[{senderName}]</color>: {message}";
        }
        else
        {
            // 如果还是没找到，打印错误方便调试
            Debug.LogError("错误：在 MessagePrefab 中找不到 TextMeshProUGUI 组件！请检查预制体结构。");
        }

        // 5. 刷新布局
        LayoutRebuilder.ForceRebuildLayoutImmediate(messageContent.GetComponent<RectTransform>());
        StartCoroutine(ScrollToBottom());
    }

    IEnumerator ScrollToBottom()
    {
        yield return new WaitForEndOfFrame();
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }
}