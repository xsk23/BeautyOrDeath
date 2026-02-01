using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class SceneScript : MonoBehaviour
{
    public static SceneScript Instance { get; private set; } // 单例方便访问
    public TextMeshProUGUI RoleText;//显示角色的文本
    public TextMeshProUGUI NameText;//显示名字的文本
    public TextMeshProUGUI WeaponText;//显示当前武器\道具的文本
    public Slider HealthSlider;//血量滑动条
    public Slider ManaSlider;//法力值滑动条
    public TextMeshProUGUI PlayerCountText;//显示玩家数量的文本
    public TextMeshProUGUI RunText;//女巫小动物形态逃跑即复活提示文本

    [Header("Pause Menu")]
    public GameObject pauseMenuPanel; // 【新增】拖入你的暂停菜单Panel
    private bool isPaused = false; // 记录当前是否暂停
    public TextMeshProUGUI GameTime;//显示游戏时间的文本
    public TextMeshProUGUI GoalText;//显示目标的文本
    public GameObject Crosshair;//准心
    [Header("Witch UI")]
    public Image revertProgressBar; // 拖入刚才创建的 Image
    [Header("Hunter UI")]
    public TextMeshProUGUI ExecutionText;//显示猎人处决提示文本
    [Header("Result UI")]
    public GameObject gameResultPanel;     // 结算面板根物体
    public TextMeshProUGUI gameResultText; // 显示 "Hunters Win!"
    public TextMeshProUGUI gameRestartText;// 显示 "Restarting in 5..."
    private void Awake()
    {
        Instance = this;
    }
    private void Start()
    {
        // 初始隐藏结算面板
        if (gameResultPanel != null) gameResultPanel.SetActive(false);
        // 游戏开始时隐藏暂停菜单
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(false);
        }
        if(revertProgressBar != null)
        {
            revertProgressBar.gameObject.SetActive(false);
        }
        if (RunText != null)
        {
            RunText.gameObject.SetActive(false);
        }
        if (ExecutionText != null)
        {
            ExecutionText.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        // 【新增】更新倒计时显示
        UpdateGameTimer();
        // 每一帧或每隔几帧更新人数（简单粗暴但有效）
        UpdateAlivePlayerCount(); 
        // 如果处于 GameOver 状态，更新重启倒计时文字
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            if (gameRestartText != null)
            {
                gameRestartText.text = $"Returning to Lobby in {GameManager.Instance.restartCountdown}...";
            }
        }
    }

    // 供 GameManager 调用显示结果
    public void ShowGameResult(PlayerRole winner)
    {
        if (gameResultPanel == null) return;

        gameResultPanel.SetActive(true);
        
        if (gameResultText != null)
        {
            if (winner == PlayerRole.Hunter)
            {
                gameResultText.text = "<color=#00FFFF>HUNTERS WIN!</color>";
            }
            else if (winner == PlayerRole.Witch)
            {
                gameResultText.text = "<color=#FF00FF>WITCHES WIN!</color>";
            }
        }
        
        // 游戏结束时解锁鼠标，方便点击可能的按钮（虽然现在是自动重启）
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void UpdateRevertUI(float progress, bool isActive)
    {
        if (revertProgressBar == null) return;

        // 设置显示或隐藏
        revertProgressBar.gameObject.SetActive(isActive);
        
        // 设置进度
        if (isActive)
        {
            revertProgressBar.fillAmount = progress;
        }
    }

    public void UpdateAlivePlayerCount()
    {
        if (PlayerCountText == null || GameManager.Instance == null) return;

        // 直接从 GameManager 读取服务器同步过来的人数
        int hunters = GameManager.Instance.aliveHuntersCount;
        int witches = GameManager.Instance.aliveWitchesCount;

        // 更新 UI
        PlayerCountText.text = $"<color=#00FFFF>Hunters: {hunters}</color> | <color=#FF00FF>Witches: {witches}</color>";
    }

    // 更新时间显示的逻辑
    private void UpdateGameTimer()
    {
        // 确保 UI 组件存在，且 GameManager 单例存在
        if (GameTime != null && GameManager.Instance != null)
        {
            float timeLeft = GameManager.Instance.gameTimer;
            
            // 防止显示负数
            if (timeLeft < 0) timeLeft = 0;

            // 计算分和秒
            int minutes = Mathf.FloorToInt(timeLeft / 60);
            int seconds = Mathf.FloorToInt(timeLeft % 60);

            // 格式化字符串为 05:00 格式
            GameTime.text = string.Format("{0:00}:{1:00}", minutes, seconds);
            
            // 可选：时间少于30秒变红
            if (timeLeft <= 30 && timeLeft > 0)
            {
                GameTime.color = Color.red;
            }
            else
            {
                GameTime.color = Color.white;
            }
        }
    }

    // 【新增】切换暂停菜单状态 (供 GamePlayer 调用)
    public void TogglePauseMenu()
    {
        if (pauseMenuPanel == null) return;

        isPaused = !isPaused;
        UpdateMenuState();
    }

    // 【新增】按钮点击：回到游戏
    public void ButtonResumeGame()
    {
        isPaused = false;
        UpdateMenuState();
    }

    // 更新菜单显示和鼠标状态的核心逻辑
    private void UpdateMenuState()
    {
        if (pauseMenuPanel != null)
        {
            pauseMenuPanel.SetActive(isPaused);
        }

        if (isPaused)
        {
            // 暂停状态：解锁鼠标，显示指针
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // 游戏状态：锁定鼠标，隐藏指针
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // 按钮点击：退出游戏 (原有逻辑微调)
    public void ButtonQuitGame()
    {
        // 确保鼠标解锁，否则回到大厅可能看不到鼠标
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log("尝试退出游戏");
        
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            // Host
            NetworkManager.singleton.StopHost();
        }
        else if (NetworkClient.isConnected)
        {
            // Client
            NetworkManager.singleton.StopClient();
        }
        else if (NetworkServer.active)
        {
            // Server only
            NetworkManager.singleton.StopServer();
        }
    }

}
