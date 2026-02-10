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
    [Header("Skill UI")]
    // 将原本的单个变量改为数组，方便扩展
    public SkillSlotUI[] skillSlots; // 在 Inspector 中把你的 Q, E, R, F 对应的 UI 拖进去

    public GameObject blindPanel; //致盲面板
    [Header("Item UI")]
    public SkillSlotUI itemSlot; // 【新增】用于显示 F 键道具的 UI 槽位
    private void Awake()
    {
        // 1. 单例赋值
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 2. 自动寻找子物体里的技能槽
        // 这样就不怕 Inspector 里的引用丢失了
        if (skillSlots == null || skillSlots.Length == 0 || skillSlots[0] == null)
        {
            //Debug.LogWarning("[SceneScript] Skill Slots references missing, auto-finding in children...");
            
            // 查找所有子物体里的 SkillSlotUI 组件
            // includeInactive = true 确保即使物体是隐藏的也能找到
            skillSlots = GetComponentsInChildren<SkillSlotUI>(true);
            
            // 可选：为了确保顺序是 Q, E, R, F，可以按名字排个序
            // 这一步不是必须的，但如果你的物体名字是 Skill Q, Skill E... 这样会更稳
            System.Array.Sort(skillSlots, (a, b) => string.Compare(a.name, b.name));
            
            //Debug.Log($"[SceneScript] Auto-found {skillSlots.Length} skill slots.");
        }
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
        UpdateGoalProgressText(); // 【新增】更新目标文本
        // 如果处于 GameOver 状态，更新重启倒计时文字
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            if (gameRestartText != null)
            {
                gameRestartText.text = $"Returning to Lobby in {GameManager.Instance.restartCountdown}...";
            }
        }
    }
    private void UpdateGoalProgressText()
    {
        if (GameManager.Instance == null || GoalText == null) return;

        int delivered = GameManager.Instance.deliveredTreesCount;
        int total = GameManager.Instance.totalRequiredTrees;
        int remainingToWin = Mathf.Max(0, total - delivered);
        
        // 获取地图上还没被收回的古树数量
        int availableOnMap = GameManager.Instance.availableAncientTreesCount;

        GamePlayer local = NetworkClient.localPlayer?.GetComponent<GamePlayer>();
        
        string statusColor = (availableOnMap < remainingToWin) ? "red" : "white";

        if (remainingToWin <= 0 && total > 0)
        {
            GoalText.text = "<color=green>Requirement met! Survive!</color>";
        }
        else
        {
            // 拼接字符串：显示“还需带回数”和“地图剩余数”
            string goalInfo = local is WitchPlayer ? 
                $"Trees needed: <color=yellow>{remainingToWin}</color>" : 
                $"Witches need: <color=red>{remainingToWin}</color>";

            // 新增一行显示地图资源情况
            GoalText.text = $"{goalInfo}\n<color={statusColor}>Ancient Trees on Map: {availableOnMap}</color> (Team: {delivered}/{total})";
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
