using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using TMPro;
using UnityEngine.UI;

public class LobbyScript : NetworkBehaviour
{
    [Header("Game Settings (Synced)")]
    [SyncVar(hook = nameof(OnGameTimerChanged))] 
    public float syncedGameTimer = 300f;

    [SyncVar(hook = nameof(OnFriendlyFireChanged))] 
    public bool syncedFriendlyFire = false;

    [SyncVar(hook = nameof(OnMapIndexChanged))] 
    public int syncedMapIndex = 0;
    [SyncVar(hook = nameof(OnAnimalsNumberChanged))] 
    public int syncedAnimalsNumber = 10;
    // 新增：数值平衡设置
    [SyncVar(hook = nameof(OnSettingChanged))] public float syncedWitchHP = 100f;
    [SyncVar(hook = nameof(OnSettingChanged))] public float syncedWitchMana = 100f;
    [SyncVar(hook = nameof(OnSettingChanged))] public float syncedHunterSpeed = 7f;
    [SyncVar(hook = nameof(OnSettingChanged))] public int syncedTrapDifficulty = 2; // 挣脱点击数
    [SyncVar(hook = nameof(OnSettingChanged))] public float syncedManaRegen = 5f;



    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI playerNumberText; // 显示人数
    // [SerializeField] private Button btnReady;       // 准备按钮
    [SerializeField] private Button btnStartGame;   // 开始游戏按钮（仅房主可见）
    // [SerializeField] private Text txtReadyBtn; // 准备按钮上的文字
    [SerializeField] public TextMeshProUGUI roomStatusText; // 显示房间状态的文本
    // 状态同步
    [SyncVar(hook = nameof(OnPlayerCountChanged))] private int playerCount = 0;
    [SyncVar(hook = nameof(OnReadyCountChanged))] private int readyCount = 0;

    // 【新增】同步倒计时状态
    [SyncVar] private bool isGameStarting = false; 
    [SyncVar] private int countdownDisplay = 5;


    private bool myReadyState = false;
    [Header("UI List Settings")]
    public GameObject playerRowPrefab;  // 拖入刚才做的 Row Prefab
    public Transform playerListContent; // 拖入挂了 VerticalLayoutGroup 的那个容器物体
    // 用字典来记录：哪个 PlayerScript 对应 UI 里的哪一行
    private Dictionary<PlayerScript, PlayerRowUI> playerRows = new Dictionary<PlayerScript, PlayerRowUI>();
    
    private Coroutine countdownCoroutine; // 【新增】保存协程引用
    private void Start()
    {
        // 绑定按钮事件
        if(btnStartGame) btnStartGame.onClick.AddListener(OnClickStartGame);
        // 默认隐藏开始按钮，稍后判断权限开启
        if(btnStartGame) btnStartGame.gameObject.SetActive(false);
        
        foreach (var p in FindObjectsOfType<PlayerScript>())
        {
            AddPlayerRow(p);
        }
    }

    private void Update()
    {
        // 1. 服务器统计人数
        if (isServer)
        {
            UpdatePlayerCounts();
        }

        // 2. 【核心修改】UI 状态逻辑优化
        UpdateLobbyUI();
    }



    // 当有玩家进入时被调用
    public void AddPlayerRow(PlayerScript player)
    {
        if (playerRows.ContainsKey(player)) return; 

        // 1. 生成 UI 行
        GameObject newRow = Instantiate(playerRowPrefab, playerListContent);
        PlayerRowUI rowScript = newRow.GetComponent<PlayerRowUI>();

        // 绑定玩家数据
        rowScript.BindToPlayer(player);
        // 2. 绑定按钮逻辑
        // 先移除旧的监听器，好习惯
        rowScript.actionButton.onClick.RemoveAllListeners();

        if (player.isLocalPlayer)
        {
            // --- 情况 A: 这是我自己 ---
            rowScript.actionButton.gameObject.SetActive(true); // 显示按钮

            // 动态绑定点击事件：点击时切换准备状态
            rowScript.actionButton.onClick.AddListener(() => 
            {
                bool newState = !player.isReady; // 取反
                player.CmdSetReady(newState);    // 发送命令
            });
        }
        else
        {
            // --- 情况 B: 这是别人 ---
            // 隐藏按钮，因为我不能帮别人准备
            // (或者你可以把它改成 "Kick" 按钮，如果是房主的话)
            rowScript.actionButton.gameObject.SetActive(false); 
        }

        // 3. 初始化显示 (文字等)
        rowScript.UpdateInfo(player.playerName, player.isReady, player.isLocalPlayer);

        // 4. 存入字典
        playerRows.Add(player, rowScript);
    }


    // 当有玩家离开时被调用
    public void RemovePlayerRow(PlayerScript player)
    {
        if (playerRows.ContainsKey(player))
        {
            // 销毁 UI 物体
            Destroy(playerRows[player].gameObject);
            // 从记录中移除
            playerRows.Remove(player);
        }
    }
    // 当玩家改名或准备状态改变时调用
    public void UpdatePlayerRow(PlayerScript player)
    {
        if (playerRows.ContainsKey(player))
        {
            playerRows[player].UpdateInfo(player.playerName, player.isReady, player.isLocalPlayer);
        }
    }
    

    private void UpdateLobbyUI()
    {
        // 只有当 (总人数 > 0) 且 (准备人数 == 总人数) 时，才算全员准备好
        bool allReady = (playerCount > 0) && (readyCount == playerCount);

        // --- 逻辑 A: 倒计时阶段 ---
        if (isGameStarting)
        {
            if (roomStatusText != null)
            {
                roomStatusText.text = $"Game Starting in {countdownDisplay}...";
                roomStatusText.color = Color.yellow; // 倒计时显示黄色
            }
            
            // 倒计时开始后，禁用开始按钮
            if (btnStartGame != null)
            {
                btnStartGame.gameObject.SetActive(true);
                btnStartGame.interactable = false; 
                // 可选：更改按钮文字
                var btnText = btnStartGame.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText) btnText.text = "Launching...";
            }
        }
        // --- 逻辑 B: 等待阶段 ---
        else
        {
            if (btnStartGame != null)
            {
                btnStartGame.gameObject.SetActive(true);
                btnStartGame.interactable = allReady; // 只有全员准备好才能点
                
                var btnText = btnStartGame.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText) btnText.text = "Start";
            }

            if (roomStatusText != null)
            {
                if (allReady)
                {
                    roomStatusText.text = "All Ready! Waiting for Players to Start...";
                    roomStatusText.color = Color.green;
                }
                else
                {
                    roomStatusText.text = $"Waiting for Players... ({readyCount}/{playerCount} Ready)";
                    roomStatusText.color = Color.red;
                }
            }
        }
    }

    private void UpdateUI()
    {
        if (playerNumberText != null)
        {
            playerNumberText.text = $"Ready: {readyCount} / {playerCount}";
        }
    }

    // 更新本地按钮文字
    public void UpdateMyReadyStatus(bool isReady)
    {
        myReadyState = isReady;
        // if(txtReadyBtn) txtReadyBtn.text = isReady ? "Cancel Ready" : "Ready Up";
    }

    // 点击准备按钮
    public void OnClickReady()
    {
        // 安全获取本地玩家
        if (NetworkClient.connection == null || NetworkClient.connection.identity == null) return;
        
        var localPlayer = NetworkClient.connection.identity.GetComponent<PlayerScript>();
        if (localPlayer != null)
        {
            // 【关键】直接对当前状态取反，不要依赖中间变量
            bool newState = !localPlayer.isReady;
            localPlayer.CmdSetReady(newState);
            
            // 注意：这里不需要手动调用 UpdateMyReadyStatus
            // 我们让 PlayerScript 的 SyncVar Hook 来回调更新，这样数据才绝对同步
        }
    }



    // 点击开始游戏按钮
    public void OnClickStartGame()
    {
        var localPlayer = NetworkClient.connection.identity.GetComponent<PlayerScript>();
        if (localPlayer != null)
        {
            localPlayer.CmdStartGame();
        }
    }



    private void OnPlayerCountChanged(int _, int __) => UpdateUI();
    private void OnReadyCountChanged(int _, int __) => UpdateUI();




    
    // 【新增】服务器端倒计时协程
    [Server]
    public void StartGameCountdown()
    {
        // 防止重复触发
        if (isGameStarting) return;

        // StartCoroutine(CountdownRoutine());
        countdownCoroutine = StartCoroutine(CountdownRoutine());
    }

    // 【新增】取消倒计时的逻辑
    [Server]
    public void CancelCountdown()
    {
        if (!isGameStarting) return;

        Debug.Log("A player unreadied! Cancelling countdown...");

        if (countdownCoroutine != null)
        {
            StopCoroutine(countdownCoroutine);
            countdownCoroutine = null;
        }

        isGameStarting = false;
        countdownDisplay = 5; // 重置倒计时数字
        
        // UI 会在 UpdateLobbyUI 中自动根据 isGameStarting 的变化而切换回等待状态
    }

    [Server]
    private IEnumerator CountdownRoutine()
    {
        isGameStarting = true;
        countdownDisplay = 5;

        while (countdownDisplay > 0)
        {
            yield return new WaitForSeconds(1f);
            countdownDisplay--;
        }

        // 倒计时自然结束
        Debug.Log("Countdown finished, switching scene...");
        
        // 切换场景前确保状态正确
        isGameStarting = false; 

        GameManager.Instance.StartGame(); 
        NetworkManager.singleton.ServerChangeScene("MyScene");
        
        countdownCoroutine = null;
    }


    [Server]
    private void UpdatePlayerCounts()
    {
        playerCount = NetworkManager.singleton.numPlayers;
        int rCount = 0;
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) continue;
            var p = conn.identity.GetComponent<PlayerScript>();
            if (p != null && p.isReady) rCount++;
        }
        readyCount = rCount;
        // --- 【核心修改】检测是否需要取消倒计时 ---
        // 如果正在倒计时，但有人取消了准备 或 有人中途退出
        if (isGameStarting && (readyCount < playerCount || playerCount == 0))
        {
            CancelCountdown();
        }
    }
    // 无论谁改了，所有人都要刷新 UI 界面
    void OnGameTimerChanged(float oldV, float newV) => RefreshAllUI();
    void OnFriendlyFireChanged(bool oldV, bool newV) => RefreshAllUI();
    void OnMapIndexChanged(int oldV, int newV) => RefreshAllUI();
    void OnAnimalsNumberChanged(int oldV, int newV) => RefreshAllUI();
    // 统一的视觉刷新钩子
    void OnSettingChanged(float oldV, float newV) => RefreshAllUI();
    void OnSettingChanged(int oldV, int newV) => RefreshAllUI();
    private void RefreshAllUI()
    {
        if (LobbySettingsManager.Instance != null)
        {
            LobbySettingsManager.Instance.UpdateVisuals();
        }
    }
}