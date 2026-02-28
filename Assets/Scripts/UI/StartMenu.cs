using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class StartMenu : MonoBehaviour
{
    NetworkManager manager;
    public GameObject networkManagerPrefab;
    public NetworkManagerHUD_UGUI networkHUD;  // 如果你還在使用這個 HUD
    [Header("Player Name Input")]
    public TMP_InputField inputFieldPlayerName;   // ← 拖進來
    [Header("UI References")]
    public Button joinButton;          // ← 在 Inspector 拖入 Join 按鈕
    [Header("Network Selection")]
    public TMP_Dropdown networkDropdown; // ← 把你的 Dropdown 拖到这里
    // 硬编码的服务器 IP
    private const string REMOTE_SERVER_IP = "101.42.183.176";
    [Header("Transition UI")]
    public GameObject loadingPanel; // 在 Inspector 中拖入你新增的那个 Panel
    public TextMeshProUGUI countdownText; // 1. 拖入你的 CountDownText (TMP)
    private void Start()
    {
        if (manager == null)
        {
            // 嘗試在場景中找到已存在的 NetworkManager
            manager = FindObjectOfType<NetworkManager>();
            if (manager == null)
            {
                // 如果找不到，則實例化一個新的
                GameObject obj = Instantiate(networkManagerPrefab);
                manager = obj.GetComponent<NetworkManager>();
                if (manager == null)
                {
                    Debug.LogError("NetworkManager component not found on the instantiated prefab!");
                }
            }
        }
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
        // 检测是否是从 ConnectRoom 跳转回来的“连接中”状态
        if (MyNetworkManager.IsTransitioningToRoom)
        {
            ShowLoadingPanel();
        }
        else
        {
            if(loadingPanel != null) loadingPanel.SetActive(false);
        }
    }
    private void ShowLoadingPanel()
    {
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(true);
            StartCoroutine(UIRunCountdownRoutine(5f)); 
            // 如果你的 Panel 里有“取消”按钮，可以绑定 StopClient 逻辑
            Debug.Log("[UI] 检测到房间跳转中，激活加载面板");
        }
    }
    // 3. 实现倒计时协程
    private IEnumerator UIRunCountdownRoutine(float duration)
    {
        float timer = duration;

        while (timer > 0)
        {
            if (countdownText != null)
            {
                // 使用 CeilToInt 向上取整，这样会显示 5, 4, 3, 2, 1
                countdownText.text = Mathf.CeilToInt(timer).ToString();
            }

            timer -= Time.deltaTime;
            yield return null; // 每帧更新
        }

        if (countdownText != null)
        {
            countdownText.text = "0"; // 结束时显示 0 或 "Connecting..."
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
        // // 2. 設定連線位址
        // if (networkHUD != null && !string.IsNullOrEmpty(networkHUD.inputFieldIP.text))
        // {
        //     manager.networkAddress = networkHUD.inputFieldIP.text;
        // }
        // --- 2. 设置 IP 地址 ---
        // 0: Localhost, 1: Server (根据你在 Inspector 里 Dropdown 选项的顺序)
        if (networkDropdown.value == 0)
        {
            // 选项 0: Localhost
            manager.networkAddress = "localhost";
            Debug.Log($"[Connect] Mode: Localhost ({manager.networkAddress})");
        }
        else
        {
            // 选项 1: Server
            manager.networkAddress = REMOTE_SERVER_IP;
            Debug.Log($"[Connect] Mode: Remote Server ({manager.networkAddress})");
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