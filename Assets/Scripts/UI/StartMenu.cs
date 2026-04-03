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
    public Button cancelLoadingButton; // 【新增】拖入 LoadingPanel 下的 Button
    [Header("Disconnect UI")]
    public GameObject reconnectPanel;      // 对应你截图里的 ReconnectImage
    public TextMeshProUGUI errorText;      // 对应面板里的 Text (TMP)
    public Button okButton;                // 对应面板里的 Button
    [Header("Help Panel Animation")]
    public GameObject helpPanel;       // 拖入 HelpPanel
    public float animDuration = 0.2f;  // 动画持续时间
    private Coroutine activeAnim;      // 记录当前正在运行的动画
    public float helpTargetScale = 1.3f; // 新增：设置目标缩放值
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
        // 【新增】绑定取消按钮事件
        if (cancelLoadingButton != null)
        {
            cancelLoadingButton.onClick.AddListener(OnCancelConnection);
        }
        // 绑定确认按钮点击事件
        if (okButton != null)
        {
            okButton.onClick.AddListener(OnCloseReconnectPanel);
        }

        // 【核心逻辑】检查是否有待显示的断线错误
        CheckForPendingDisconnect();
        // --- 【新增代码】 ---
        // 如果不在编辑器中运行（即打包出来的游戏），隐藏切换选项
        #if !UNITY_EDITOR
        if (networkDropdown != null)
        {
            networkDropdown.gameObject.SetActive(false);
        }
        #endif
        // -------------------
        if (helpPanel != null) {
            helpPanel.transform.localScale = Vector3.zero;
            helpPanel.SetActive(false);
        }
    }
    // 供 Keyboard 按钮调用
    public void OnButtonKeyboardOpen()
    {
        if (activeAnim != null) StopCoroutine(activeAnim);
        AudioManager.Instance?.Play2D("UI选择"); // 播放你现有的音效
        activeAnim = StartCoroutine(AnimatePanel(true));
    }
    // 供右上角 X 按钮调用
    public void OnButtonKeyboardClose()
    {
        if (activeAnim != null) StopCoroutine(activeAnim);
        AudioManager.Instance?.Play2D("UI点击（木头）"); 
        activeAnim = StartCoroutine(AnimatePanel(false));
    }
    private IEnumerator AnimatePanel(bool show)
    {
        if (show) helpPanel.SetActive(true);

        Vector3 fullScale = new Vector3(helpTargetScale, helpTargetScale, helpTargetScale);
        Vector3 startScale = show ? Vector3.zero : fullScale;
        Vector3 endScale = show ? fullScale : Vector3.zero;
        float elapsed = 0f;

        while (elapsed < animDuration)
        {
            elapsed += Time.deltaTime;
            float percent = elapsed / animDuration;
            
            // 使用 SmoothStep 让动画有加速减速感，比线性更平滑
            float curvePercent = Mathf.SmoothStep(0, 1, percent);
            
            helpPanel.transform.localScale = Vector3.Lerp(startScale, endScale, curvePercent);
            yield return null;
        }

        helpPanel.transform.localScale = endScale;
        if (!show) helpPanel.SetActive(false);
        
        activeAnim = null;
    }
    private void CheckForPendingDisconnect()
    {
        // 检查 MyNetworkManager 里存的静态错误字符串
        if (!string.IsNullOrEmpty(MyNetworkManager.PendingErrorMessage))
        {
            // 显示面板
            if (reconnectPanel != null)
            {
                reconnectPanel.SetActive(true);
            }

            // 设置文字内容
            if (errorText != null)
            {
                errorText.text = MyNetworkManager.PendingErrorMessage;
            }

            // 播放提示音 (可选)
            // AudioManager.Instance?.Play2D("Error_Sound");
        }
        else
        {
            // 如果没有错误，确保面板是关闭的
            if (reconnectPanel != null) reconnectPanel.SetActive(false);
        }
    }
    // 点击 OK~ 按钮执行的逻辑
    public void OnCloseReconnectPanel()
    {
        // 1. 关闭面板
        if (reconnectPanel != null)
        {
            reconnectPanel.SetActive(false);
        }

        // 2. 【重要】清除静态错误信息，防止下次进主菜单又弹出来
        MyNetworkManager.PendingErrorMessage = "";

        // 3. 播放按钮音效
        AudioManager.Instance?.Play2D("UI点击（木头）");
    }
    public void OnCancelConnection()
    {
        Debug.Log("[UI] 用户取消加载，准备回大厅销毁房间进程...");

        // 1. 停止 UI 表现
        StopAllCoroutines();
        if (loadingPanel != null) loadingPanel.SetActive(false);

        // 2. 标记“下次连上大厅就杀掉房间”
        // 只有当我们确实有一个创建出来的 ID 时才标记
        if (MyNetworkManager.GlobalPendingRoomId > 0)
        {
            MyNetworkManager.PendingKillOnConnect = true;
        }

        // 3. 开启重连协程
        StartCoroutine(CancelAndReconnectRoutine());
        
        AudioManager.Instance?.Play2D("UI点击（木头）");
    }
    // 【新增】等待底层断开再重连，极其简单稳定
    private IEnumerator CancelAndReconnectRoutine()
    {
        MyNetworkManager.AbortTransition();

        // 等待 Mirror 彻底释放网络资源
        while (NetworkClient.active)
        {
            yield return null;
        }

        // 【核心修改】读取静态变量
        if (MyNetworkManager.GlobalPendingRoomId > 0)
        {
            Debug.Log($"[UI] Cancelling... Reconnecting to kill Room {MyNetworkManager.GlobalPendingRoomId}");
            
            // 重新连接大厅，利用 MyNetworkManager.OnClientConnect 里的逻辑自动发包
            OnButtonJoin(); 
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
        //AudioManager.Instance?.Play2D("UI点击（木头）");
        // --- 新增：触发 BGM 淡出 ---
        if (BGMController.Instance != null)
        {
            BGMController.Instance.StartFadeOut();
        }
        // -------------------------
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
        // --- 【修改这部分逻辑】 ---
        #if UNITY_EDITOR
            // 如果在编辑器中，根据 Dropdown 选择
            if (networkDropdown.value == 0)
            {
                manager.networkAddress = "localhost";
                Debug.Log($"[Connect] Editor Mode: Localhost");
            }
            else
            {
                manager.networkAddress = REMOTE_SERVER_IP;
                Debug.Log($"[Connect] Editor Mode: Remote Server ({REMOTE_SERVER_IP})");
            }
        #else
            // 如果是打包后的游戏，强制固定为服务器 IP
            manager.networkAddress = REMOTE_SERVER_IP;
            Debug.Log($"[Connect] Build Mode: Fixed Remote Server");
        #endif
        // ------------------------

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