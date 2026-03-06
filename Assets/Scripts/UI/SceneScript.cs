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
    public GameObject revertProgressBar; // 拖入刚才创建的 Image
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
    [Header("Special Action Slots")]
    public SkillSlotUI morphSlot; // 在 Inspector 中拖入一个新的 SkillSlotUI 预制体（通常放在 Q/E 旁边）
    public Sprite morphIcon;      // 拖入一张代表变身的图标（如魔法棒或圈圈图标）
    public CircularProgressGlow revertProgressController; 
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
            revertProgressBar.SetActive(false);
        }
        if (RunText != null)
        {
            RunText.gameObject.SetActive(false);
        }
        if (ExecutionText != null)
        {
            ExecutionText.gameObject.SetActive(false);
        }
        // 初始化变身槽位显示
        if (morphSlot != null)
        {
            // 假设变身对应左键或右键，这里写 "LMB" 或 "Morph"
            morphSlot.Setup(morphIcon, "LMB"); 
        }

    }
    public void HideHUDForVictory()
    {
        // 隐藏基础信息
        if (RoleText != null) RoleText.gameObject.SetActive(false);
        if (NameText != null) NameText.gameObject.SetActive(false);
        if (WeaponText != null) WeaponText.gameObject.SetActive(false);
        if (PlayerCountText != null) PlayerCountText.gameObject.SetActive(false);
        if (GameTime != null)
        {
            GameTime.gameObject.SetActive(false);
            // 如果有父物体（比如背景），也一起隐藏
            if (GameTime.transform.parent != null)
            {
                GameTime.transform.parent.gameObject.SetActive(false);
            }
        }
        
        if (GoalText != null) GoalText.gameObject.SetActive(false);
        if (Crosshair != null) Crosshair.SetActive(false);
        
        // 隐藏状态条
        if (HealthSlider != null)
        {
            HealthSlider.gameObject.SetActive(false); 
            HealthSlider.gameObject.transform.parent.gameObject.SetActive(false); // 同时隐藏父物体，防止残留背景
        } 
        if (ManaSlider != null){} ManaSlider.gameObject.SetActive(false);
        {
            ManaSlider.gameObject.SetActive(false);
            ManaSlider.gameObject.transform.parent.gameObject.SetActive(false); // 同时隐藏父物体，防止残留背景
        }
        
        // 隐藏所有技能槽位
        if (skillSlots != null)
        {
            foreach (var slot in skillSlots)
            {
                if (slot != null) slot.gameObject.SetActive(false);
            }
        }
        
        // 隐藏道具和变身槽
        if (itemSlot != null) itemSlot.gameObject.SetActive(false);
        if (morphSlot != null) morphSlot.gameObject.SetActive(false);
        
        // 隐藏其他可能的提示文本
        if (RunText != null) RunText.gameObject.SetActive(false);
        if (ExecutionText != null) ExecutionText.gameObject.SetActive(false);
        if (blindPanel != null) blindPanel.SetActive(false);
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
            // 只有当 restartCountdown 被服务器明确设置为 20 以下（且大于 0）时，
            // 说明此时玩家已经在 VictoryZone 站好了，正在等回大厅
            if (gameRestartText != null && GameManager.Instance.restartCountdown > 0 && GameManager.Instance.restartCountdown <= 20)
            {
                gameRestartText.text = $"Returning to Lobby in <color=orange>{GameManager.Instance.restartCountdown}</color>";
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
            // 1. 基础胜利目标文本
            string goalInfo = local is WitchPlayer ? 
                $"Trees needed: <color=yellow>{remainingToWin}</color>" : 
                $"Witches need: <color=red>{remainingToWin}</color>";

            // 2. 地图资源统计
            string mapInfo = $"\n<color={statusColor}>Ancient Trees on Map: {availableOnMap}</color> (Team: {delivered}/{total})";

            // ----------------- 【核心修改：添加女巫奖励进度】 -----------------
            string rewardInfo = "";
            if (local is WitchPlayer witch)
            {
                // 计算当前这一轮奖励的进度 (例如 5/20)
                int currentProgress = witch.scoutedCount % witch.treesPerReward;
                
                // 如果有待领取的奖励，高亮显示
                if (witch.pendingRewards > 0)
                {
                    rewardInfo = $"\n<color=#FFD700>---REWARD READY: {witch.pendingRewards}---</color>";
                }
                else
                {
                    // 显示普通进度，使用紫色区分
                    rewardInfo = $"\n<color=#BB88FF>Scouting Reward: {currentProgress}/{witch.treesPerReward}</color>";
                }
            }
            // ----------------------------------------------------------------

            GoalText.text = goalInfo + mapInfo + rewardInfo;
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
        // 1. 安全检查：检查整个进度条组物体是否存在
        if (revertProgressBar == null) return;

        // 2. 控制整个 UI 组的显隐
        revertProgressBar.SetActive(isActive);

        // 3. 如果处于激活状态，且关联了高级控制器脚本
        if (isActive && revertProgressController != null)
        {
            // 调用我们之前写的 CircularProgressGlow 脚本里的 UpdateProgress 方法
            revertProgressController.UpdateProgress(progress);
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
            
            // --- 核心逻辑修改：醒目效果 ---
            if (timeLeft <= 60 && timeLeft > 0)
            {
                // 1. 颜色变红
                GameTime.color = Color.red;

                // 2. 添加呼吸脉冲缩放效果 (醒目表现)
                // 基于正弦波计算缩放值，范围在 1.0 到 1.25 之间
                // 使用 Time.time * 5f 让脉冲速度随紧急感稍微加快
                float pulse = 1.0f + (Mathf.Sin(Time.time * 5f) * 0.15f);
                GameTime.transform.localScale = new Vector3(pulse, pulse, 1f);

                // 可选：添加轻微的抖动或在最后 10 秒加快脉冲速度
                if (timeLeft <= 10)
                {
                    float fastPulse = 1.0f + (Mathf.Sin(Time.time * 10f) * 0.25f);
                    GameTime.transform.localScale = new Vector3(fastPulse, fastPulse, 1f);
                }
            }
            else
            {
                // 恢复默认状态
                GameTime.color = Color.white;
                GameTime.transform.localScale = Vector3.one;
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
    public void ShowVictoryUI(PlayerRole winner)
    {
        gameResultPanel.SetActive(true);
        gameResultText.text = (winner == PlayerRole.Witch) ? "WITCHES TRIUMPH!" : "HUNTERS TRIUMPH!";
        
        // 3秒后自动隐藏结果文字，展示风景
        StartCoroutine(FadeOutResultText());
    }

    private IEnumerator FadeOutResultText()
    {
        yield return new WaitForSeconds(3f);
        gameResultText.gameObject.SetActive(false);
    }
}
