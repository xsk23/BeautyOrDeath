using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using TMPro;
using UnityEngine.UI;

public class StartMenu : MonoBehaviour
{
    NetworkManager manager;
    public NetworkManagerHUD_UGUI networkHUD;  // 如果你還在使用這個 HUD
    [Header("Player Name Input")]
    public TMP_InputField inputFieldPlayerName;   // ← 拖進來
    [Header("UI References")]
    public Button joinButton;          // ← 在 Inspector 拖入 Join 按鈕
    private void Start()
    {
        manager = FindObjectOfType<NetworkManager>();
        // 如果之前有存過名字，可以預填
        if (PlayerSettings.Instance != null && !string.IsNullOrEmpty(PlayerSettings.Instance.PlayerName))
        {
            inputFieldPlayerName.text = PlayerSettings.Instance.PlayerName;
        }
        // 一開始先檢查一次
        UpdateJoinButtonState();

        // 監聽輸入改變 → 每次輸入都檢查一次
        if (inputFieldPlayerName != null)
        {
            inputFieldPlayerName.onValueChanged.AddListener(OnPlayerNameChanged);
        }
    }
    private void OnPlayerNameChanged(string newText)
    {
        UpdateJoinButtonState();
    }

    private void UpdateJoinButtonState()
    {
        if (joinButton == null) return;

        bool hasName = inputFieldPlayerName != null 
            && !string.IsNullOrWhiteSpace(inputFieldPlayerName.text.Trim());

        joinButton.interactable = hasName;

        // 可選：改變按鈕顏色或文字提示更明顯
        // var colors = joinButton.colors;
        // colors.normalColor = hasName ? Color.white : new Color(0.7f, 0.7f, 0.7f);
        // joinButton.colors = colors;
    }

    // 現在只有一個 Join 按鈕，功能等同「加入伺服器」
    public void OnButtonJoin()
    {
        if (!joinButton.interactable) return;  // 保險起見再檢查一次
        // 1. 儲存玩家輸入的名字
        string name = "";
        if (inputFieldPlayerName != null && !string.IsNullOrWhiteSpace(inputFieldPlayerName.text))
        {
            name = inputFieldPlayerName.text.Trim();
            // 可選：限制長度
            if (name.Length > 16) name = name.Substring(0, 16);
        }

        // 存到持久物件
        if (PlayerSettings.Instance != null)
        {
            PlayerSettings.Instance.PlayerName = name;
        }
        else
        {
            Debug.LogWarning("PlayerSettings singleton not found!");
        }
        // 2. 設定連線位址
        if (networkHUD != null && !string.IsNullOrEmpty(networkHUD.inputFieldIP.text))
        {
            manager.networkAddress = networkHUD.inputFieldIP.text;
        }

        Debug.Log($"嘗試連線到 {manager.networkAddress}，名字：{name}");

        manager.StartClient();
    }

    public void OnButtonQuit()
    {
        Debug.Log("玩家選擇退出遊戲");

    #if UNITY_EDITOR
        UnityEditor.EditorApplication.ExitPlaymode();  // 在 Editor 裡停止 Play 模式
    #else
        Application.Quit();  // 建置後真正退出
    #endif
    }
}