using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoomItemUI : MonoBehaviour
{
    public Button myButton;
    public TextMeshProUGUI roomNameText;
    public GameObject lockIcon;
    public TextMeshProUGUI roomIdText;

    private int myRoomId;
    private bool hasPassword;
    private ConnectUIManager manager;

    public void Setup(RoomInfo info, ConnectUIManager uiManager)
    {
        myRoomId = info.roomId;
        hasPassword = info.hasPassword;
        manager = uiManager;

        // 设置 UI 显示
        if (roomNameText) roomNameText.text = info.roomName;
        if (roomIdText) roomIdText.text = $"{info.roomId}";
        if (lockIcon) lockIcon.SetActive(info.hasPassword);

        myButton.onClick.RemoveAllListeners();
        myButton.onClick.AddListener(OnItemClicked);
    }

    void OnItemClicked()
    {
        manager.SelectRoom(myRoomId, hasPassword);
    }
}