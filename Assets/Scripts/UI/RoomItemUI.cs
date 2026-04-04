using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomItemUI : MonoBehaviour
{
    public Button myButton;
    public TextMeshProUGUI roomNameText;
    public GameObject lockIcon;
    public TextMeshProUGUI roomIdText;
    [Header("Visual Selection")]
    public Image backgroundImage; // 拖入条目的背景图组件
    public Color normalColor = new Color(1,1,1,0); // 透明或默认色
    public Color selectedColor = new Color(1, 0.9f, 0, 0.5f); // 选中的颜色（如淡金色）
    private int myRoomId;
    private bool hasPassword;
    private ConnectUIManager manager;
    private RoomInfo cachedInfo; // 增加一个缓存
    public void Setup(RoomInfo info, ConnectUIManager uiManager)
    {
        myRoomId = info.roomId;
        hasPassword = info.hasPassword;
        manager = uiManager;
        cachedInfo = info; // 缓存当前房间信息，方便后续点击时使用
        // 设置 UI 显示
        if (roomNameText) roomNameText.text = info.roomName;
        // if (roomIdText) roomIdText.text = $"{info.roomId}";
        // --- 修改：设置人数显示 (当前人数/上限) ---
        if (roomIdText) // 对应你截图中的 roomNum 物体
        {
            // 如果上限 >= 1000 (我们在创建时设定的非限制值)，则显示 ∞
            string maxStr = (info.maxPlayers >= 1000) ? "∞" : info.maxPlayers.ToString();
            roomIdText.text = $"{info.currentPlayers}/{maxStr}";
        }

        if (lockIcon) lockIcon.SetActive(info.hasPassword);

        myButton.onClick.RemoveAllListeners();
        myButton.onClick.AddListener(OnItemClicked);
        // 初始化视觉状态
        SetHighlight(false);
    }
    public void SetHighlight(bool active)
    {
        if (backgroundImage != null)
        {
            backgroundImage.color = active ? selectedColor : normalColor;
        }
    }
    void OnItemClicked()
    {
        // 播放音效
        AudioManager.Instance?.Play2D("UI选择");
        manager.SelectRoom(this, myRoomId, hasPassword, cachedInfo.currentPlayers, cachedInfo.maxPlayers);
    }
}