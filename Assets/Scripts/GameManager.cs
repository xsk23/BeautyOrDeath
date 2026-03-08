using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

// 【关键修改】不再继承 SingletonAutoMono，直接继承 NetworkBehaviour
public class GameManager : NetworkBehaviour
{
    // 手动实现静态实例，方便访问
    public static GameManager Instance { get; private set; }
    private ServerAnimalSpawner animalSpawner;
    public enum GameState
    {
        Lobby,
        InGame,
        Paused,
        GameOver
    }
    [SyncVar] 
    private GameState currentState = GameState.Lobby;

    public GameState CurrentState
    {
        get { return currentState; }
    }
    [SyncVar]
    public float gameTimer = 300f; // 5分钟倒计时
    // 【新增】持久化设置存储（用于跨场景）
    private float witchHPInternal = 100f;
    private float witchManaInternal = 100f;
    private float hunterSpeedInternal = 7f;
    private int trapDifficultyInternal = 2;
    private float manaRegenInternal = 5f;
    private int animalsToSpawnInternal = 10;
    private bool friendlyFireInternal = false; // 【新增】
    private float hunterRatioInternal = 0.3f; // 猎人比例
    private float ancientRatioInternal = 1.5f; // 【新增】内部固化变量
    public bool FriendlyFire => friendlyFireInternal; // 提供一个只读访问接口

    [SyncVar(hook = nameof(OnWinnerChanged))]
    public PlayerRole gameWinner = PlayerRole.None;
    [SyncVar]
    public int restartCountdown = 5;
    [Header("Alive Stats (Synced)")]
    [SyncVar] public int aliveHuntersCount = 0;
    [SyncVar] public int aliveWitchesCount = 0;
    [Header("Ancient Tree Goal")]
    [SyncVar(hook = nameof(OnGoalProgressChanged))]
    public int deliveredTreesCount = 0; // 已带回的数量
    [SyncVar]
    public int totalRequiredTrees = 0; // 总共需要的数量（初始女巫人数）
    [Header("Ancient Tree Stats")]
    [SyncVar] public int availableAncientTreesCount = 0; // 【新增】地图上剩余可用的古树总数

    private float gameStartTimer = 0f;
    private const float winConditionGracePeriod = 10f; // 10秒保护期，等待所有玩家加载

    [Header("Portal Settings")]
    public GameObject portalPrefab; // 这里的引用将在 Prefab 中设置
    public string portalSpawnGroupName = "PortalPositions"; 

    [Header("Physics Settings")]
    public LayerMask propLayer; // 用于检测树木/道具的层级
    public Dictionary<int, Gender> pendingGenders = new Dictionary<int, Gender>();
    public Dictionary<int, string> pendingItems = new Dictionary<int, string>();
    [Header("胜利表现配置")]
    public VictoryAnimData witchVictoryData;   // 拖入巫师胜利的 SO
    public VictoryAnimData hunterVictoryData;  // 拖入猎人胜利的 SO
    public float victoryModelSpacing = 2.0f;   // 胜利者之间的间隔
    [Header("音频设置")]
    public AudioSource victoryAudioSource; // 在 Inspector 中把 GameManager 身上挂的 AudioSource 拖进来
    [Header("失败表现配置")]
    public RuntimeAnimatorController failAnimatorController; // 在 Inspector 中拖入你的 failanimation.controller
    [Header("视频配置")]
    public float witchVictoryVideoDuration = 12f; // 视频文件的长度（秒）
    // 提供一个接口供 TreeManager 获取计算后的古树总数
    [Server]
    public int GetCalculatedAncientTreeCount()
    {
        // 统计初始分配的女巫人数 (pendingRoles 存储了分配结果)
        int initialWitchCount = 0;
        foreach(var role in pendingRoles.Values)
        {
            if(role == PlayerRole.Witch) initialWitchCount++;
        }
        
        // 计算并取整 (使用 Mathf.RoundToInt 实现 1.5x2=3 的逻辑)
        return Mathf.Max(1, Mathf.RoundToInt(initialWitchCount * ancientRatioInternal));
    }

    [Server]
    public void RegisterTreeDelivery()
    {
        deliveredTreesCount++;
        Debug.Log($"[Server] Tree Delivered! Progress: {deliveredTreesCount}/{totalRequiredTrees}");
        
        // 检查胜利条件
        if (deliveredTreesCount >= totalRequiredTrees && totalRequiredTrees > 0)
        {
            ServerEndGame(PlayerRole.Witch);
        }
    }

    // 当进度改变时，客户端同步更新 UI
    void OnGoalProgressChanged(int oldVal, int newVal)
    {
        // 触发 SceneScript 更新文本（我们稍后在 SceneScript 里实现）
    }

    // 当获胜者确定时，客户端回调
    void OnWinnerChanged(PlayerRole oldW, PlayerRole newW)
    {
        if (newW != PlayerRole.None)
        {
            SceneScript.Instance?.ShowGameResult(newW);
        }
    }

    private void Awake()
    {
        // 严格的单例检查
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        Instance = this;

        // 确保切换场景不销毁 (客户端和服务器都需要)
        DontDestroyOnLoad(gameObject);
    }

    [Server]
    public void ServerPlay3DAt(string soundName, Vector3 position)
    {
        RpcPlay3D(soundName, position);
    }

    [ClientRpc]
    private void RpcPlay3D(string soundName, Vector3 position)
    {
        AudioManager.Instance?.Play3D(soundName, position);
    }

    // 【新增】服务器端更新时间
    [ServerCallback]
    private void Update()
    {
        if (currentState == GameState.InGame)
        {
            if (gameTimer > 0)
            {
                gameTimer -= Time.deltaTime;
            }
            else
            {
                gameTimer = 0;
                // EndGame(); 
                // 时间到，如果女巫没完成任务（目前默认逻辑），猎人胜
                ServerEndGame(PlayerRole.Hunter); 
            }
            // 2. 【核心修改】统计人数并检查胜负
            UpdateAliveCountsAndCheckWin();
        }
    }


    [Server]
    private void UpdateAliveCountsAndCheckWin()
    {
        // 如果没有玩家，或者还在加载中，不进行胜负判定
        if (GamePlayer.AllPlayers.Count == 0) return;
        if (currentState != GameState.InGame) return;
        
        // --- 新增：如果游戏刚开始不到 10 秒，不进行“人数归零”的胜负判定 ---
        // 这样可以等所有猎人和女巫都加载进场
        if (Time.time - gameStartTimer < winConditionGracePeriod) return;

        int hunters = 0;
        int witchesAlive = 0;
        int witchesFinishedButDead = 0; // 记录那些完成了任务但死掉的女巫
        int totalWitchesEver = 0; 

        // 此时遍历的是服务器端的 AllPlayers 列表
        foreach (var player in GamePlayer.AllPlayers)
        {
            if (player == null) continue;

            if (player.playerRole == PlayerRole.Hunter)
            {
                if (!player.isPermanentDead) hunters++;
            }
            else if (player.playerRole == PlayerRole.Witch)
            {
                totalWitchesEver++;
                WitchPlayer witch = (WitchPlayer)player;

                if (!witch.isPermanentDead)
                {
                    witchesAlive++;
                }
                else if (witch.hasDeliveredTree)
                {
                    // 虽然她死了，但她生前带回了树，这颗树应该保留在总目标里作为“已完成”的占位
                    witchesFinishedButDead++;
                }
            }
        }

        // 更新同步变量
        aliveHuntersCount = hunters;
        aliveWitchesCount = witchesAlive;

        // 【核心修改】动态更新总目标
        // 目标数 = 活着的女巫 + 死了但生前完成任务的女巫
        totalRequiredTrees = witchesAlive + witchesFinishedButDead;

        // ==========================================
        // 修正后的判定逻辑
        // ==========================================
        
        // 1. 女巫胜判定：带回的树 满足了 动态目标（且目标必须 > 0，防止加载瞬间判定）
        if (totalRequiredTrees > 0 && deliveredTreesCount >= totalRequiredTrees)
        {
            Debug.Log($"[Server] Witches Win! Goal reached: {deliveredTreesCount}/{totalRequiredTrees}");
            ServerEndGame(PlayerRole.Witch);
            return; // 胜负已分，跳出
        }

        // 2. 猎人胜判定：
        // 条件 A：场上曾经有过女巫 (totalWitchesEver > 0)
        // 条件 B：当前活着的女巫为 0 (aliveWitchesCount == 0)
        // 注意：因为上面已经拦截了“女巫胜”，所以运行到这里说明女巫没能在死前交够树
        if (totalWitchesEver > 0 && aliveWitchesCount == 0)
        {
            Debug.Log($"[Server] Hunters Win! All witches eliminated without completing task.");
            ServerEndGame(PlayerRole.Hunter);
            return;
        }
        
        // 3. 猎人胜判定（特殊情况）：如果猎人全灭，女巫自动胜利（可选）
        if (hunters == 0 && totalWitchesEver > 0)
        {
            Debug.Log($"[Server] Witches Win! No hunters remain.");
            ServerEndGame(PlayerRole.Witch);
        }
    }

    [Server]
    public void ServerEndGame(PlayerRole winner)
    {
        // 【关键修复 1】如果已经处理过结束，直接跳出
        if (currentState == GameState.GameOver) return;

        // --- 新增：把倒计时归零，避免触发 SceneScript 里的 UI 覆盖 ---
        restartCountdown = 0; 

        // 【关键修复 2】立即切换状态，阻断 Update 的再次进入
        SetGameState(GameState.GameOver);
        gameWinner = winner;
        
        // 开启新的胜利序列协程
        StartCoroutine(VictorySequenceRoutine(winner));
    }
    [Server]
    private IEnumerator VictorySequenceRoutine(PlayerRole winner)
    {
        // --- 新增：转场前的倒计时 UI 表现 ---
        for (int i = 5; i > 0; i--)
        {
            RpcUpdateVictoryTransitionUI(winner, i);
            yield return new WaitForSeconds(1f);
        }
        // 【新增】转场开始时，正式进入 GameOver 状态
        // SetGameState(GameState.GameOver);
        
        // 【关键修复】在统计胜败者之前，先清理 AllPlayers 中的无效引用
        GamePlayer.CleanupDeadReferences();
        Debug.Log($"[Server] Cleaned up AllPlayers. Current count: {GamePlayer.AllPlayers.Count}");
        
        // 统计胜利者与失败者
        List<GamePlayer> winners = new List<GamePlayer>();
        List<GamePlayer> losers = new List<GamePlayer>();
        foreach (var p in GamePlayer.AllPlayers)
        {
            if (p == null) continue;

            // 【关键修改点】：
            // 判定为胜利者的条件：属于获胜阵营 并且 没有永久死亡
            if (p.playerRole == winner && !p.isPermanentDead) 
            {
                winners.Add(p);
            }
            else 
            {
                // 阵营不对，或者阵营对了但是人死了，都算作失败者（Loser）
                losers.Add(p);
            }
        }
        // --- 阶段 2：播放视频 (如果是巫师胜利) ---
        if (winner == PlayerRole.Witch)
        {
            // 通知所有客户端播放视频
            RpcPlayVictoryVideo(witchVictoryVideoDuration);
            
            // 服务器等待视频播完
            yield return new WaitForSeconds(witchVictoryVideoDuration);
        }
        // 2. 【核心修改】由服务器选定这局用哪套舞蹈
        VictoryAnimData animData = (winner == PlayerRole.Witch) ? witchVictoryData : hunterVictoryData;
        int selectedDanceIndex = animData.GetRandomConfigIndex(winners.Count);
        // 2. 通知所有客户端切换相机 (传入胜方以便客户端选配置)
        RpcNotifyVictorySequence(winner, selectedDanceIndex);

        // 4. 生成模型 (传入所选索引)
        SetupVictoryStage(winner, winners, losers, selectedDanceIndex);

        // 【核心修复】：在这里实现真正的 20 秒倒计时同步
        restartCountdown = 20; 
        while (restartCountdown > 0)
        {
            yield return new WaitForSeconds(1f);
            restartCountdown--;
            // 因为 restartCountdown 是 SyncVar，改变它会自动同步到所有客户端的 SceneScript
        }
        RpcStopVictoryMusic(); // 先通知所有客户端停掉音乐
        ResetGame();
        NetworkManager.singleton.ServerChangeScene(MyNetworkManager.singleton.onlineScene);
    }
    [ClientRpc]
    private void RpcPlayVictoryVideo(float duration)
    {
        if (SceneScript.Instance != null)
        {
            // 隐藏 HUD 以便看清视频
            SceneScript.Instance.HideHUDForVictory();
            SceneScript.Instance.PlayVictoryVideo(duration);
        }
    }
    [ClientRpc]
    private void RpcUpdateVictoryTransitionUI(PlayerRole winner, int seconds)
    {
        if (SceneScript.Instance == null) return;
        SceneScript.Instance.gameResultPanel.SetActive(true);
        string teamName = (winner == PlayerRole.Witch) ? "<color=#FF00FF>WITCHES</color>" : "<color=#00FFFF>HUNTERS</color>";
        SceneScript.Instance.gameResultText.text = $"{teamName} TRIUMPH!";
        SceneScript.Instance.gameRestartText.text = $"Moving to Victory Zone in {seconds}...";
    }

    [ClientRpc]
    private void RpcNotifyVictorySequence(PlayerRole winner, int danceIndex)
    {
        Camera mainCam = Camera.main;
        if (mainCam == null) return;

        mainCam.transform.SetParent(null);

        // 获取对应的胜利配置
        VictoryAnimData animData = (winner == PlayerRole.Witch) ? witchVictoryData : hunterVictoryData;

        // 【核心修改】：从 CameraData 资源读取位置和旋转
        if (animData != null && animData.cameraSettings != null)
        {
            mainCam.transform.position = animData.cameraSettings.position;
            mainCam.transform.rotation = Quaternion.Euler(animData.cameraSettings.eulerRotation);
            Debug.Log($"[Victory] Camera applied from CameraData Asset: {animData.cameraSettings.name}");
        }

        // 2. UI 深度清理
        if (SceneScript.Instance != null)
        {
            // --- 调用刚才写的方法隐藏所有 HUD ---
            SceneScript.Instance.HideHUDForVictory();

            // --- 处理结算面板 ---
            SceneScript.Instance.gameResultPanel.SetActive(true); 

            // 隐藏胜利大标题文字 (按照你的需求)
            if (SceneScript.Instance.gameResultText != null)
            {
                SceneScript.Instance.gameResultText.gameObject.SetActive(false);
            }

            // 背景设为全透明 (按照你的需求)
            UnityEngine.UI.Image panelImage = SceneScript.Instance.gameResultPanel.GetComponent<UnityEngine.UI.Image>();
            if (panelImage != null)
            {
                Color c = panelImage.color;
                c.a = 0f; 
                panelImage.color = c;
            }
            
            // 确保重启倒计时文本是可见的（因为它通常在 ResultPanel 下面）
            if (SceneScript.Instance.gameRestartText != null)
            {
                SceneScript.Instance.gameRestartText.gameObject.SetActive(true);
            }
        }
        var localPlayer = NetworkClient.localPlayer?.GetComponent<GamePlayer>();
        if (localPlayer != null) localPlayer.isPermanentDead = true; 
        // --- 【新增：立即刷新本地所有视觉脚本】 ---
        if (localPlayer != null)
        {
            localPlayer.GetComponent<TeamVision>()?.ForceUpdateVisuals();
        }
        // --- 新增：音乐播放逻辑 ---
        // 1. 获取胜利者人数（这里假设是基于当前阵营的存活/参与人数）
        // 注意：这里的 winnersCount 必须和生成模型时的人数一致
        List<GamePlayer> winners = new List<GamePlayer>();
        foreach (var p in GamePlayer.AllPlayers)
        {
            // 【关键修改点】：判定逻辑必须与服务器一致
            if (p != null && p.playerRole == winner && !p.isPermanentDead) 
            {
                winners.Add(p);
            }
        }
        if (animData != null)
        {
            // 【修改】根据服务器给的索引获取配置
            GroupDanceConfig config = animData.GetConfigByIndex(danceIndex);
            
            // 3. 播放音乐
            if (config.victoryMusic != null && victoryAudioSource != null)
            {
                victoryAudioSource.clip = config.victoryMusic;
                victoryAudioSource.loop = true; // 舞蹈通常是循环的
                victoryAudioSource.Play();
                Debug.Log($"[Victory] Playing music: {config.victoryMusic.name} for {winners.Count} players.");
            }
        }
    }


    [Server]
    private void SetupVictoryStage(PlayerRole winner, List<GamePlayer> winners, List<GamePlayer> losers, int danceIndex)
    {
        // 【新增调试日志】显示胜败者统计
        Debug.Log($"[Server] SetupVictoryStage: Winners={winners.Count}, Losers={losers.Count}");
        
        GameObject stageCenter = GameObject.Find("VictoryStageCenter");
        Vector3 centerPos = stageCenter ? stageCenter.transform.position : new Vector3(-180, 10, 140);
        
        // 获取配置数据中的相机位置，用于让模型面朝相机
        VictoryAnimData animData = (winner == PlayerRole.Witch) ? witchVictoryData : hunterVictoryData;
        if (animData == null || animData.cameraSettings == null) return;

        RpcHideOriginalPlayers();
        MyNetworkManager netManager = NetworkManager.singleton as MyNetworkManager;
        // RuntimeAnimatorController[] anims = animData.GetAnimatorsForCount(winners.Count);

        // --- 1. 生成胜利者 (中间排列，面朝相机) ---
        float tightSpacing = 1.1f; // 间距从 2.0 缩小到 1.1，肩膀挨着肩膀
        for (int i = 0; i < winners.Count; i++)
        {
            float offset = (i - (winners.Count - 1) / 2f) * tightSpacing;
            Vector3 spawnPos = centerPos + (stageCenter.transform.right * offset);
            
            // 【核心修改】：计算指向 CameraData 中定义的相机位置的旋转
            Vector3 dirToCam = (animData.cameraSettings.position - spawnPos).normalized;
            dirToCam.y = 0;
            Quaternion lookRotation = Quaternion.LookRotation(dirToCam);

            // 【修改点】传入 true
            GameObject prefab = GetVictoryPrefab(winners[i], netManager, true); 
            if (prefab != null)
            {
                GameObject displayObj = Instantiate(prefab, spawnPos, lookRotation);
                NetworkServer.Spawn(displayObj);
                // 【关键修复 1】通知所有客户端禁用该物体的玩家逻辑
                RpcDisablePlayerLogic(displayObj);
                // 【修改】传入选中的 danceIndex
                RpcApplyVictoryAnimation(displayObj, danceIndex, i, winner);
                RpcSetVictoryModelName(displayObj, winners[i].playerName, winners[i].playerRole);
                
                // 【新增】如果胜利者是猎人，隐藏武器
                if (winners[i].playerRole == PlayerRole.Hunter)
                {
                    RpcHideHunterWeapons(displayObj);
                }
            }
        }

        // --- 2. 失败者生成 (核心修改：侧身朝向) ---
        for (int j = 0; j < losers.Count; j++)
        {
            bool isLeft = (j % 2 == 0);
            // 站位更紧凑：侧向距离 2.2 -> 1.8，深度距离 1.5 -> 1.2
            float sideOffset = isLeft ? -1.8f : 1.8f; 
            float depthOffset = 1.2f + (j / 2) * 0.7f; 
            
            Vector3 loserSpawnPos = centerPos + (stageCenter.transform.right * sideOffset) - (stageCenter.transform.forward * depthOffset);
            
            // --- 计算侧身旋转 ---
            Vector3 dirToWinners = (centerPos - loserSpawnPos).normalized; // 指向舞台中心的向量
            Vector3 dirToCam = (animData.cameraSettings.position - loserSpawnPos).normalized; // 指向相机的向量
            
            // 使用 Slerp 进行混合：0.4f 代表 40% 看向相机，60% 看向中心
            // 这样会产生一种“斜对着镜头”的高级感
            Vector3 blendedDir = Vector3.Slerp(dirToWinners, dirToCam, 0.4f);
            blendedDir.y = 0; // 确保不仰头或低头
            
            Quaternion loserRot = Quaternion.LookRotation(blendedDir);

            GameObject lPrefab = GetVictoryPrefab(losers[j], netManager, false);
            if (lPrefab != null)
            {
                GameObject loserObj = Instantiate(lPrefab, loserSpawnPos, loserRot);
                
                // 【关键修改】：不再禁用 Animator，而是交给客户端去初始化
                NetworkServer.Spawn(loserObj);
                // 【关键修复 2】同样禁用失败者的逻辑
                RpcDisablePlayerLogic(loserObj);                
                // 1. 设置名字（你原有的）
                RpcSetVictoryModelName(loserObj, losers[j].playerName, losers[j].playerRole);
                
                // 2. 【新增】调用自动挂载 Animator 的 RPC
                RpcSetupLoserFailLogic(loserObj);
                // ==========================================
                // 【新增修改】如果失败者也是猎人，同样需要隐藏武器
                // ==========================================
                if (losers[j].playerRole == PlayerRole.Hunter)
                {
                    RpcHideHunterWeapons(loserObj);
                }
            }
        }
    }
    [ClientRpc]
    private void RpcDisablePlayerLogic(GameObject targetObj)
    {
        if (targetObj == null) return;

        // 1. 禁用所有业务脚本
        MonoBehaviour[] allScripts = targetObj.GetComponents<MonoBehaviour>();
        foreach (var s in allScripts)
        {
            if (s is GamePlayer || s is HunterPlayer || s is WitchPlayer || s is TeamVision || s is CharacterController)
            {
                s.enabled = false;
            }
        }

        // 2. 彻底移除 CharacterController 的影响
        CharacterController cc = targetObj.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        // 3. 强制清空 Animator 的旧参数，防止它跳回 Lobby 动画
        Animator anim = targetObj.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.enabled = true;
            foreach (var param in anim.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Float) anim.SetFloat(param.name, 0f);
                if (param.type == AnimatorControllerParameterType.Bool) anim.SetBool(param.name, false);
            }
        }
    }

    [ClientRpc]
    private void RpcSetupLoserFailLogic(GameObject loserObj)
    {
        if (loserObj == null) return;

        // 1. 【核心修复】禁用原有的玩家逻辑脚本，防止它去更新 "speed" 参数
        MonoBehaviour[] allScripts = loserObj.GetComponents<MonoBehaviour>();
        foreach (var s in allScripts)
        {
            // 禁用除本脚本和 RandomAnimationPlayer 以外的所有逻辑
            if (s is GamePlayer || s is HunterPlayer || s is WitchPlayer || s is TeamVision)
            {
                s.enabled = false;
            }
        }

        // 2. 获取子物体上的 Animator
        Animator anim = loserObj.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            if (failAnimatorController != null)
            {
                anim.runtimeAnimatorController = failAnimatorController;
                anim.enabled = true;
            }
        }

        // 3. 挂载随机播放脚本
        RandomAnimationPlayer randomPlayer = loserObj.GetComponent<RandomAnimationPlayer>();
        if (randomPlayer == null)
        {
            randomPlayer = loserObj.AddComponent<RandomAnimationPlayer>();
        }
        
        randomPlayer.stateNames = new string[] { "sad_idle", "sad_idle 0", "sad_idle 1" };
    }

    [ClientRpc]
    private void RpcHideHunterWeapons(GameObject hunterObj)
    {
        if (hunterObj == null) return;

        Debug.Log($"[Victory] Hiding weapons for display hunter model: {hunterObj.name}");
        
        int hiddenCount = 0; // 【修复】声明计数变量
            
        // 【修复】直接从传入的展示模型 (hunterObj) 获取 HunterPlayer 组件
        HunterPlayer hunter = hunterObj.GetComponent<HunterPlayer>();
        
        if (hunter != null && hunter.hunterWeapon != null)
        {
            foreach (GameObject weapon in hunter.hunterWeapon)
            {
                if (weapon != null)
                {
                    weapon.SetActive(false);
                    hiddenCount++;
                    Debug.Log($"[Victory] Hidden hunter weapon: {weapon.name}");
                }
            }
            Debug.Log($"[Victory] Hid all {hunter.hunterWeapon.Length} weapons for hunter: {hunter.playerName}");
        }
        
        Debug.Log($"[Victory] Total display weapons hidden: {hiddenCount}");
    }

    // 【新增 Rpc】专门用于在客户端设置展示物体的名字
    [ClientRpc]
    private void RpcSetVictoryModelName(GameObject modelObj, string pName, PlayerRole role)
    {
        if (modelObj == null) return;

        // 1. 寻找名字组件
        TMPro.TextMeshPro textComp = modelObj.GetComponentInChildren<TMPro.TextMeshPro>();
        if (textComp != null)
        {
            textComp.text = pName;
            textComp.gameObject.SetActive(true);
            textComp.color = (role == PlayerRole.Witch) ? Color.magenta : Color.cyan;

            // 2. 寻找动画模型中的骨骼（比如头部）
            // 建议在 Animator 所在的物体下寻找
            Transform headBone = FindRecursive(modelObj.transform, "CC_Base_Spine01"); 
            
            // 如果没找到名为 "Head" 的，尝试寻找通用节点
            if (headBone == null) headBone = modelObj.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Head);

            // 3. 挂载跟随逻辑
            if (headBone != null)
            {
                VictoryNameFollow follower = textComp.gameObject.GetComponent<VictoryNameFollow>();
                if (follower == null) follower = textComp.gameObject.AddComponent<VictoryNameFollow>();
                
                follower.targetBone = headBone;
                follower.offset = new Vector3(0, -0.6f, 0); // 根据模型大小微调
            }
        }
    }
    // 辅助方法：递归查找指定名称的子物体
    private Transform FindRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name.Contains(name)) return child;
            Transform result = FindRecursive(child, name);
            if (result != null) return result;
        }
        return null;
    }
    [ClientRpc]
    private void RpcApplyVictoryAnimation(GameObject targetObj, int danceIndex, int positionIndex, PlayerRole winner)
    {
        if (targetObj == null) return;

        VictoryAnimData animData = (winner == PlayerRole.Witch) ? witchVictoryData : hunterVictoryData;
        if (animData == null) return;

        // 【修改】直接通过索引拿配置
        GroupDanceConfig config = animData.GetConfigByIndex(danceIndex);
        // 【排查点】确保你的 individualAnimators 数组长度 >= winners 的人数
        if (config.individualAnimators != null && positionIndex < config.individualAnimators.Length)
        {
            Animator anim = targetObj.GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.runtimeAnimatorController = config.individualAnimators[positionIndex];
                // 跳舞通常需要开启 Root Motion，否则模型会原地踏步
                anim.applyRootMotion = true; 
                
                // 强制从第0帧开始播放，防止逻辑卡在旧状态
                anim.Play(0, -1, 0f); 
            }
        }
        else
        {
            Debug.LogError($"[Victory] 动画配置不足! 舞蹈:{config.danceName}, 需要索引:{positionIndex}, 但数组只有:{config.individualAnimators.Length}");
        }
    }
    // 修改辅助方法：增加 isWinner 参数
    private GameObject GetVictoryPrefab(GamePlayer player, MyNetworkManager netManager, bool isWinner)
    {
        if (player.playerRole == PlayerRole.Witch)
        {
            if (isWinner)
            {
                // 胜利的女巫使用 Young 模型
                return (player.myGender == Gender.Male) ? netManager.youngWitchMalePrefab : netManager.youngWitchFemalePrefab;
            }
            else
            {
                // 失败的女巫使用原始模型
                return (player.myGender == Gender.Male) ? netManager.witchMalePrefab : netManager.witchFemalePrefab;
            }
        }
        else // 猎人
        {
            // 猎人无论胜负都使用原本模型
            return (player.myGender == Gender.Male) ? netManager.maleHunterVictoryPrefab : netManager.hunterFemalePrefab;
        }
    }

    [ClientRpc]
    private void RpcHideOriginalPlayers()
    {
        // 静态列表在跨局时非常容易残留 Missing Reference
        for (int i = GamePlayer.AllPlayers.Count - 1; i >= 0; i--)
        {
            var p = GamePlayer.AllPlayers[i];
            
            // 【关键修复】: 必须检查 p 是否还存在于 Unity 内存中
            if (p == null || p.gameObject == null) 
            {
                GamePlayer.AllPlayers.RemoveAt(i);
                continue;
            }

            // 隐藏所有 Renderer
            Renderer[] rs = p.GetComponentsInChildren<Renderer>();
            foreach (var r in rs)
            {
                if (r != null) r.enabled = false;
            }
        }
    }

    // 辅助消息
    public struct RpcSetVisibleMsg : NetworkMessage { public bool visible; }
    [Server]
    private IEnumerator RestartRoutine()
    {
        restartCountdown = 5;
        while (restartCountdown > 0)
        {
            yield return new WaitForSeconds(1f);
            restartCountdown--;
        }

        // 回到大厅场景
        ResetGame();
        // 假设你的大厅场景在 NetworkManager 的 Online Scene 槽位里
        NetworkManager.singleton.ServerChangeScene(MyNetworkManager.singleton.onlineScene);
    }

    public void SetGameState(GameState newState)
    {
        currentState = newState;
        // 可以在这里添加状态变化时的逻辑处理
        Debug.Log("Game State changed to: " + newState.ToString());

    }
    // 【新增】用于在场景切换间隙保存玩家角色的字典 <ConnectionId, Role>
    public Dictionary<int, PlayerRole> pendingRoles = new Dictionary<int, PlayerRole>();
    // 【新增】用于保存名字的字典
    public Dictionary<int, string> pendingNames = new Dictionary<int, string>();
    public Dictionary<int, Color> pendingColors = new Dictionary<int, Color>(); // 建议也存一下颜色



    // 【修改】原来的 SpawnGamePlayers 改名为 SpawnPlayerForConnection，并只处理单个连接
    // 我们不再需要遍历所有连接，因为 NetworkManager 会一个个通知我们
    [Server]
    public void SpawnPlayerForConnection(NetworkConnectionToClient conn)
    {
        Gender gender = pendingGenders.ContainsKey(conn.connectionId) ? pendingGenders[conn.connectionId] : Gender.Male;
        MyNetworkManager netManager = NetworkManager.singleton as MyNetworkManager;
        GameObject prefabToUse;
        if (netManager == null) return;
        int id = conn.connectionId;
        string selectedItem = pendingItems.ContainsKey(id) ? pendingItems[id] : "";
        // ---------------------------------------------------------
        // 1. 决定角色 (Role) 和 名字 (Name)
        // ---------------------------------------------------------
        PlayerRole role;
        string pName;
        // 1. 获取数据
        if (pendingRoles.ContainsKey(id))
        {
            role = pendingRoles.ContainsKey(conn.connectionId) ? pendingRoles[conn.connectionId] : PlayerRole.Hunter;
            pName = pendingNames.ContainsKey(conn.connectionId) ? pendingNames[conn.connectionId] : $"Player {conn.connectionId}";            
        }
        else
        {
            // --- 核心修改：中途加入处理 ---
            // 如果是中途加入 (InGame)，或者预分配列表里没有 (Late Join)，强制给 Hunter
            // 你也可以在这里扩展：比如给 "Spectator" 观察者模式
            role = PlayerRole.Hunter;
            
            // 名字尝试从连接对象获取，或者给个默认名
            // 注意：因为是中途加入，conn.identity 可能为空或者不是 PlayerScript
            // 这里我们给一个默认名，或者之后让玩家自己改
            pName = $"Hunter (Late) {id}";
            
            Debug.LogWarning($"[Spawn] No role found for ID {id}. Assigning Default (Hunter). GameState: {currentState}");
        }

        // 2. 获取 Prefab
        // 根据角色和性别四选一
        if (role == PlayerRole.Witch)
        {
            switch (selectedItem)
            {
                case "InvisibilityCloak":
                    prefabToUse = (gender == Gender.Male) ? netManager.witchMaleCloakPrefab : netManager.witchFemaleCloakPrefab;
                    break;
                case "LifeAmulet":
                    prefabToUse = (gender == Gender.Male) ? netManager.witchMaleAmuletPrefab : netManager.witchFemaleAmuletPrefab;
                    break;
                case "MagicBroom":
                    prefabToUse = (gender == Gender.Male) ? netManager.witchMaleBroomPrefab : netManager.witchFemaleBroomPrefab;
                    break;
                default: // 默认形态
                    prefabToUse = (gender == Gender.Male) ? netManager.witchMalePrefab : netManager.witchFemalePrefab;
                    break;
            }
        }
        else
        {
            prefabToUse = (gender == Gender.Male) ? netManager.hunterMalePrefab : netManager.hunterFemalePrefab;
        }
        if (prefabToUse == null) return;



        // 3. 计算位置
        // Transform startTrans = NetworkManager.singleton.GetStartPosition();
        // Vector3 spawnPos = startTrans != null ? startTrans.position : Vector3.zero;
        // Quaternion spawnRot = startTrans != null ? startTrans.rotation : Quaternion.identity;
        
        // ---------------------------------------------------------
        // 3. 【核心修改】根据阵营计算位置
        // ---------------------------------------------------------
        Vector3 spawnPos = Vector3.zero;
        Quaternion spawnRot = Quaternion.identity;

        // 寻找对应的出生点组物体
        string groupName = (role == PlayerRole.Witch) ? "WitchSpawnPoints" : "HunterSpawnPoints";
        GameObject spawnGroup = GameObject.Find(groupName);

        if (spawnGroup != null && spawnGroup.transform.childCount > 0)
        {
            // 从该组的子物体中随机选一个
            int randomIndex = UnityEngine.Random.Range(0, spawnGroup.transform.childCount);
            Transform targetPoint = spawnGroup.transform.GetChild(randomIndex);
            spawnPos = targetPoint.position;
            spawnRot = targetPoint.rotation;
        }
        else
        {
            // 兜底方案：如果没找到组，使用 Mirror 默认逻辑
            Debug.LogWarning($"[Spawn] Could not find spawn group {groupName}, using default.");
            Transform startTrans = NetworkManager.singleton.GetStartPosition();
            if (startTrans != null)
            {
                spawnPos = startTrans.position;
                spawnRot = startTrans.rotation;
            }
        }

        // 1. 确保位置在地面上（向上发射射线再向下测，或者直接稍微抬高）
        spawnPos += Vector3.up * 0.5f; 

        // 2. 实例化
        GameObject characterInstance = Instantiate(prefabToUse, spawnPos, spawnRot);

        // 3. 物理纠偏：检查是否出生在树里
        CharacterController cc = characterInstance.GetComponent<CharacterController>();
        if (cc != null)
        {
            // 定义胶囊体检测的上下球心
            // 如果出生在树里，通过移动逻辑将其“挤”出去
            Vector3 p1 = spawnPos + Vector3.up * cc.radius;
            Vector3 p2 = spawnPos + Vector3.up * (cc.height - cc.radius);
            
            // 如果该区域已经有碰撞体（LayerMask 排除玩家自身层级，包含树木层级）
            if (Physics.CheckCapsule(p1, p2, cc.radius, propLayer)) 
            {
                // 暂时关掉一下，强行位移后再开
                cc.enabled = false;
                Vector3 pushDir = Random.onUnitSphere;
                pushDir.y = 0;
                characterInstance.transform.position += pushDir.normalized * 1.5f;
                cc.enabled = true;
                Debug.Log($"[Spawn] Fixed player {id} spawn collision.");
            }
        }


        // 4. 生成实例
        // GameObject characterInstance = Instantiate(prefabToUse, spawnPos, spawnRot);

        // 5. 初始化数据
        LobbyScript lobby = FindObjectOfType<LobbyScript>();
        GamePlayer playerScript = characterInstance.GetComponent<GamePlayer>();
        if (playerScript != null)
        {
            playerScript.playerName = pName;
            playerScript.playerRole = role;
            playerScript.myGender = gender; // 【新增这一行】将上面获取到的 gender 赋给角色脚本

            // 2. 【核心】在这里应用刚才抢救下来的内部变量
            playerScript.manaRegenRate = this.manaRegenInternal;
            playerScript.requiredClicks = this.trapDifficultyInternal;

            if (role == PlayerRole.Witch)
            {
                playerScript.maxHealth = this.witchHPInternal;
                playerScript.currentHealth = this.witchHPInternal;
                playerScript.maxMana = this.witchManaInternal;
                playerScript.currentMana = this.witchManaInternal;
            }
            else if (role == PlayerRole.Hunter)
            {
                playerScript.moveSpeed = this.hunterSpeedInternal;
            }
        }


        // 6. 【关键修改】处理 "Replace" 还是 "Add"
        // 当通过 OnServerAddPlayer 调用时，Mirror 期望我们调用 AddPlayerForConnection
        // 此时 conn.identity 通常为空（因为是新场景），但也可能是残留的
        
        // 简单暴力法：直接用 Replace，但使用 KeepAuthority 避免去销毁那个可能已经报错的旧对象
        // 或者更标准的做法：
        
        if (conn.identity == null)
        {
            // 如果连接上没有玩家（正常情况），直接添加
            NetworkServer.AddPlayerForConnection(conn, characterInstance);
        }
        else
        {
            // 如果连接上还有残留的引用（可能已销毁），用 Replace
            // 使用 KeepAuthority 选项，仅仅替换引用，不尝试去 Destroy 那个可能已经坏掉的旧物体
            NetworkServer.ReplacePlayerForConnection(conn, characterInstance, ReplacePlayerOptions.KeepAuthority);
            
            // 如果旧物体还活着，手动销毁它 (双保险)
            if (conn.identity.gameObject != null)
                NetworkServer.Destroy(conn.identity.gameObject);
        }

        Debug.Log($"[Server] Spawning {role} ({pName}) for ConnId: {conn.connectionId}");
    }

    [Server]
    public void PreAssignRoles()
    {
        pendingRoles.Clear();
        pendingNames.Clear();

        // 1. 获取所有有效连接
        List<NetworkConnectionToClient> connections = new List<NetworkConnectionToClient>();
        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity != null) connections.Add(conn);
        }

        int totalPlayers = connections.Count;
        if (totalPlayers == 0) return;

        // 2. 计算猎人应有数量 (至少 1 名猎人，除非只有 1 个人)
        int hunterTargetCount = Mathf.Max(1, Mathf.RoundToInt(totalPlayers * hunterRatioInternal));
        // 如果总人数超过1人，确保至少留一个位置给女巫
        if (totalPlayers > 1 && hunterTargetCount >= totalPlayers) hunterTargetCount = totalPlayers - 1;

        // 3. 洗牌算法 (Shuffle) 确保公平分配
        for (int i = 0; i < connections.Count; i++)
        {
            NetworkConnectionToClient temp = connections[i];
            int randomIndex = UnityEngine.Random.Range(i, connections.Count);
            connections[i] = connections[randomIndex];
            connections[randomIndex] = temp;
        }

        // 4. 按洗牌后的顺序分配角色
        for (int i = 0; i < connections.Count; i++)
        {
            NetworkConnectionToClient conn = connections[i];
            
            // 前 hunterTargetCount 名玩家为猎人，其余为女巫
            PlayerRole assignedRole = (i < hunterTargetCount) ? PlayerRole.Hunter : PlayerRole.Witch;
            
            pendingRoles[conn.connectionId] = assignedRole;

            var playerScript = conn.identity.GetComponent<PlayerScript>();
            string pName = (playerScript != null) ? playerScript.playerName : "Unknown";
            pendingNames[conn.connectionId] = pName;

            Debug.Log($"[PreAssignRoles] ID: {conn.connectionId} | Name: {pName} | Role: {assignedRole} (Ratio Target: {hunterTargetCount}/{totalPlayers})");
        }
        foreach (var conn in connections)
        {
            var pScript = conn.identity.GetComponent<PlayerScript>();
            // 记录该连接选中的性别
            pendingGenders[conn.connectionId] = pScript.myGender;
            // 【关键修改】记录玩家选择的道具
            pendingItems[conn.connectionId] = pScript.selectedWitchItemName;
            // 增加这一行日志，看看服务器在分配角色时抓到的是什么
            Debug.Log($"[Server] 正在记录玩家 {pScript.playerName} 的道具选择: {pScript.selectedWitchItemName}");
        }
    }

    // 【新增】当游戏场景真正加载完成后被调用
    [Server]
    public void OnGameSceneReady()
    {
        Debug.Log("[Server] Game Scene Ready. Initializing managers...");
        // 1. 随机分布树木
        TreeManager treeMgr = FindObjectOfType<TreeManager>();
        if (treeMgr != null)
        {
            treeMgr.ShuffleTrees();
        }
        // 此时已经在新场景，可以找到物体了
        if (animalSpawner == null) 
        {
            animalSpawner = FindObjectOfType<ServerAnimalSpawner>();
        }

        if (animalSpawner != null)
        {
            animalSpawner.SpawnAnimals(this.animalsToSpawnInternal);
        }
        else
        {
            Debug.LogError("[Server] Failed to find ServerAnimalSpawner in the new scene!");
        }
        // 生成传送门
        SpawnRandomPortal();
    }

    [Server]
    private void SpawnRandomPortal()
    {
        if (portalPrefab == null)
        {
            Debug.LogError("[Server] Portal Prefab 未赋值！请检查 Project 里的 GameManager Prefab。");
            return;
        }

        GameObject spawnGroup = GameObject.Find(portalSpawnGroupName);
        if (spawnGroup != null && spawnGroup.transform.childCount > 0)
        {
            int randomIndex = Random.Range(0, spawnGroup.transform.childCount);
            Transform targetTransform = spawnGroup.transform.GetChild(randomIndex);

            // 实例化并同步
            GameObject portalInstance = Instantiate(portalPrefab, targetTransform.position, targetTransform.rotation);
            NetworkServer.Spawn(portalInstance);
            
            Debug.Log($"[Server] Portal spawned at {targetTransform.name}");
        }
        else
        {
            Debug.LogError($"[Server] 找不到名为 '{portalSpawnGroupName}' 的物体或其没有子物体！");
        }
    }
    // 1. 增加一个停止音乐的客户端指令
    [ClientRpc]
    private void RpcStopVictoryMusic()
    {
        if (victoryAudioSource != null)
        {
            victoryAudioSource.Stop();
            Debug.Log("[Victory] Music stopped by Server.");
        }
    }
    public void ResetGame()
    {
        // 重置基础状态
        currentState = GameState.Lobby;
        gameTimer = 300f;
        gameWinner = PlayerRole.None;
        restartCountdown = 0;  // <-- 加上这一句
        // 重置统计人数
        aliveHuntersCount = 0;
        aliveWitchesCount = 0;

        // 【核心修复】重置古树任务相关的所有变量
        deliveredTreesCount = 0;
        totalRequiredTrees = 0;
        availableAncientTreesCount = 0;
        
        // 清除待定数据，防止旧数据干扰下一局
        pendingRoles.Clear();
        pendingNames.Clear();
        pendingColors.Clear();
        pendingItems.Clear();

        // 恢复 UI 状态（仅在客户端执行）
        if (isClient && SceneScript.Instance != null)
        {
            if (SceneScript.Instance.gameResultText != null)
                SceneScript.Instance.gameResultText.gameObject.SetActive(true);

            UnityEngine.UI.Image panelImage = SceneScript.Instance.gameResultPanel.GetComponent<UnityEngine.UI.Image>();
            if (panelImage != null)
            {
                Color c = panelImage.color;
                c.a = 0.5f; // 恢复为你原始的遮罩透明度（例如 0.5f）
                panelImage.color = c;
            }
        }
        // 清理全局玩家列表中的无效引用
        GamePlayer.AllPlayers.Clear(); // 彻底清空，因为回到大厅后所有人都会重新生成
        if (victoryAudioSource != null)
        {
            victoryAudioSource.Stop();
        }
        Debug.Log("[GameManager] Game State and delivery counters have been fully reset.");
    }
    [Server] // 确保只在服务器运行
    public void StartGame()
    {              
        // 1. 寻找大厅脚本
        LobbyScript lobby = FindObjectOfType<LobbyScript>();
        
        if (lobby != null)
        {
            // 1. 【核心】在切换场景前，把所有 SyncVar 的值存入 GameManager
            this.gameTimer = lobby.syncedGameTimer;
            this.animalsToSpawnInternal = lobby.syncedAnimalsNumber;
            this.witchHPInternal = lobby.syncedWitchHP;
            this.witchManaInternal = lobby.syncedWitchMana;
            this.hunterSpeedInternal = lobby.syncedHunterSpeed;
            this.trapDifficultyInternal = lobby.syncedTrapDifficulty;
            this.manaRegenInternal = lobby.syncedManaRegen;
            this.friendlyFireInternal = lobby.syncedFriendlyFire; // 【核心修改】捕获开关状态
            this.hunterRatioInternal = lobby.syncedHunterRatio;
            this.ancientRatioInternal = lobby.syncedAncientRatio; // 【新增】保存倍率
            Debug.Log($"[Server] Applying Lobby Settings: Timer = {this.gameTimer}, Animals = {this.animalsToSpawnInternal}, WitchHP = {this.witchHPInternal}, WitchMana = {this.witchManaInternal}, HunterSpeed = {this.hunterSpeedInternal}, TrapDifficulty = {this.trapDifficultyInternal}, ManaRegen = {this.manaRegenInternal}, FriendlyFire = {this.friendlyFireInternal}");
        }
        else
        {
            // 兜底方案：如果找不到大厅（比如直接从开发场景启动），使用默认值
            this.gameTimer = 300f; 
            this.animalsToSpawnInternal = 10;
            this.witchHPInternal = 100f;
            this.witchManaInternal = 100f;
            this.hunterSpeedInternal = 7f;
            this.trapDifficultyInternal = 2;
            this.manaRegenInternal = 5f;
            this.friendlyFireInternal = false; // 【核心修改】默认关闭
            this.hunterRatioInternal = 0.3f; // 默认猎人比例 30%
            this.ancientRatioInternal =  1.5f;
            Debug.LogWarning("[Server] LobbyScript not found, using default timer 300s");
        }

        // 2. 寻找 Spawner
        if (animalSpawner == null) {
            animalSpawner = FindObjectOfType<ServerAnimalSpawner>();
        }

        // 【新增】双重保险：确保开始时计数器为 0
        deliveredTreesCount = 0;
        totalRequiredTrees = 0;
        RpcStopVictoryMusic(); // 确保新对局开始时没有残留音乐
        // 3. 改变游戏状态
        gameStartTimer = Time.time; // 记录开始时间
        SetGameState(GameState.InGame);
        
        // --- 【删除掉原来的 gameTimer = 300f; 这行】 ---

        Debug.Log($"Game has started with duration: {gameTimer}s");
        
        if (NetworkServer.active)
        {
            PreAssignRoles(); 
        }
   
    }
    public void PauseGame()
    {
        SetGameState(GameState.Paused);
        Debug.Log("Game is paused.");
    }
    public void EndGame()
    {
        SetGameState(GameState.GameOver);
        Debug.Log("Game Over.");
    }
    public void getCurrentState()
    {
        Debug.Log("Current Game State: " + currentState.ToString());
    }
}