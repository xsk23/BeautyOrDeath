# WordPress Code Repository: Scripts

> Auto generated code dump.

## GameManager.cs

```csharp
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
```

## generate_md.py

```python
import os

import chardet  # pip install chardet
import docx  # pip install python-docx


def get_file_content(file_path):
    """
    尝试读取文件内容，自动处理编码问题
    """
    try:
        # 1. 尝试直接读取为 UTF-8 (最快)
        with open(file_path, "r", encoding="utf-8") as f:
            return f.read()
    except UnicodeDecodeError:
        try:
            # 2. 如果失败，使用二进制模式读取并检测编码
            with open(file_path, "rb") as f:
                raw_data = f.read()
                result = chardet.detect(raw_data)
                encoding = result["encoding"]
                if encoding:
                    return raw_data.decode(encoding)
                else:
                    # 3. 如果检测不到，尝试 latin-1 (可以读取任意字节流，不会报错但可能有乱码)
                    return raw_data.decode("latin-1")
        except Exception as e:
            return f"Error decoding file: {e}"
    except Exception as e:
        return f"Error reading file: {e}"


def generate_wp_code_markdown(
    root_dir,
    output_file,
    include_dirs=None,
    include_files=None,
    exclude_dirs=None,
    exclude_files=None,
):
    # WordPress 及 Web 开发常见后缀
    code_extensions = (
        # 核心逻辑
        ".php",
        ".inc",
        # 前端
        ".js",
        ".jsx",
        ".ts",
        ".tsx",
        ".vue",
        ".css",
        ".scss",
        ".sass",
        ".less",
        ".html",
        ".htm",
        # 配置与数据
        ".json",
        ".xml",
        ".yaml",
        ".yml",
        ".sql",  # 数据库导出
        ".ini",
        ".conf",
        ".htaccess",
        ".config",
        ".txt",
        ".md",
        ".svg",  # SVG本质是XML代码
        # 其他代码
        ".py",
        ".sh",
        ".bat",
        # 文档
        ".docx",
        ".cs",
    )

    # 默认初始化
    if include_dirs is None:
        include_dirs = []
    if include_files is None:
        include_files = []

    # 默认排除 WordPress 中不需要分析的目录
    if exclude_dirs is None:
        exclude_dirs = [
            ".git",
            ".vs",
            ".idea",
            ".vscode",  # IDE和版本控制
            "bin",
            "obj",
            "node_modules",
            "vendor",  # 依赖包
            "uploads",
            "cache",
            "upgrade",  # WP 动态资源
            "wp-content/uploads",
            "wp-content/cache",  # 具体路径匹配
        ]

    if exclude_files is None:
        exclude_files = [".DS_Store", "Thumbs.db", "wp-config-sample.php"]

    print(f"开始扫描目录: {root_dir}")
    print(f"结果将保存至: {output_file}")

    file_count = 0

    with open(output_file, "w", encoding="utf-8") as md_file:
        md_file.write(f"# WordPress Code Repository: {os.path.basename(root_dir)}\n\n")
        md_file.write("> Auto generated code dump.\n\n")

        for root, dirs, files in os.walk(root_dir):
            # 1. 过滤排除的目录 (修改 dirs 列表以阻止 os.walk 进入)
            dirs[:] = [
                d
                for d in dirs
                if d not in exclude_dirs
                and not any(ex in os.path.join(root, d) for ex in exclude_dirs)
            ]

            # 2. 检查包含目录逻辑 (如果指定了 include_dirs)
            # 这里的逻辑是：如果当前路径不是 include_dirs 的子路径，也不是 include_dirs 的父路径，则跳过
            if include_dirs:
                # 简单判断：当前 root 是否包含在任何 include_dirs 中，或者 include_dirs 是否包含在当前 root 中
                # 这里为了简化，假设 include_dirs 是相对于 root_dir 的名字
                # 如果当前 root 路径中不包含任何指定的 include 文件夹名，且我们已经深入到子目录，则可能需要跳过
                # 但为了保险起见，建议让 os.walk 遍历，在文件层级过滤
                pass

            for file in files:
                # 过滤文件名
                if file in exclude_files:
                    continue
                if include_files and file not in include_files:
                    continue

                # 过滤后缀
                if not file.lower().endswith(code_extensions):
                    continue

                file_path = os.path.join(root, file)

                # 再次确认目录包含逻辑 (更精准)
                if include_dirs:
                    rel_dir = os.path.relpath(root, root_dir)
                    # 如果当前文件的相对目录 不在 包含列表中，且不是根目录
                    is_included = False
                    for inc_dir in include_dirs:
                        if inc_dir in rel_dir.split(os.sep):
                            is_included = True
                            break
                    if not is_included and rel_dir != ".":
                        continue

                relative_path = os.path.relpath(file_path, root_dir)
                print(f"[{file_count + 1}] Processing: {relative_path}")

                md_file.write(f"## {relative_path}\n\n")

                # 处理 DOCX
                if file.lower().endswith(".docx"):
                    try:
                        doc = docx.Document(file_path)
                        md_file.write("```text\n")
                        for para in doc.paragraphs:
                            if para.text.strip():
                                md_file.write(para.text + "\n")
                        md_file.write("\n```\n\n")
                    except Exception as e:
                        md_file.write(f"> Error reading DOCX: {e}\n\n")

                # 处理普通代码文件
                else:
                    # 获取扩展名用于 markdown 高亮 (去掉点)
                    ext = file.split(".")[-1].lower()
                    # 映射一些扩展名到 markdown 支持的标准语言名
                    lang_map = {
                        "cs": "csharp",
                        "py": "python",
                        "js": "javascript",
                        "ts": "typescript",
                        "vue": "html",
                        "htm": "html",
                        "htaccess": "apache",
                        "conf": "nginx",
                    }
                    lang = lang_map.get(ext, ext)

                    md_file.write(f"```{lang}\n")
                    content = get_file_content(file_path)
                    md_file.write(content)
                    md_file.write("\n```\n\n")

                file_count += 1

    print(f"\n完成! 共处理 {file_count} 个文件。")


# ================= 配置区域 =================

# WordPress 根目录路径
root_directory = r"E:\UnityProjects\Mirror_Lobby\Assets\Scripts"
output_md = "unity_code.md"

# 如果只想导出特定目录 (例如只看主题或插件)
# include_dirs = ['wp-content', 'themes', 'plugins']
include_dirs = []

# 如果只想导出特定文件
include_files = []

# 额外的排除目录 (在默认排除基础上增加)
exclude_dirs = [
    "wp-admin",
    "wp-includes",
    "easyshop",
    "shopire",
    "twentytwentyfive",
    "twentytwentythree",
    "twentytwentytwo",
    "uploads",
    "plugins",
    "languages",
    "fonts",
]  # 如果你只想看用户代码，建议排除这两个核心目录
exclude_files = []  # 额外排除特定文件

# 执行生成
generate_wp_code_markdown(
    root_directory, output_md, include_dirs, include_files, exclude_dirs, exclude_files
)

```

## HUDExtension.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

public class HUDExtension : MonoBehaviour
{
    private void OnEnable()
    {
        SceneManager.activeSceneChanged += HandleSceneChanged;//注册场景切换事件
    }
    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleSceneChanged;//注销场景切换事件
    }
    private void HandleSceneChanged(Scene oldScene, Scene newScene)
    {
        GetComponent<NetworkManagerHUD>().enabled = newScene.name != "Menu";//在非菜单场景启用HUD
    }
}

```

## LobbyServer.cs

```csharp
using UnityEngine;
using Mirror;
using System.Collections.Generic;
using System.Diagnostics; // 用于 Process
using System.Linq; // 用于 Linq 查询

public class LobbyServer : MonoBehaviour
{
    // --- 配置 ---
    [Header("Network Config")]
    public string publicIP = "localhost"; // 你的公网IP (本机测试用 127.0.0.1)

    [Header("Port Management")]
    public int startPort = 7771;
    public int endPort = 7780; // 最多允许 10 个房间同时运行

    // --- 内部数据结构 ---
    class ServerRoomData
    {
        public int roomId;
        public string name;
        public string password;
        public int maxPlayers;
        public ushort port;
        public Process process; // 保存进程引用，用于监听退出事件
    }

    // 存储所有活跃房间 <RoomID, Data>
    private Dictionary<int, ServerRoomData> activeRooms = new Dictionary<int, ServerRoomData>();

    // 使用 HashSet 记录当前正在使用的端口，方便快速查找空缺
    private HashSet<int> usedPorts = new HashSet<int>();

    // 主线程调度器引用 (单例)
    private UnityMainThreadDispatcher dispatcher;

    public void StartLobby()
    {
        // 再次确认：如果是子进程房间，不要启动大厅逻辑
        if (IsSubProcess())
        {
            UnityEngine.Debug.Log("[Lobby] Currently a game room subprocess, skipping lobby initialization.");
            this.enabled = false;
            return;
        }

        UnityEngine.Debug.Log("[Lobby] Lobby service initializing...");

        // 确保主线程调度器存在
        dispatcher = UnityMainThreadDispatcher.Instance();

        // 注册消息
        if (NetworkServer.active)
        {
            NetworkServer.RegisterHandler<CreateRoomReq>(OnCreateRoom);
            NetworkServer.RegisterHandler<GetRoomListReq>(OnGetRoomList);
            NetworkServer.RegisterHandler<JoinRoomReq>(OnJoinRoom);
            UnityEngine.Debug.Log("[Lobby] Message callbacks registered successfully, lobby ready!");
        }
        else
        {
            UnityEngine.Debug.LogError("[Lobby] NetworkServer not active, lobby startup failed!");
        }
    }
    // 辅助方法：判断当前是否是子进程
    bool IsSubProcess()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        return System.Array.Exists(args, arg => arg == "-port");
    }
    // --- 1. 处理创建房间请求 ---
    void OnCreateRoom(NetworkConnectionToClient conn, CreateRoomReq msg)
    {
        // A. 智能获取最小可用端口
        int port = GetAvailablePort();
        UnityEngine.Debug.Log($"[LobbyServer] Received create request, assigning port: {port}");
        if (port == -1)
        {
            conn.Send(new CreateRoomRes { success = false, message = "服务器爆满，无可用房间" });
            return;
        }

        // B. 启动子进程
        Process p = SpawnGameProcess(port, msg.roomName); // 传入房间名参数

        if (p != null)
        {
            // 生成唯一房间ID
            int newId = GenerateRoomId();

            // C. 记录房间数据
            ServerRoomData newRoom = new ServerRoomData
            {
                roomId = newId,
                name = string.IsNullOrEmpty(msg.roomName) ? $"Room {newId}" : msg.roomName,
                password = msg.password,
                maxPlayers = msg.maxPlayers,
                port = (ushort)port,
                process = p
            };

            // D. 标记端口和房间为“占用”
            usedPorts.Add(port);
            activeRooms.Add(newId, newRoom);

            // E. 【关键】监听进程退出事件 (自动回收)
            try
            {
                p.EnableRaisingEvents = true;
                // 当进程关闭（房间没人自杀）时，触发回调
                // 注意：这里使用了闭包捕获 newId 和 port
                p.Exited += (sender, args) => OnGameProcessExited(newId, port);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[LobbyServer] Unable to listen for process exit event: {ex.Message}");
            }

            // F. 回复客户端：成功
            conn.Send(new CreateRoomRes
            {
                success = true,
                serverIp = publicIP,
                serverPort = (ushort)port
            });

            UnityEngine.Debug.Log($"[LobbyServer] Room created successfully ID:{newId} Port:{port} Name:{newRoom.name}");
        }
        else
        {
            UnityEngine.Debug.LogError($"[LobbyServer] Room creation failed, could not start subprocess.");
            conn.Send(new CreateRoomRes { success = false, message = "服务器进程启动失败" });
        }
    }

    // --- 2. 处理获取列表请求 ---
    void OnGetRoomList(NetworkConnectionToClient conn, GetRoomListReq msg)
    {
        var query = activeRooms.Values.AsEnumerable();

        // 搜索过滤逻辑
        if (!string.IsNullOrEmpty(msg.searchKeyword))
        {
            string key = msg.searchKeyword.ToLower();
            query = query.Where(r =>
                r.roomId.ToString().Contains(key) ||
                r.name.ToLower().Contains(key)
            );
        }

        // 转换为网络传输结构体 (隐藏密码)
        RoomInfo[] list = query.Select(r => new RoomInfo
        {
            roomId = r.roomId,
            roomName = r.name,
            hasPassword = !string.IsNullOrEmpty(r.password),
            currentPlayers = 0, // 暂时写0，进阶需进程间通信(IPC)获取实时人数
            maxPlayers = r.maxPlayers,
            port = r.port
        }).ToArray();

        conn.Send(new RoomListRes { rooms = list });
    }

    // --- 3. 处理加入房间请求 ---
    void OnJoinRoom(NetworkConnectionToClient conn, JoinRoomReq msg)
    {
        if (!activeRooms.ContainsKey(msg.roomId))
        {
            conn.Send(new JoinRoomRes { success = false, message = "房间不存在" });
            return;
        }

        ServerRoomData room = activeRooms[msg.roomId];

        // 校验密码
        if (!string.IsNullOrEmpty(room.password) && room.password != msg.password)
        {
            conn.Send(new JoinRoomRes { success = false, message = "密码错误" });
            return;
        }

        // 校验通过，发送跳转地址
        conn.Send(new JoinRoomRes
        {
            success = true,
            serverIp = publicIP,
            serverPort = room.port
        });
    }

    // --- 辅助方法：智能获取端口 ---
    int GetAvailablePort()
    {
        for (int i = startPort; i <= endPort; i++)
        {
            if (!usedPorts.Contains(i))
            {
                return i; // 找到第一个没被用的，直接返回
            }
        }
        return -1; // 所有端口都满了
    }

    // --- 辅助方法：生成唯一房间ID ---
    int GenerateRoomId()
    {
        int id;
        do
        {
            id = UnityEngine.Random.Range(1000, 9999);
        } while (activeRooms.ContainsKey(id));
        return id;
    }
    // --- 辅助方法：启动子进程 ---
    Process SpawnGameProcess(int port, string roomName)// 增加 roomName 参数
    {
        string fileName = "MyGameServer.exe"; // 请确保这是你 Build 出来的 exe 名字

        // // 自动适配扩展名
        // if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
        //     fileName += ".exe";
        // else if (Application.platform == RuntimePlatform.LinuxPlayer)
        //     fileName += ".x86_64";

        string path = "";

#if UNITY_EDITOR
        // 编辑器模式下：去项目根目录下的 Build 文件夹找 (需要你手动 Build 一次放在那里)
        path = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Build", fileName);
#else
        // 发布模式下：在 exe 同级目录找
        path = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, fileName);
#endif

        if (!System.IO.File.Exists(path))
        {
            UnityEngine.Debug.LogError($"[LobbyServer] Server file not found! Path: {path}");
            // ★ 如果找不到，返回 null，不要让服务器崩溃
            return null;
        }

        try
        {
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = path;
            // --- 核心修改：添加 -name 参数，注意名称中可能有空格，需要用引号包裹 ---
            info.Arguments = $"-batchmode -nographics -port {port} -name \"{roomName}\"";
            info.UseShellExecute = false;

            // 开启日志重定向 (可选，方便调试子进程报错)
            // info.RedirectStandardOutput = true;
            // info.RedirectStandardError = true;

            Process p = Process.Start(info);
            UnityEngine.Debug.Log($"[LobbyServer] Subprocess started successfully PID: {p.Id}, Port: {port}");
            return p;
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"[LobbyServer] Exception starting process: {e.Message}");
            return null;
        }
    }

    // --- 回调方法：当子进程退出时触发 ---
    // 注意：此方法运行在后台线程，不能直接操作 Unity API 或非线程安全集合
    void OnGameProcessExited(int roomId, int port)
    {
        // 将任务扔回主线程执行
        dispatcher.Enqueue(() =>
        {
            UnityEngine.Debug.Log($"[LobbyServer] Detected room process exit ID:{roomId} Port:{port}");

            // 1. 释放端口
            if (usedPorts.Contains(port))
            {
                usedPorts.Remove(port);
            }

            // 2. 从列表中移除房间
            if (activeRooms.ContainsKey(roomId))
            {
                // 既然进程都退出了，就把原来的 process 对象 dispose 掉防止内存泄漏
                try
                {
                    activeRooms[roomId].process?.Dispose();
                }
                catch { }

                activeRooms.Remove(roomId);
            }

            UnityEngine.Debug.Log($"[LobbyServer] Port {port} reclaimed, active room count: {activeRooms.Count}");
        });
    }

    // 在大厅关闭时清理所有子进程 (防止残留僵尸进程)
    void OnApplicationQuit()
    {
        foreach (var room in activeRooms.Values)
        {
            try
            {
                if (room.process != null && !room.process.HasExited)
                {
                    room.process.Kill(); // 强制关闭所有子房间
                }
            }
            catch { }
        }
    }
}
```

## MyNetworkManager.cs

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using System.Collections;
using kcp2k;

public class MyNetworkManager : NetworkManager
{
    // 定义静态变量，静态变量在场景切换时绝对不会丢失
    // 新增一个静态变量临时存储解析出来的房间名
    public static string InitialRoomName = "New Room";
    private static string _targetAddr;
    private static ushort _targetPort;
    private static bool _shouldReconnect = false;
    public static bool IsTransitioningToRoom = false; 
    [Header("Game Settings")]
    // [Scene] 属性会让字符串变成路径，导致对比失败。
    // 为了简单，我们直接用 Tooltip 提示，或者改用 Path.GetFileNameWithoutExtension 处理
    [Tooltip("Ensure the name here matches exactly with the scene name in Build Settings")]
    public string gameSceneName = "MyScene";

    // 【新增】在这里定义 Prefab 槽位，方便在 Inspector 拖拽
    [Header("Role Prefabs")]
    public GameObject witchMalePrefab;
    public GameObject witchFemalePrefab;
    public GameObject hunterMalePrefab;
    public GameObject hunterFemalePrefab;
    [Header("Role Prefabs (Special Variants)")]
    public GameObject witchMaleCloakPrefab;
    public GameObject witchFemaleCloakPrefab;
    public GameObject witchMaleAmuletPrefab;    // 【新增】护符版男巫
    public GameObject witchFemaleAmuletPrefab;  // 【新增】护符版女巫
    public GameObject witchMaleBroomPrefab;     // 【新增】扫帚版男巫
    public GameObject witchFemaleBroomPrefab;   // 【新增】扫帚版女巫
    [Header("Victory Special Prefabs")]
    public GameObject youngWitchMalePrefab;   // 新增：Young版男巫
    public GameObject youngWitchFemalePrefab; // 新增：Young版女巫
    public GameObject maleHunterVictoryPrefab; // 【新增】在此处拖入你的 malehuntervictoryzone Prefab
    [Header("System Prefabs")]
    // 【新增】拖入你做好的 GameManager Prefab (必须带 NetworkIdentity)
    public GameObject gameManagerPrefab;

    // ---------------------------------------------------------
    // 服务器启动时生成 GameManager
    // ---------------------------------------------------------
    public override void Awake()
    {
        base.Awake();
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-port" && i + 1 < args.Length)
            {
                if (ushort.TryParse(args[i + 1], out ushort port))
                {
                    // 假设你用的是 KcpTransport (Mirror 默认)
                    if (Transport.active is kcp2k.KcpTransport kcp)
                    {
                        kcp.Port = port;
                        Debug.Log($"[ServerStartup] Transport Port set to: {port}");
                    }
                    else
                    {
                        Debug.LogWarning($"[ServerStartup] Current Transport is not KcpTransport, cannot set port!");
                    }
                }
            }
            // --- 新增：解析房间名参数 ---
            if (args[i] == "-name" && i + 1 < args.Length)
            {
                InitialRoomName = args[i + 1];
                Debug.Log($"[ServerStartup] Room Name set to: {InitialRoomName}");
            }
        }
    }
    public void ClientChangeRoom(string ip, ushort port)
    {
        Debug.Log($"[Client] 准备跳转至房间: {ip}:{port}");
        // 1. 开启跳转标志位
        IsTransitioningToRoom = true;
        // 1. 存入静态变量
        _targetAddr = ip;
        _targetPort = port;
        _shouldReconnect = true;

        // 2. 停止当前客户端
        StopClient();

        // 注意：StopClient 之后，代码可能就会因为对象销毁而停止执行了。
        // 所以我们必须利用 OnStopClient 这个钩子来“接力”。
    }
    // 当成功连入新房间后，关闭标志位
    public override void OnClientConnect()
    {
        base.OnClientConnect();
        IsTransitioningToRoom = false;
    }
    // 这个钩子在客户端完全停止后（场景也切换完了）会被触发
    public override void OnStopClient()
    {
        Debug.Log("[Client] OnStopClient triggered.");
        base.OnStopClient();

        if (_shouldReconnect)
        {
            _shouldReconnect = false;

            // 【关键修改】
            // 不要直接调用 StartCoroutine(...)，因为那是 this.StartCoroutine
            // 改为调用 Dispatcher 上的 StartCoroutine
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                Debug.Log("[Client] Enqueued reconnection task to Dispatcher.");
                // 注意这里：是让 Dispatcher 这个长生不老的物体去跑协程
                UnityMainThreadDispatcher.Instance().StartCoroutine(FinalConnectRoutine());
            });
        }
    }

    private IEnumerator FinalConnectRoutine()
    {
        Debug.Log($"[Client] FinalConnectRoutine started on Dispatcher. Target: {_targetAddr}:{_targetPort}");

        // 等待更长的时间，给子进程 .exe 预留启动和开启监听的时间
        // 建议设为 4-5 秒
        yield return new WaitForSeconds(5.0f); 

        // 重新通过单例获取 NetworkManager（此时可能是新场景里的那个实例）
        var nm = NetworkManager.singleton;
        if (nm == null)
        {
            Debug.LogError("[Client] Fatal: NetworkManager.singleton is NULL after waiting!");
            yield break;
        }
        nm.onlineScene = "LobbyRoom";
        nm.networkAddress = _targetAddr;

        // 获取 KcpTransport
        var kcp = nm.GetComponent<KcpTransport>();
        if (kcp != null)
        {
            kcp.Port = _targetPort;
            Debug.Log($"[Client] Config applied. Address: {nm.networkAddress}, Port: {kcp.Port}");
        }

        Debug.Log("[Client] Starting Client to connect to Room...");
        nm.StartClient();
    }
    public override void OnStartServer()
    {
        base.OnStartServer();

        // 检查是否已经存在 (防止重复生成)
        if (GameManager.Instance == null && gameManagerPrefab != null)
        {
            GameObject gm = Instantiate(gameManagerPrefab);

            // 【关键】让它在场景切换时不销毁
            DontDestroyOnLoad(gm);

            // 在网络上生成
            NetworkServer.Spawn(gm);
        }
        if (Application.isBatchMode && IsRoomSubProcess())
        {
            // --- 分支 A：我是子进程 (游戏房间) ---
            Debug.Log("[Server] Detected room subprocess, switching to game scene: LobbyRoom");
            this.onlineScene = "LobbyRoom"; // 确保在线场景是 LobbyRoom
            ServerChangeScene("LobbyRoom");
            StartCoroutine(AutoShutdownIfEmpty());
        }
        else
        {
            // --- 分支 B：我是主进程 (大厅服务器) 或 编辑器Host ---
            // 尝试获取挂在同一个物体上的 LobbyServer 组件
            this.onlineScene = "ConnectRoom"; // 确保在线场景是 ConnectRoom
            LobbyServer lobby = GetComponent<LobbyServer>();
            if (lobby != null)
            {
                // 【核心修改】手动启动大厅逻辑
                lobby.StartLobby();
            }
        }
    }

    private bool IsRoomSubProcess()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        bool isSubProcess = System.Array.Exists(args, arg => arg == "-port");
        UnityEngine.Debug.Log($"[Server] Checking if subprocess: {isSubProcess}");
        return isSubProcess;
    }
    // 在 MyNetworkManager 类中重写客户端场景切换完成的回调
    public override void OnClientSceneChanged()
    {
        base.OnClientSceneChanged();

        // 获取当前场景名
        string activeSceneName = SceneManager.GetActiveScene().name;
        string configNameClean = System.IO.Path.GetFileNameWithoutExtension(gameSceneName);
        // 如果回到了大厅（假设你的在线场景是 Lobby 或 Menu 相关的）
        // 或者干脆判断：只要不是游戏场景，就解锁鼠标
        if (activeSceneName != configNameClean)
        {
            Debug.Log("[Cursor] Reseting cursor for non-game scene.");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        // IsTransitioningToRoom = false;
        // 当断开连接回到离线状态（StartMenu）时
        // 把 onlineScene 还原为 ConnectRoom
        // 这样下次你点“进入大厅”时，它才会去 ConnectRoom
        this.onlineScene = "ConnectRoom";
    }

    // ---------------------------------------------------------
    // 场景切换完成后的回调
    // ---------------------------------------------------------
    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);
        // 1. 获取当前激活的场景名称 (例如 "MyScene")
        string activeSceneName = SceneManager.GetActiveScene().name;

        // 2. 处理配置的名称 (去掉路径和后缀，确保只剩下 "MyScene")
        // 如果 gameSceneName 是 "Assets/Scenes/MyScene.unity"，这一步会变成 "MyScene"
        string configNameClean = System.IO.Path.GetFileNameWithoutExtension(gameSceneName);


        if (activeSceneName == configNameClean)
        {
            Debug.Log("[Mirror] Game Scene Loaded on Server. Waiting for clients to join...");
        }
        // 只有当加载的是游戏地图时才触发
        if (sceneName == configNameClean)
        {
            if (GameManager.Instance != null)
            {
                // 通知 GameManager：游戏场景已就绪，可以生成东西了
                GameManager.Instance.OnGameSceneReady();
            }
        }
        if (sceneName == "LobbyRoom")
        {
            Debug.Log("[Server] LobbyRoom scene loaded, ready to accept player connections.");
        }

    }

    // ---------------------------------------------------------
    // 【核心】当客户端加载完场景，请求加入游戏时调用
    // ---------------------------------------------------------
    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        // 1. 获取当前场景名
        string activeSceneName = SceneManager.GetActiveScene().name;
        string configNameClean = System.IO.Path.GetFileNameWithoutExtension(gameSceneName);

        // 2. 判断当前是在 "大厅" 还是 "游戏"
        if (activeSceneName == configNameClean)
        {
            // --- 在游戏场景中 ---
            // 调用 GameManager 生成 Witch 或 Hunter
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SpawnPlayerForConnection(conn);
            }
        }
        else
        {
            // --- 在大厅场景中 ---
            // 执行默认逻辑（生成 Player Prefab / LobbyPlayer）
            base.OnServerAddPlayer(conn);
        }
    }


    // ---------------------------------------------------------
    // 玩家断线回调 (保持你原有的逻辑)
    // ---------------------------------------------------------
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // 1. 先执行基类逻辑（这会销毁玩家物体，从列表中移除连接）
        base.OnServerDisconnect(conn);
        // 只有在纯服务器模式下检查
        if (Application.isBatchMode && IsRoomSubProcess())
        {
            Debug.Log($"[Server] Player disconnected. Remaining players: {numPlayers}");

            // If player count reaches zero, shut down the server
            if (numPlayers == 0)
            {
                Debug.Log("[Server] Room is empty, shutting down process...");
                // Delay shutdown by 1 second to allow network messages to send
                StartCoroutine(QuitGameRoutine());
            }
        }
        // // 2. 获取当前场景名字
        // string currentScene = SceneManager.GetActiveScene().name;

        // // 3. 只有在“游戏场景”中才执行这个检查
        // // 防止在大厅里有人退出导致服务器重载大厅
        // if (currentScene == "MyScene") 
        // {
        //     // 4. 检查当前连接的玩家数量
        //     // numPlayers is a built-in counter in NetworkManager
        //     Debug.Log($"A player left. Remaining players: {numPlayers}");

        //     if (numPlayers == 0)
        //     {
        //         Debug.Log("All players have left, server returning to lobby...");
        //         // 重置游戏状态
        //         GameManager.Instance.ResetGame();
        //         // 切回大厅 (假设你的 offlineScene 或 onlineScene 是大厅)
        //         // 注意：onlineScene 通常指大厅，offlineScene 是登录界面
        //         // 如果你想切回 Lobby，确保这里填对了场景名
        //         ServerChangeScene(onlineScene); 
        //     }
        // }
    }
    IEnumerator AutoShutdownIfEmpty()
    {
        if (!IsRoomSubProcess()) yield break; // Only execute in subprocess rooms
        // Wait 60 seconds for the first player to join
        yield return new WaitForSeconds(60f);

        if (numPlayers == 0)
        {
            Debug.Log("[Server] No players joined within 60 seconds, shutting down automatically...");
            Application.Quit();
        }
    }

    IEnumerator QuitGameRoutine()
    {
        yield return new WaitForSeconds(1.0f);
        Application.Quit(); // 杀死当前进程
    }
}
```

## NetworkManagerHUD_UGUI.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.UI;

public class NetworkManagerHUD_UGUI : MonoBehaviour
{
    NetworkManager manager;
    public GameObject StartButtonGroup;//开始按钮组
    public GameObject StopButtonGroup;//停止按钮组
    public Text StatusText;//状态文本
    public Button HostButton;//主机按钮
    public Button ClientButton;//客户端按钮
    public InputField inputFieldIP;//IP输入框
    public InputField inputFieldPort;//端口输入框
    public Button ServerOnlyButton;//仅服务器按钮
    public Button StopButton;//停止按钮

    //点击创建Server
    public void OnClickServerOnltBtn()
    {
        manager.StartServer();
    }
    //点击创建client
    private void OnClickClient()
    {
        manager.StartClient();
    }
    //点击创建Host
    private void OnClickHost()
    {
        manager.StartHost();
    }
    //点击停止按钮
    private void OnClickStopBtn()
    {
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            manager.StopHost();
        }
        else if (NetworkClient.isConnected)
        {
            manager.StopClient();
        }
        else if (NetworkServer.active)
        {
            manager.StopServer();
        }
    }
    void StatusLabels()
    {
        // host mode
        // display separately because this always confused people:
        //   Server: ...
        //   Client: ...
        if (NetworkServer.active && NetworkClient.active)
        {
            // host mode
            StatusText.text = $"<b>Host</b>: running via {Transport.active}";
        }
        else if (NetworkServer.active)
        {
            // server only
            StatusText.text = $"<b>Server</b>: running via {Transport.active}";
        }
        else if (NetworkClient.isConnected)
        {
            // client only
            StatusText.text = $"<b>Client</b>: connected to {manager.networkAddress} via {Transport.active}";
        }
    }


    void Start()
    {
        manager = FindObjectOfType<NetworkManager>();
        //按钮绑定事件
        HostButton.onClick.AddListener(OnClickHost);
        ClientButton.onClick.AddListener(OnClickClient); 
        ServerOnlyButton.onClick.AddListener(OnClickServerOnltBtn);
        StopButton.onClick.AddListener(OnClickStopBtn);
    }
    void Update()
    {
        if (!NetworkClient.isConnected && !NetworkServer.active)
        {
            if (!NetworkClient.active)
            {
                manager.networkAddress = inputFieldIP.text;
                // only show a port field if we have a port transport
                // we can't have "IP:PORT" in the address field since this only
                // works for IPV4:PORT.
                // for IPV6:PORT it would be misleading since IPV6 contains ":":
                // 2001:0db8:0000:0000:0000:ff00:0042:8329
                if (Transport.active is PortTransport portTransport)
                {
                    // use TryParse in case someone tries to enter non-numeric characters
                    if (ushort.TryParse(inputFieldPort.text, out ushort port))
                        portTransport.Port = port;
                }      
                StatusText.text = "";
            }  
            else
            {
                // Connecting
                StatusText.text = $"Connecting to {manager.networkAddress}..";              
            }
            StartButtonGroup.SetActive(true);
            StopButtonGroup.SetActive(false);   
            
        }
        else
        {
            StatusLabels();
        }
        if (NetworkServer.active && NetworkClient.active)
        {
            StartButtonGroup.SetActive(false);
            StopButtonGroup.SetActive(true);
        }
        else if (NetworkServer.active)
        {
            // server only
            StartButtonGroup.SetActive(false);
            StopButtonGroup.SetActive(true);        
        }
        else if (NetworkClient.isConnected)
        {
            // client only
            StartButtonGroup.SetActive(false);
            StopButtonGroup.SetActive(true);        
        }            
    }
}

```

## NetworkMessage.cs

```csharp
using Mirror;

// 1. 请求创建房间
public struct CreateRoomReq : NetworkMessage
{
    public string roomName;
    public string password;  // 空字符串代表无密码
    public int maxPlayers;
}

// 2. 回复创建结果
public struct CreateRoomRes : NetworkMessage
{
    public bool success;
    public string message;
    public string serverIp;   // 新增：告诉客户端连哪个 IP
    public ushort serverPort; // 新增：告诉客户端连哪个 端口
}


// 3. 房间数据 (用于之后刷新列表)
[System.Serializable]
public struct RoomInfo
{
    public int roomId;
    public string roomName;
    public bool hasPassword; // 只告诉客户端有没有密码，不发真实密码
    public int currentPlayers;
    public int maxPlayers;
    public ushort port;
}

// 4. 回复房间列表
public struct RoomListRes : NetworkMessage
{
    public RoomInfo[] rooms;
}

// 5. 请求刷新列表
public struct GetRoomListReq : NetworkMessage { public string searchKeyword; }

// 6. 请求：加入房间
public struct JoinRoomReq : NetworkMessage
{
    public int roomId;
    public string password;
}

// 7. 回复：加入结果 (包含跳转地址)
public struct JoinRoomRes : NetworkMessage
{
    public bool success;
    public string message;
    public string serverIp;
    public ushort serverPort;
}
```

## SingletonAutoMono.cs

```csharp
 using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class SingletonAutoMono<T> : MonoBehaviour where T : SingletonAutoMono<T>
{
    private static T _instance;
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                // 尝试在场景中找到已有的实例
                _instance = FindObjectOfType<T>();
                if (_instance == null)
                {
                    // 如果没有找到，则创建一个新的 GameObject 并附加该组件
                    GameObject singletonObject = new GameObject(typeof(T).Name);
                    singletonObject.name = typeof(T).ToString();
                    _instance = singletonObject.AddComponent<T>();
                    DontDestroyOnLoad(singletonObject); // 可选：在场景切换时不销毁
                }
            }
            return _instance;
        }
    }

    protected virtual void Awake()
    {
        // 确保只有一个实例存在
        if (_instance == null)
        {
            _instance = this as T;
            DontDestroyOnLoad(gameObject); // 可选：在场景切换时不销毁
        }
        else if (_instance != this)
        {
            Destroy(gameObject); // 销毁重复的实例
        }
    }
}

```

## UnityMainThreadDispatcher.cs

```csharp
using UnityEngine;
using System.Collections.Generic;
using System;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();

    public void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    private static UnityMainThreadDispatcher _instance = null;

    public static UnityMainThreadDispatcher Instance()
    {
        if (!_instance)
        {
            _instance = FindObjectOfType<UnityMainThreadDispatcher>();
            if (!_instance)
            {
                var obj = new GameObject("MainThreadDispatcher");
                _instance = obj.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(obj);
            }
        }
        return _instance;
    }

    public void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }
}
```

## Audio\AudioManager.cs

```csharp
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class SoundAction
{
    public string soundName;       // 声音的唯一标识符（如 "Footstep_Dirt", "Shotgun_Fire"）
    public AudioClip[] clips;      // 数组：支持同类音效随机播放（如 3 种不同泥土脚步声）
    [Range(0f, 1f)] public float volume = 1.0f;
    public bool randomPitch = false; // 是否随机音高（防单调）
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("音效库配置")]
    public SoundAction[] soundLibrary;
    private Dictionary<string, SoundAction> soundDictionary;

    [Header("2D音效源 (UI、系统音)")]
    public AudioSource source2D;

    [Header("3D音效对象池配置")]
    public GameObject audioSourcePrefab; // 需要一个挂了 AudioSource 的空物体Prefab
    public int poolSize = 10;
    private Queue<AudioSource> sourcePool3D;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 初始化字典，查找速度 O(1)
        soundDictionary = new Dictionary<string, SoundAction>();
        foreach (var sound in soundLibrary)
        {
            if (!soundDictionary.ContainsKey(sound.soundName))
                soundDictionary.Add(sound.soundName, sound);
        }

        // 初始化 3D 音效对象池
        sourcePool3D = new Queue<AudioSource>();
        for (int i = 0; i < poolSize; i++)
        {
            AudioSource newSource = Instantiate(audioSourcePrefab, transform).GetComponent<AudioSource>();
            newSource.gameObject.SetActive(false);
            sourcePool3D.Enqueue(newSource);
        }
    }

    // --- 播放 2D 声音（如 UI点击、耳鸣、心跳声） ---
    public void Play2D(string name)
    {
        if (soundDictionary.TryGetValue(name, out SoundAction soundData) && soundData.clips.Length > 0)
        {
            AudioClip clip = soundData.clips[Random.Range(0, soundData.clips.Length)];
            if (soundData.randomPitch) source2D.pitch = Random.Range(0.9f, 1.1f);
            else source2D.pitch = 1f;
            
            source2D.PlayOneShot(clip, soundData.volume);
        }
    }

    // --- 播放 3D 声音（如 枪声、变身音效、狗叫，具有空间衰减） ---
    public void Play3D(string name, Vector3 position)
    {
        if (soundDictionary.TryGetValue(name, out SoundAction soundData) && soundData.clips.Length > 0)
        {
            AudioSource source = GetPooledSource();
            if (source == null) return; // 池满了且都在播放，直接丢弃（防止爆音）

            source.transform.position = position;
            source.gameObject.SetActive(true);

            AudioClip clip = soundData.clips[Random.Range(0, soundData.clips.Length)];
            source.clip = clip;
            source.volume = soundData.volume;
            
            if (soundData.randomPitch) source.pitch = Random.Range(0.85f, 1.15f);
            else source.pitch = 1f;

            source.Play();

            // 播放完毕后自动回收
            StartCoroutine(ReturnToPool(source, clip.length));
        }
        else
        {
            Debug.LogWarning($"[AudioManager] Sound '{name}' not found!");
        }
    }

    private AudioSource GetPooledSource()
    {
        if (sourcePool3D.Count > 0) return sourcePool3D.Dequeue();
        return null; 
    }

    private System.Collections.IEnumerator ReturnToPool(AudioSource source, float delay)
    {
        yield return new WaitForSeconds(delay);
        source.gameObject.SetActive(false);
        sourcePool3D.Enqueue(source);
    }
}
```

## Objects\CreatureAIWander.cs

```csharp
using UnityEngine;
using Mirror;

namespace Controller
{
    [RequireComponent(typeof(CreatureMover))]
    public class CreatureAIWander : NetworkBehaviour
    {
        public enum WanderState { Idle, Walking, Running }

        [Header("状态权重 (总和建议为1)")]
        [Range(0, 1)] public float m_IdleWeight = 0.4f;   // 停下的概率
        [Range(0, 1)] public float m_WalkWeight = 0.4f;   // 走路的概率
        [Range(0, 1)] public float m_RunWeight = 0.2f;    // 跑步的概率

        [Header("时间设置")]
        [SerializeField] private float m_IdleTimeMin = 2f;
        [SerializeField] private float m_IdleTimeMax = 5f;
        [SerializeField] private float m_MoveTimeMin = 3f;
        [SerializeField] private float m_MoveTimeMax = 6f;

        private CreatureMover m_Mover;
        private float m_Timer;
        private WanderState m_CurrentState = WanderState.Idle;
        private Vector2 m_MoveInput;

        private void Awake()
        {
            m_Mover = GetComponent<CreatureMover>();
            SelectNextState();
        }
        [ServerCallback] 
        private void Update()
        {
            m_Timer -= Time.deltaTime;

            // 【新增逻辑】如果当前位置已经在边界边缘，强制立即切换状态（重新选路）
            if (WorldBoundaryManager.Instance != null)
            {
                float distToCenter = Vector3.Distance(transform.position, WorldBoundaryManager.Instance.Center);
                // 如果距离边缘不到 2 米，提前换向
                if (distToCenter > (WorldBoundaryManager.Instance.Radius - 2f))
                {
                    // 只有当动物还在“往外走”时才重置计时器
                    Vector3 moveDir = new Vector3(m_MoveInput.x, 0, m_MoveInput.y);
                    if (Vector3.Dot(moveDir, (transform.position - WorldBoundaryManager.Instance.Center)) > 0)
                    {
                        m_Timer = 0; // 强制下一帧进入 SelectNextState
                    }
                }
            }
            
            if (m_Timer <= 0)
            {
                SelectNextState();
            }

            // 根据当前状态决定输入
            bool isRunning = (m_CurrentState == WanderState.Running);
            Vector2 currentInput = (m_CurrentState == WanderState.Idle) ? Vector2.zero : m_MoveInput;

            // 虚拟目标点（用于控制转向）
            Vector3 virtualTarget = transform.position + new Vector3(m_MoveInput.x, 0, m_MoveInput.y) * 5f;

            if (m_Mover != null)
            {
                // 调用 Mover 接口
                // 第三个参数 isRun 为 true 时，CreatureMover 会把 Animator 的 State 设为 1
                m_Mover.SetInput(currentInput, virtualTarget, isRunning, false);
            }
        }

        private void SelectNextState()
        {
            float roll = Random.value;

            if (roll < m_IdleWeight)
            {
                // 进入 Idle
                m_CurrentState = WanderState.Idle;
                m_Timer = Random.Range(m_IdleTimeMin, m_IdleTimeMax);
            }
            else if (roll < m_IdleWeight + m_WalkWeight)
            {
                // 进入 Walking
                m_CurrentState = WanderState.Walking;
                m_Timer = Random.Range(m_MoveTimeMin, m_MoveTimeMax);
                GenerateRandomDirection();
            }
            else
            {
                // 进入 Running
                m_CurrentState = WanderState.Running;
                m_Timer = Random.Range(m_MoveTimeMin * 0.7f, m_MoveTimeMax * 0.7f); // 跑步时间通常稍短
                GenerateRandomDirection();
            }
        }

        private void GenerateRandomDirection()
        {
            m_MoveInput = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized;
        }
    }
}
```

## Objects\CreatureMover.cs

```csharp
using System;
using UnityEditor;
using UnityEngine;
using Mirror;

namespace Controller
{
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public class CreatureMover : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField]
        public float m_WalkSpeed = 1f;
        [SerializeField]
        public float m_RunSpeed = 4f;
        [SerializeField, Range(0f, 360f)]
        private float m_RotateSpeed = 90f;
        [SerializeField]
        private Space m_Space = Space.Self;
        [SerializeField]
        private float m_JumpHeight = 5f;

        [Header("Animator")]
        [SerializeField]
        public string m_VerticalID = "Vert";
        [SerializeField]
        public string m_StateID = "State";
        [SerializeField]
        private LookWeight m_LookWeight = new(1f, 0.3f, 0.7f, 1f);

        private Transform m_Transform;
        private CharacterController m_Controller;
        private Animator m_Animator;

        private MovementHandler m_Movement;
        private AnimationHandler m_Animation;

        private Vector2 m_Axis;
        private Vector3 m_Target;
        private bool m_IsRun;

        private bool m_IsMoving;

        public Vector2 Axis => m_Axis;
        public Vector3 Target => m_Target;
        public bool IsRun => m_IsRun;

        private new void OnValidate()
        {
            m_WalkSpeed = Mathf.Max(m_WalkSpeed, 0f);
            m_RunSpeed = Mathf.Max(m_RunSpeed, m_WalkSpeed);

            m_Movement?.SetStats(m_WalkSpeed / 3.6f, m_RunSpeed / 3.6f, m_RotateSpeed, m_JumpHeight, m_Space);
        }

        private void Awake()
        {
            m_Transform = transform;
            m_Controller = GetComponent<CharacterController>();
            m_Animator = GetComponent<Animator>();

            m_Movement = new MovementHandler(m_Controller, m_Transform, m_WalkSpeed, m_RunSpeed, m_RotateSpeed, m_JumpHeight, m_Space);
            m_Animation = new AnimationHandler(m_Animator, m_VerticalID, m_StateID);
        }
        [ServerCallback]
        private void Update()
        {
            m_Movement.Move(Time.deltaTime, in m_Axis, in m_Target, m_IsRun, m_IsMoving, out var animAxis, out var isAir);
            m_Animation.Animate(in animAxis, m_IsRun ? 1f : 0f, Time.deltaTime);
            // 【新增】服务器端强制边界约束
            if (WorldBoundaryManager.Instance != null && WorldBoundaryManager.Instance.isActive)
            {
                // 动物通常没有 localPlayer 概念，由服务器统一约束
                Vector3 constrainedPos = WorldBoundaryManager.Instance.GetConstrainedPosition(
                    transform.position, 
                    m_Controller.radius
                );

                if (constrainedPos != transform.position)
                {
                    transform.position = constrainedPos;
                }
            }
        }
        [ServerCallback]
        private void OnAnimatorIK()
        {
            m_Animation.AnimateIK(in m_Target, m_LookWeight);
        }

        public void SetInput(in Vector2 axis, in Vector3 target, in bool isRun, in bool isJump)
        {
            m_Axis = axis;
            m_Target = target;
            m_IsRun = isRun;

            if (m_Axis.sqrMagnitude < Mathf.Epsilon)
            {
                m_Axis = Vector2.zero;
                m_IsMoving = false;
            }
            else
            {
                m_Axis = Vector3.ClampMagnitude(m_Axis, 1f);
                m_IsMoving = true;
            }
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if(hit.normal.y > m_Controller.stepOffset)
            {
                m_Movement.SetSurface(hit.normal);
            }
        }

        [Serializable]
        private struct LookWeight
        {
            public float weight;
            public float body;
            public float head;
            public float eyes;

            public LookWeight(float weight, float body, float head, float eyes)
            {
                this.weight = weight;
                this.body = body;
                this.head = head;
                this.eyes = eyes;
            }
        }

        #region Handlers
        private class MovementHandler
        {
            private readonly CharacterController m_Controller;
            private readonly Transform m_Transform;

            private float m_WalkSpeed;
            private float m_RunSpeed;
            private float m_RotateSpeed;

            private Space m_Space;

            private readonly float m_Luft = 75f;

            private float m_TargetAngle;
            private bool m_IsRotating = false;

            private Vector3 m_Normal;
            private Vector3 m_GravityAcelleration = Physics.gravity;

            private float m_jumpTimer;
            private Vector3 m_LastForward;

            public MovementHandler(CharacterController controller, Transform transform, float walkSpeed, float runSpeed, float rotateSpeed, float jumpHeight, Space space)
            {
                m_Controller = controller;
                m_Transform = transform;

                m_WalkSpeed = walkSpeed;
                m_RunSpeed = runSpeed;
                m_RotateSpeed = rotateSpeed;

                m_Space = space;
            }

            public void SetStats(float walkSpeed, float runSpeed, float rotateSpeed, float jumpHeight, Space space)
            {
                m_WalkSpeed = walkSpeed;
                m_RunSpeed = runSpeed;
                m_RotateSpeed = rotateSpeed;

                m_Space = space;
            }

            public void SetSurface(in Vector3 normal)
            {
                m_Normal = normal;
            }

            public void Move(float deltaTime, in Vector2 axis, in Vector3 target, bool isRun, bool isMoving, out Vector2 animAxis, out bool isAir)
            {
                var cameraLook = Vector3.Normalize(target - m_Transform.position);
                var targetForward = m_LastForward;

                ConvertMovement(in axis, in cameraLook, out var movement);
                if (movement.sqrMagnitude > 0.5f) {
                    m_LastForward = Vector3.Normalize(movement);
                }

                CaculateGravity(deltaTime, out isAir);
                Displace(deltaTime, in movement, isRun);
                Turn(in targetForward, isMoving);
                UpdateRotation(deltaTime);

                GenAnimationAxis(in movement, out animAxis);
            }

            private void ConvertMovement(in Vector2 axis, in Vector3 targetForward, out Vector3 movement)
            {
                Vector3 forward;
                Vector3 right;

                if (m_Space == Space.Self)
                {
                    forward = new Vector3(targetForward.x, 0f, targetForward.z).normalized;
                    right = Vector3.Cross(Vector3.up, forward).normalized;
                }
                else
                {
                    forward = Vector3.forward;
                    right = Vector3.right;
                }

                movement = axis.x * right + axis.y * forward;
                movement = Vector3.ProjectOnPlane(movement, m_Normal);
            }

            private void Displace(float deltaTime, in Vector3 movement, bool isRun)
            {
                Vector3 displacement = (isRun ? m_RunSpeed : m_WalkSpeed) * movement;
                displacement += m_GravityAcelleration;
                displacement *= deltaTime;

                m_Controller.Move(displacement);
            }

            private void CaculateGravity(float deltaTime, out bool isAir)
            {
                m_jumpTimer = Mathf.Max(m_jumpTimer - deltaTime, 0f);

                if (m_Controller.isGrounded)
                {
                    m_GravityAcelleration = Physics.gravity;
                    isAir = false;

                    return;
                }

                isAir = true;

                m_GravityAcelleration += Physics.gravity * deltaTime;
                return;
            }

            private void GenAnimationAxis(in Vector3 movement, out Vector2 animAxis)
            {
                if(m_Space == Space.Self)
                {
                    animAxis = new Vector2(Vector3.Dot(movement, m_Transform.right), Vector3.Dot(movement, m_Transform.forward));
                }
                else
                {
                    animAxis = new Vector2(Vector3.Dot(movement, Vector3.right), Vector3.Dot(movement, Vector3.forward));
                }
            }

            private void Turn(in Vector3 targetForward, bool isMoving)
            {
                var angle = Vector3.SignedAngle(m_Transform.forward, Vector3.ProjectOnPlane(targetForward, Vector3.up), Vector3.up);

                if (!m_IsRotating)
                {
                    if (!isMoving && Mathf.Abs(angle) < m_Luft)
                    {
                        m_IsRotating = false;
                        return;
                    }

                    m_IsRotating = true;
                }

                m_TargetAngle = angle;
            }

            private void UpdateRotation(float deltaTime)
            {
                if(!m_IsRotating)
                {
                    return;
                }

                var rotDelta = m_RotateSpeed * deltaTime;
                if (rotDelta + Mathf.PI * 2f + Mathf.Epsilon >= Mathf.Abs(m_TargetAngle))
                {
                    rotDelta = m_TargetAngle;
                    m_IsRotating = false;
                }
                else
                {
                    rotDelta *= Mathf.Sign(m_TargetAngle);
                }

                m_Transform.Rotate(Vector3.up, rotDelta);
            }
        }

        private class AnimationHandler
        {
            private readonly Animator m_Animator;
            private readonly string m_VerticalID;
            private readonly string m_StateID;

            private readonly float k_InputFlow = 4.5f;

            private float m_FlowState;
            private Vector2 m_FlowAxis;

            public AnimationHandler(Animator animator, string verticalID, string stateID)
            {
                m_Animator = animator;
                m_VerticalID = verticalID;
                m_StateID = stateID;
            }

            public void Animate(in Vector2 axis, float state, float deltaTime)
            {
                m_Animator.SetFloat(m_VerticalID, m_FlowAxis.magnitude);
                m_Animator.SetFloat(m_StateID, Mathf.Clamp01(m_FlowState));

                m_FlowAxis = Vector2.ClampMagnitude(m_FlowAxis + k_InputFlow * deltaTime * (axis - m_FlowAxis).normalized, 1f);
                m_FlowState = Mathf.Clamp01(m_FlowState + k_InputFlow * deltaTime * Mathf.Sign(state - m_FlowState));
            }

            public void AnimateIK(in Vector3 target, in LookWeight lookWeight)
            {
                m_Animator.SetLookAtPosition(target);
                m_Animator.SetLookAtWeight(lookWeight.weight, lookWeight.body, lookWeight.head, lookWeight.eyes);
            }
        }
        #endregion
    }
}
```

## Objects\PropDatabase.cs

```csharp
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor; // 必须在编辑器环境下
#endif

public class PropDatabase : MonoBehaviour
{
    public static PropDatabase Instance;

    [Header("可变身物品列表 (索引即为 ID)")]
    public List<GameObject> propPrefabs;
    
    [Header("仅限服务器自动生成的动物列表")]
    public List<GameObject> animalPrefabs;

    private Dictionary<int, PropTarget> runtimeProps = new Dictionary<int, PropTarget>();
    [Header("全局视觉设置")]
    public Material defaultHighlightMaterial; // <--- 在 Inspector 拖入你的 Mat_Outline
    public Material ancientHighlightMaterial;  // <--- 新增：在此处拖入你的 Mat_TeamOutline (绿色版)
    private void Awake()
    {
        Instance = this;
    }

    // ========================================================
    // 【自动化工具】自动分配场景中所有物体的 PropID
    // ========================================================
    #if UNITY_EDITOR
    [ContextMenu("Update All Scene Prop IDs")]
    public void UpdateScenePropIDs()
    {
        // 1. 获取场景中所有的 PropTarget
        PropTarget[] allTargets = Object.FindObjectsOfType<PropTarget>(true);
        int updatedCount = 0;
        int warningCount = 0;

        foreach (var target in allTargets)
        {
            // 2. 找到该实例对应的 Prefab 资源物体
            GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(target.gameObject);
            
            if (prefabSource == null)
            {
                // 如果这个物体不是从 Prefab 拖出来的，或者 Prefab 链接断了
                Debug.LogWarning($"[PropDatabase] 物体 '{target.name}' 不是 Prefab 实例，无法自动分配 ID。", target);
                warningCount++;
                continue;
            }

            // 3. 在列表中寻找这个 Prefab 的索引
            int index = propPrefabs.IndexOf(prefabSource);

            if (index != -1)
            {
                // 4. 赋值并标记脏数据（确保保存场景时能存住）
                if (target.propID != index)
                {
                    Undo.RecordObject(target, "Auto Assign Prop ID");
                    target.propID = index;
                    EditorUtility.SetDirty(target);
                    updatedCount++;
                }
            }
            else
            {
                Debug.LogError($"[PropDatabase] 场景物体 '{target.name}' 的 Prefab 不在 propPrefabs 列表中！请先将其加入列表。", target);
                warningCount++;
            }
        }

        Debug.Log($"[PropDatabase] 自动分配完成！更新了 {updatedCount} 个物体，存在 {warningCount} 个异常，总计检查了 {allTargets.Length} 个物体。");
    }
    #endif

    // --- 原有逻辑保持不变 ---
    public void RegisterProp(int id, PropTarget prop)
    {
        if (!runtimeProps.ContainsKey(id)) runtimeProps.Add(id, prop);
        else runtimeProps[id] = prop;
    }

    public bool GetPropPrefab(int id, out GameObject prefab)
    {
        prefab = null;
        if (id < 0 || id >= propPrefabs.Count) return false;
        prefab = propPrefabs[id];
        return prefab != null;
    }

    public bool GetPropData(int id, out Mesh mesh, out Material[] materials, out Vector3 scale)
    {
        mesh = null; materials = null; scale = Vector3.one;
        if (runtimeProps.TryGetValue(id, out PropTarget prop))
        {
            // Renderer rd = prop.GetComponentInChildren<Renderer>();
            // 优先寻找名字里带 "LOD0" 的渲染器，如果没有，就取第一个
            Renderer rd = prop.GetComponentsInChildren<Renderer>()
                            .FirstOrDefault(r => r.name.Contains("LOD0")) 
                        ?? prop.GetComponentInChildren<Renderer>();
            if (rd != null)
            {
                materials = rd.sharedMaterials;
                scale = prop.transform.lossyScale;
                if (rd is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else {
                    MeshFilter mf = prop.GetComponentInChildren<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }
                return mesh != null;
            }
        }
        return false;
    }
}
```

## Objects\PropTarget.cs

```csharp
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Mirror;

public class PropTarget : NetworkBehaviour
{
    public bool isLocalTempRevealed = false; // 仅本地有效，不联网
    [Header("Identity")]
    [SyncVar]
    public int propID; 
    [SyncVar(hook = nameof(OnAncientStatusChanged))] 
    public bool isAncientTree = false;
    public int runtimeID;
    [Header("Possession State")]
    [SyncVar(hook = nameof(OnPossessedChanged))]
    public bool isHiddenByPossession = false; // 树是否因为被附身而隐藏
    [Header("Tree Manager Settings")]
    public bool isStaticTree = false; // 在 Inspector 中勾选此项
    [SyncVar(hook = nameof(OnScoutedChanged))]
    public bool isScouted = false; // 是否已被女巫发现
    [Header("Visuals")]
    // 修改：改为存储多个渲染器
    private Renderer[] allLODRenderers; 
    
    [Header("Highlight Settings")]
    [SerializeField] private Material outlineMaterialSource; 
    private Material outlineInstance;
    private bool isHighlighted = false;

    // 【新增属性】方便 WitchPlayer 判断是否需要初始化
    public bool IsInitialized => allLODRenderers != null && allLODRenderers.Length > 0;

    // private Material[] originalMaterials; // 保存初始材质数组
    // private Material[] highlightedMaterials; // 预存高亮时的材质数组
    private List<Material[]> originalMaterialsList = new List<Material[]>();
    private List<Material[]> highlightedMaterialsList = new List<Material[]>();

    // 当服务器同步 isAncientTree 状态到客户端时调用
    void OnAncientStatusChanged(bool oldVal, bool newVal)
    {
        // 如果渲染器已经获取到了，重新初始化材质数组以应用绿色高亮
        if (IsInitialized)
        {
            // 先销毁旧的实例，防止内存泄漏
            if (outlineInstance != null) Destroy(outlineInstance);
            outlineInstance = null;
            
            InitMaterials();
        }
    }

    // 只有古树需要同步这个 Hook
    void OnPossessedChanged(bool oldVal, bool newVal)
    {
        // 禁用/启用树的所有视觉效果和碰撞
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = !newVal;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = !newVal;
        
        // 如果是古树，还需要关闭名字显示（如果有的话）
        // if (nameText != null) nameText.gameObject.SetActive(!newVal);
    }  
    [Server]
    public void ServerSetHidden(bool hidden)
    {
        isHiddenByPossession = hidden;
    }
    public override void OnStartClient()
    {
        Register();
    }
     public override void OnStartServer()
    {
        Register();
    }   
    private void Register()
    {
        runtimeID = (int)netId; 
        if (PropDatabase.Instance != null)
        {
            PropDatabase.Instance.RegisterProp(runtimeID, this);
        }
        
        // 如果 targetRenderer 还没赋值（比如场景里的静态物体），尝试自动查找
        // if (targetRenderer == null)
        // {
        //     targetRenderer = GetComponent<Renderer>() ?? GetComponentInChildren<Renderer>();
        // }
        // 自动获取所有子物体的渲染器（包括 LOD0, LOD1）
        allLODRenderers = GetComponentsInChildren<Renderer>();
        InitMaterials();
    }

    // private void InitMaterials()
    // {
    //     if (targetRenderer == null) return;

    //     // 1. 记录初始材质
    //     originalMaterials = targetRenderer.sharedMaterials;

    //     // 2. 预热高亮材质
    //     if (outlineMaterialSource != null)
    //     {
    //         if (outlineInstance == null) 
    //         {
    //             outlineInstance = new Material(outlineMaterialSource);
    //             outlineInstance.color = Color.yellow; 
    //         }

    //         highlightedMaterials = new Material[originalMaterials.Length + 1];
    //         for (int i = 0; i < originalMaterials.Length; i++)
    //         {
    //             highlightedMaterials[i] = originalMaterials[i];
    //         }
    //         highlightedMaterials[highlightedMaterials.Length - 1] = outlineInstance;
    //     }
    // }
        
    private void InitMaterials()
    {
        if (allLODRenderers == null || allLODRenderers.Length == 0) return;

        originalMaterialsList.Clear();
        highlightedMaterialsList.Clear();

        // 【核心修改】选择源材质：如果是古树，使用古树材质，否则使用默认材质
        Material sourceMat = outlineMaterialSource;
        if (sourceMat == null && PropDatabase.Instance != null)
        {
            sourceMat = isAncientTree ? 
                PropDatabase.Instance.ancientHighlightMaterial : 
                PropDatabase.Instance.defaultHighlightMaterial;
        }

        foreach (var renderer in allLODRenderers)
        {
            if (renderer == null) continue;

            // 1. 记录初始材质
            Material[] originals = renderer.sharedMaterials;
            originalMaterialsList.Add(originals);

            // 2. 准备高亮材质
            if (sourceMat != null) // 使用上面判断后的 sourceMat
            {
                // 确保 outlineInstance 存在 (这里可以每个 PropTarget 共享一个，也可以每个生成实例)
                // 为了防止不同物体颜色干扰，建议保持 new Material 的逻辑
                if (outlineInstance == null) 
                {
                    outlineInstance = new Material(sourceMat);
                    // 【关键】如果是古树，强制设为绿色；否则设为黄色
                    Color highlightColor = isAncientTree ? Color.green : Color.yellow;
                    
                    if(outlineInstance.HasProperty("_OutlineColor"))
                        outlineInstance.SetColor("_OutlineColor", highlightColor);
                    else if(outlineInstance.HasProperty("_BaseColor")) // 兼容不同 Shader
                        outlineInstance.SetColor("_BaseColor", highlightColor);
                }

                Material[] highlighted = new Material[originals.Length + 1];
                for (int j = 0; j < originals.Length; j++) highlighted[j] = originals[j];
                highlighted[highlighted.Length - 1] = outlineInstance;
                highlightedMaterialsList.Add(highlighted);
            }
            else
            {
                // 如果连全局的都没有，则用原材质占位，防止越界
                highlightedMaterialsList.Add(originals);
            }
        }
    }

    // 【新增】供女巫变身后手动初始化
    public void ManualInit(int id, GameObject visualRoot)
    {
        this.propID = id;
        // 获取变身模型下所有的渲染器，这样变身后的物体也能支持多级 LOD 高亮
        this.allLODRenderers = visualRoot.GetComponentsInChildren<Renderer>();
        InitMaterials();
    }


    private void Awake()
    {

    }

    public void SetHighlight(bool active)
    {
        if (allLODRenderers == null) return;
        // --- 【新增：游戏结束强制关闭】 ---
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            active = false;
            isScouted = false; // 确保侦察状态不干扰
            isLocalTempRevealed = false; // 确保临时透视不干扰
        }
        // ---------------------------------
        // 获取本地玩家身份
        var localPlayer = NetworkClient.localPlayer?.GetComponent<GamePlayer>();
        bool isWitch = localPlayer != null && localPlayer.playerRole == PlayerRole.Witch;

        // 判定逻辑：
        // 女巫看到高亮的情况：准星正指着 (active) OR 已经被发现 (isScouted)
        // 猎人看到高亮的情况：仅准星正指着 (active)
        // 修改判定逻辑：增加 isLocalTempRevealed
        bool shouldShow = active || (isWitch && (isScouted || isLocalTempRevealed));

        if (isHighlighted == shouldShow) 
        {
            // 状态没变时，如果是女巫且高亮着，刷新一次属性（防止从 active 切换到 permanent 时颜色没变）
            if (shouldShow && isWitch) UpdateColorAndZTest(active);
            return;
        }

        isHighlighted = shouldShow;
        
        // 应用材质球切换
        for (int i = 0; i < allLODRenderers.Length; i++)
        {
            var renderer = allLODRenderers[i];
            if (renderer == null) continue;
            renderer.materials = shouldShow ? highlightedMaterialsList[i] : originalMaterialsList[i];
        }

        if (shouldShow) UpdateColorAndZTest(active);
    }

    void OnDestroy()
    {
        if (outlineInstance != null) Destroy(outlineInstance);
    }
    // 当服务器同步侦察状态时，通知本地女巫刷新视觉
    void OnScoutedChanged(bool oldVal, bool newVal)
    {
        // 获取本地玩家并通知 TeamVision 刷新
        var localPlayer = NetworkClient.localPlayer?.GetComponent<GamePlayer>();
        if (localPlayer != null && localPlayer.playerRole == PlayerRole.Witch)
        {
            localPlayer.GetComponent<TeamVision>()?.ForceUpdateVisuals();
        }
    }

    private void UpdateColorAndZTest(bool isActiveByCrosshair)
    {
        if (outlineInstance == null) return;

        Color finalColor = Color.yellow;
        float zTestMode = 8f; // 默认为 Always (穿透)
        float outlineWidth = 0.03f; // 默认宽度（对应你Shader里的默认值）

        if (isAncientTree)
        {
            // ================= 古树逻辑 =================
            finalColor = Color.green;
            zTestMode = 8f; // 常驻穿透，方便女巫远距离看到目标
            outlineWidth = 0.05f;  // 古树可以稍微加粗，显示重要性
        }
        else
        {
            // ================= 普通树逻辑 =================
            if (isActiveByCrosshair && !isScouted)
            {
                // 正在被检视，但还没完成
                finalColor = Color.yellow;
                zTestMode = 8f; // 检视时穿透，方便看清轮廓
                outlineWidth = 0.03f;
            }
            else if (isScouted)
            {
                // 检视完成：普通树常驻
                // 方案：亮银色 (R:0.8, G:0.8, B:1.0) 比灰色显眼得多
                finalColor = new Color(0.8f, 0.8f, 1.0f, 1.0f); 
                
                // 不穿透透视
                zTestMode = 4f; 
                
                // 【关键点】加粗轮廓！因为不透视，加粗可以防止被细小的枝叶完全盖住
                outlineWidth = 0.06f; 
            }
        }

        // 设置 Shader 参数
        outlineInstance.SetColor("_OutlineColor", finalColor);
        outlineInstance.SetFloat("_ZTestMode", zTestMode);
        // 动态修改轮廓粗细
        outlineInstance.SetFloat("_OutlineWidth", outlineWidth);
    }
}
```

## Objects\ResurrectionPortal.cs

```csharp
using UnityEngine;
using Mirror;

public class ResurrectionPortal : MonoBehaviour 
{
    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        WitchPlayer witch = other.GetComponentInParent<WitchPlayer>();
        if (witch == null) return;

        // 逻辑 A：原有的小动物复活
        if (witch.isInSecondChance && !witch.isPermanentDead)
        {
            witch.ServerRevive();
        }

        // 逻辑 B：【新增】检测带回古树
        // 只有驾驶员 (possessedTreeNetId != 0) 且还没完成过任务的能触发
        if (witch.possessedTreeNetId != 0 && !witch.hasDeliveredTree)
        {
            UnityEngine.Debug.Log($"[Server] Driver {witch.playerName} reached the portal with a tree!");
            witch.ServerOnReachPortal();
        }
    }

}
```

## Objects\ServerAnimalSpawner.cs

```csharp
using UnityEngine;
using Mirror;
using System.Collections.Generic;
using System.Diagnostics;

public class ServerAnimalSpawner : NetworkBehaviour
{
    [Header("生成区域")]
    public BoxCollider spawnArea; // 拖入用于定义范围的 BoxCollider
    public LayerMask groundLayer; // 地面层级（建议设为 Environment 或 Terrain）

    [Server]
    public void SpawnAnimals(int countFromManager)
    {
        // 1. 基础检查
        if (spawnArea == null)
        {
            // Debug.LogError("[Server] 未分配 spawnArea (BoxCollider)!");
            UnityEngine.Debug.LogError("[Server] spawnArea (BoxCollider) not assigned!");
            return;
        }

        var db = PropDatabase.Instance;
        if (db == null || db.animalPrefabs.Count == 0) return;

        // 获取 Box 的边界信息
        Bounds bounds = spawnArea.bounds;

        for (int i = 0; i < countFromManager; i++)
        {
            // 2. 在 Box 范围内随机选一个 X 和 Z
            float randomX = Random.Range(bounds.min.x, bounds.max.x);
            float randomZ = Random.Range(bounds.min.z, bounds.max.z);

            // 3. 计算高度 (Y 轴)
            // 逻辑：从 Box 的最顶部（bounds.max.y）向下发射射线
            Vector3 rayOrigin = new Vector3(randomX, bounds.max.y, randomZ);
            Vector3 spawnPoint;

            // 尝试通过射线击中地面来确定 Y 坐标
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, bounds.size.y + 10f, groundLayer))
            {
                spawnPoint = hit.point;
            }
            else
            {
                // 兜底方案：如果没射中地面，直接取 Box 的中心点高度
                spawnPoint = new Vector3(randomX, bounds.center.y, randomZ);
                //改成英文debug
                // Debug.LogWarning($"[Spawner] 未能在位置 {randomX}, {randomZ} 下方找到地面，使用默认高度。");
                UnityEngine.Debug.LogWarning($"[Spawner] Could not find ground below position {randomX}, {randomZ}, using default height.");
            }

            // 4. 随机选一只动物 Prefab
            int animalIndex = Random.Range(0, db.animalPrefabs.Count);
            GameObject prefab = db.animalPrefabs[animalIndex];

            // 5. 实例化
            GameObject animal = Instantiate(prefab, spawnPoint, Quaternion.Euler(0, Random.Range(0, 360), 0));
            
            // 6. 映射 propID
            PropTarget propTarget = animal.GetComponentInChildren<PropTarget>();
            if (propTarget != null)
            {
                propTarget.propID = db.propPrefabs.IndexOf(prefab);
            }

            // 7. 网络生成
            NetworkServer.Spawn(animal);
        }
    }
}
```

## Objects\TreeManager.cs

```csharp
using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class TreeManager : NetworkBehaviour
{
    public static TreeManager Instance;

    [Header("Spawn Protection")]
    public float spawnSafeRadius = 4.0f; // 出生点周围保护半径

    [Header("Forest Density & Spacing")]
    public float minTreeSpacing = 2.5f; // 树与树之间的最小间距
    [Tooltip("当位置冲突时，最大尝试偏移寻找新位置的次数")]
    public int maxAdjustmentAttempts = 5; 
    [Tooltip("每次尝试偏移的距离步长")]
    public float adjustmentStep = 1.5f;

    [Header("Settings")]
    public float positionOffsetRange = 0.5f; // 最终分布时的微小随机抖动
    public bool randomYRotation = true;    // 随机旋转

    private List<PropTarget> allTrees = new List<PropTarget>();

    private void Awake()
    {
        Instance = this;
    }

    [Server]
    public void ShuffleTrees()
    {
        // 1. 获取所有出生点
        List<Vector3> spawnPoints = new List<Vector3>();
        var nss = Object.FindObjectsOfType<Mirror.NetworkStartPosition>();
        foreach (var sp in nss) spawnPoints.Add(sp.transform.position);
        
        if (spawnPoints.Count == 0) {
            GameObject[] groups = { GameObject.Find("WitchSpawnPoints"), GameObject.Find("HunterSpawnPoints") };
            foreach(var g in groups) if(g != null) foreach(Transform t in g.transform) spawnPoints.Add(t.position);
        }

        // 2. 初始化树木状态并收集所有原始坐标
        allTrees.Clear();
        List<Vector3> rawCandidatePositions = new List<Vector3>();

        PropTarget[] sceneProps = Object.FindObjectsOfType<PropTarget>();
        foreach (var prop in sceneProps)
        {
            if (prop.isStaticTree)
            {
                prop.isAncientTree = false; 
                prop.isHiddenByPossession = false;
                prop.ServerSetHidden(false);
                allTrees.Add(prop);
                rawCandidatePositions.Add(prop.transform.position);
            }
        }

        if (allTrees.Count == 0) return;
   
        // ================= 【核心修复：打乱树木对象列表】 =================
        for (int i = 0; i < allTrees.Count; i++) {
            PropTarget tempProp = allTrees[i];
            int randomIndex = Random.Range(i, allTrees.Count);
            allTrees[i] = allTrees[randomIndex];
            allTrees[randomIndex] = tempProp;
        }

        // 3. 打乱候选坐标顺序
        for (int i = 0; i < rawCandidatePositions.Count; i++) {
            Vector3 temp = rawCandidatePositions[i];
            int randomIndex = Random.Range(i, rawCandidatePositions.Count);
            rawCandidatePositions[i] = rawCandidatePositions[randomIndex];
            rawCandidatePositions[randomIndex] = temp;
        }

        // 4. 【核心逻辑修改】筛选并尝试偏移坐标
        List<Vector3> finalFilteredPositions = new List<Vector3>();
        
        foreach (Vector3 originalPos in rawCandidatePositions) {
            Vector3 currentTestPos = originalPos;
            // bool successfullyPlaced = false;

            // 尝试多次偏移以寻找合法位置
            for (int attempt = 0; attempt <= maxAdjustmentAttempts; attempt++) {
                if (IsPositionValid(currentTestPos, finalFilteredPositions, spawnPoints)) {
                    finalFilteredPositions.Add(currentTestPos);
                    // successfullyPlaced = true;
                    break;
                }

                // 如果不合法，计算一个随机偏移量尝试推开
                // 随着尝试次数增加，偏移半径逐渐扩大
                Vector2 randomNudge = Random.insideUnitCircle.normalized * (adjustmentStep * (attempt + 1));
                currentTestPos = new Vector3(originalPos.x + randomNudge.x, originalPos.y, originalPos.z + randomNudge.y);
            }
            
            // 如果经过多次偏移还是找不到位置，该树将在后续步骤被隐藏（防止重叠卡死）
        }

        Debug.Log($"[TreeManager] {allTrees.Count} trees total. Successfully spaced {finalFilteredPositions.Count} positions.");

        // 5. 分配最终坐标
        int dynamicAncientCount = GameManager.Instance.GetCalculatedAncientTreeCount();
        int actualAncientCount = 0;

        for (int i = 0; i < allTrees.Count; i++)
        {
            if (i >= finalFilteredPositions.Count) {
                // 如果偏移重试后依然无法满足间距限制，将多余的树移除地图
                allTrees[i].transform.position = Vector3.down * 100f; 
                allTrees[i].ServerSetHidden(true);
                continue;
            }

            Vector3 targetBasePos = finalFilteredPositions[i];
            
            // 最后的微小随机抖动（不破坏整体间距）
            float jitter = Mathf.Min(positionOffsetRange, minTreeSpacing * 0.1f);
            Vector3 finalPos = targetBasePos + new Vector3(Random.Range(-jitter, jitter), 0, Random.Range(-jitter, jitter));
            
            allTrees[i].transform.position = finalPos;
            if (randomYRotation) allTrees[i].transform.rotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);

            if (i < dynamicAncientCount) {
                allTrees[i].isAncientTree = true;
                actualAncientCount++;
            }
        }

        if (GameManager.Instance != null) {
            GameManager.Instance.availableAncientTreesCount = actualAncientCount;
        }
    }

    // 辅助判定函数：检查坐标是否同时远离已选中的树和出生点
    private bool IsPositionValid(Vector3 pos, List<Vector3> acceptedPositions, List<Vector3> spawnPoints)
    {
        // 检查与出生点的距离
        foreach (Vector3 spPos in spawnPoints) {
            if (Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(spPos.x, spPos.z)) < spawnSafeRadius)
                return false;
        }

        // 检查与其他树的距离
        foreach (Vector3 acceptedPos in acceptedPositions) {
            if (Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(acceptedPos.x, acceptedPos.z)) < minTreeSpacing)
                return false;
        }

        return true;
    }
}
```

## Objects\WorldBoundaryManager.cs

```csharp
using UnityEngine;

public class WorldBoundaryManager : MonoBehaviour
{
    public static WorldBoundaryManager Instance { get; private set; }

    [Header("设置")]
    public bool isActive = true;
    public float radiusOffset = 0.5f; // 考虑到角色半径的缓冲距离

    private SphereCollider sphereCollider;

    public Vector3 Center => transform.position;
    public float Radius => (sphereCollider != null) ? (sphereCollider.radius * transform.lossyScale.x) : 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        sphereCollider = GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            Debug.LogError("WorldBoundaryManager: 找不到 SphereCollider！");
        }
    }

    // 提供给所有物体使用的静态约束方法
    public Vector3 GetConstrainedPosition(Vector3 currentPos, float characterRadius = 0.5f)
    {
        if (!isActive) return currentPos;

        Vector3 center = Center;
        float radius = Radius - characterRadius - radiusOffset;
        
        float dist = Vector3.Distance(currentPos, center);

        if (dist > radius)
        {
            Vector3 fromCenterToPos = (currentPos - center).normalized;
            return center + fromCenterToPos * radius;
        }

        return currentPos;
    }

    // 用于 AI 逻辑：判断一个点是否在球体内
    public bool IsWithinBoundary(Vector3 targetPos)
    {
        return Vector3.Distance(targetPos, Center) < (Radius - 1f);
    }
}
```

## Player\AnimationEventBridge.cs

```csharp
using UnityEngine;

/// <summary>
/// 动画事件桥接器：挂载在包含 Animator 组件的模型节点上
/// 负责接收动画帧事件，并转发给父节点的核心控制脚本
/// </summary>
[RequireComponent(typeof(Animator))]
public class AnimationEventBridge : MonoBehaviour
{
    private HunterPlayer hunterPlayer;

    void Awake()
    {
        // 自动在父节点中寻找 HunterPlayer 脚本
        hunterPlayer = GetComponentInParent<HunterPlayer>();
        
        if (hunterPlayer == null)
        {
            Debug.LogError("AnimationEventBridge: 在父节点中找不到 HunterPlayer 脚本！");
        }
    }

    /// <summary>
    /// 在 Shoot_Single 动画的第 11 帧添加 Event，并选择此函数！
    /// </summary>
    public void OnShootHitPoint()
    {
        if (hunterPlayer != null)
        {
            // 触发真正的攻击特效和逻辑
            hunterPlayer.ExecuteAttackEffect();
        }
    }
}
```

## Player\BulletTracerEffect.cs

```csharp
using UnityEngine;
using System.Collections;

public class BulletTracerEffect : MonoBehaviour
{
    [Header("设置")]
    public LineRenderer lineRenderer;
    public float duration = 0.1f; // 弹道存在时间（非常短）

    public void Init(Vector3 startPos, Vector3 endPos)
    {
        lineRenderer.positionCount = 2;
        // 1. 设置线的起点和终点
        lineRenderer.SetPosition(0, startPos);
        lineRenderer.SetPosition(1, endPos);

        // 2. 开始消失协程
        StartCoroutine(FadeAndDestroy());
    }

    IEnumerator FadeAndDestroy()
    {
        float timer = 0f;
        float startWidth = lineRenderer.startWidth;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            // 计算进度 0.0 -> 1.0
            float progress = timer / duration;

            // 视觉效果：让线随着时间变得越来越细，直到看不见
            // Lerp(a, b, t) 是在 a 和 b 之间插值
            float currentWidth = Mathf.Lerp(startWidth, 0f, progress);

            lineRenderer.startWidth = currentWidth;
            lineRenderer.endWidth = currentWidth; // 尾部也变细

            // 或者你可以改颜色透明度：
            // Color c = lineRenderer.material.color;
            // c.a = Mathf.Lerp(1, 0, progress);
            // lineRenderer.material.color = c;

            yield return null; // 等待下一帧
        }

        // 3. 销毁这个特效物体
        Destroy(gameObject);
    }
}
```

## Player\FistWeapon.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class FistWeapon : WeaponBase
{
    [Header("近战特有设置")]
    public float attackRadius = 0.5f; // 拳头的判定半径
    public float attackDistance = 2.0f; // 攻击距离
    public float stunDuration = 0.5f; // 眩晕时间

    private void Awake()
    {
        // 初始化默认值
        damage = 10f;
        fireRate = 0.5f;
        weaponName = "Fist";
    }

    public override void OnFire(Vector3 origin, Vector3 direction)
    {   
        AudioManager.Instance?.Play2D("拳头攻击");
        Debug.Log($" fired a punch!");
        nextFireTime = Time.time + fireRate;
        if (isServer)
        {
            if (Physics.SphereCast(origin, attackRadius, direction, out RaycastHit hit, attackDistance))
            {
                GamePlayer target = hit.collider.GetComponent<GamePlayer>();
                if (target == null)
                    target = hit.collider.GetComponentInParent<GamePlayer>();

                if (target != null)
                {
                    // 造成伤害
                    target.ServerTakeDamage(damage);
                    if (target is WitchPlayer)
                    {
                        StartCoroutine(ApplyMicroStun(target));
                    }

                    Debug.Log($"[Fist] Punched {target.playerName}!");
                }
            }
        }
    }

    // 服务器端协程：短暂眩晕
    [Server]
    private IEnumerator ApplyMicroStun(GamePlayer target)
    {
        if (!target.isStunned)
        {
            target.isStunned = true;
            yield return new WaitForSeconds(stunDuration);
            target.isStunned = false;
        }
    }
}
```

## Player\GamePlayer.cs

```csharp
using UnityEngine;
using Mirror;
using TMPro;
using System.Collections;          
using System.Collections.Generic;
using kcp2k;

public enum PlayerRole
{
    None,
    Witch,
    Hunter
}

// 抽象基类：不能直接挂载，必须由 Witch 或 Hunter 继承
public abstract class GamePlayer : NetworkBehaviour
{
    // ==========================================
    // 静态全局列表：方便 TeamVision 访问所有玩家
    // ==========================================
    public static List<GamePlayer> AllPlayers = new List<GamePlayer>();
    [Header("组件")]
    [SerializeField] protected CharacterController controller;
    [SerializeField] public TextMeshPro nameText; // 头顶名字

    [Header("挣脱设置")]
    public int requiredClicks = 2; // 需要按多少次空格才能挣脱
    public float maxTrapTime = 6.0f; // 6秒后还没挣脱就释放

    [Header("外部受力(击退)")]
    protected Vector3 impact = Vector3.zero;

    [SyncVar]
    public int currentClicks = 0; // 当前挣扎次数
    private float trapTimer = 0f;// 计时器

    [Header("同步属性")]
    [SyncVar] public Gender myGender = Gender.Male;
    [SyncVar] public string syncedSkill1Name = "";
    [SyncVar] public string syncedSkill2Name = "";
    [SyncVar] public uint caughtInTrapNetId = 0; // 记录当前是被哪个陷阱抓住了
    [SyncVar] public int ping;
    [SyncVar(hook = nameof(OnStunChanged))]
    public bool isStunned = false; // 是否被禁锢
    [SyncVar(hook = nameof(OnTrappedStatusChanged))]
    public bool isTrappedByNet = false;
    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName;
    [SyncVar(hook = nameof(OnHealthChanged))]// 血量变化钩子
    public float currentHealth = 100f;
    [SyncVar(hook = nameof(OnMaxHealthChanged))]
    public float maxHealth = 100f;
    public float manaRegenRate = 5f;
    [SyncVar(hook = nameof(OnManaChanged))]
    public float currentMana = 100f;
    [SyncVar(hook = nameof(OnMaxManaChanged))]
    public float maxMana = 100f;

    [SyncVar(hook = nameof(OnMorphChanged))]
    public bool isMorphed = false; // 当前是否处于变身状态 
    [SyncVar(hook = nameof(OnMorphedPropIDChanged))]
    public int morphedPropID = -1; // -1 表示没变身，>=0 表示对应的 PropID

    [SyncVar]
    public PlayerRole playerRole = PlayerRole.None;

    [SyncVar(hook = nameof(OnSecondChanceChanged))]
    public bool isInSecondChance = false; // 是否在小动物逃跑状态

    [SyncVar(hook = nameof(OnPermanentDeadChanged))]
    public bool isPermanentDead = false; // 是否永久死亡
    [SyncVar]
    public bool isInvulnerable = false; // 是否无敌

    [Header("移动参数")]
    [SyncVar(hook = nameof(OnMoveSpeedChanged))] // 添加 SyncVar 和钩子
    public float moveSpeed = 6f;
    public float gravity = -9.81f;
    [Header("跳跃参数")]
    public float jumpHeight = 2.0f; // 跳跃高度 (建议改小一点，50太高了会飞出地图)
    public float groundCheckDistance = 1.1f; // 射线长度：胶囊体高度的一半(1.0) + 缓冲(0.1)
    public LayerMask groundLayer; // 地面层级，防止检测到自己
    // 【新增】空中控制力 (0 = 完全无法在空中变向，10 = 空中变向也很灵活)
    // 建议设置为 1.0f 到 5.0f 之间，既有惯性又能微调
    public float airControl = 2.0f;
    [Header("Mouse Look")]
    public float mouseSensitivity = 2f;
    float xRotation = 0f;

    public GameObject crosshairUI;
    protected Vector3 velocity;
    // 场景脚本引用
    public SceneScript sceneScript;
    // 【修改】这里定义一次，子类直接使用，不要在子类重复定义
    [HideInInspector] // 可选：不在Inspector显示，防止乱改
    public string goalText;
    // 在类字段区域新增或修改
    public bool isFirstPerson = true;           // 默认第一人称


    [Header("Chat State")]
    public bool isChatting = false; // 用于禁止移动

    [Header("球形边界设置")]
    public bool useSphereBoundary = true;
    public Vector3 sphereCenter = Vector3.zero; // 你的球体中心坐标
    public float sphereRadius = 20f; // 你的球体半径

    // 新增一个变量缓存 ChatUI
    private GameChatUI gameChatUI;

    // 【抽象方法】强制子类必须实现 Attack
    protected abstract void Attack();
    [SyncVar] protected float syncedSpeed;// 让速度在全网同步


    // --------------------------------------------------------
    // 生命周期
    // --------------------------------------------------------
    // 在 OnDestroy 中确保移除自己（你代码里写了 OnStopClient，但 OnDestroy 更保险）
    private void OnDestroy()
    {
        if (AllPlayers.Contains(this))
        {
            AllPlayers.Remove(this);
        }
    }
    // 在静态构造或合适的地方提供一个清理方法
    public static void CleanupDeadReferences()
    {
        AllPlayers.RemoveAll(p => p == null || p.gameObject == null);
    }
    // 服务器初始化角色
    public override void OnStartServer()
    {
        base.OnStartServer();
        // 【核心修复】服务器启动时也加入列表
        if (!AllPlayers.Contains(this)) AllPlayers.Add(this);
        if (this is WitchPlayer) playerRole = PlayerRole.Witch;
        else if (this is HunterPlayer) playerRole = PlayerRole.Hunter;
        else playerRole = PlayerRole.None;
    }
    public override void OnStopServer()
    {
        // 【核心修复】服务器断开时移除
        if (AllPlayers.Contains(this)) AllPlayers.Remove(this);
        base.OnStopServer();
    }
    // 客户端初始化
    public override void OnStartClient()
    {
        base.OnStartClient();
        // 加入全局列表
        if (!AllPlayers.Contains(this)) AllPlayers.Add(this);
        // 只要有新玩家加入，刷新计数
        RefreshSceneUI();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        // 移除全局列表
        if (AllPlayers.Contains(this)) AllPlayers.Remove(this);
        // 只要有玩家离开，刷新计数
        RefreshSceneUI();
    }

    // 当本地玩家控制这个物体时调用
    public override void OnStartLocalPlayer()
    {

        // ---------------------------------------------------------
        // 【新增】名字同步逻辑 (仿照 PlayerScript)
        // ---------------------------------------------------------
        if (PlayerSettings.Instance != null && !string.IsNullOrWhiteSpace(PlayerSettings.Instance.PlayerName))
        {
            // 如果本地存了名字，立刻告诉服务器覆盖掉那个默认的 "Hunter (Late)"
            CmdUpdateName(PlayerSettings.Instance.PlayerName);
        }
        else
        {
            // 如果没存名字（极其罕见），就告诉服务器用个随机名或者保持默认
            // CmdUpdateName("Player " + Random.Range(100, 999));
        }
        // ---------------------------------------------------------

        // 设置场景 UI 显示角色和名字
        sceneScript = FindObjectOfType<SceneScript>();
        // 【新增】获取 ChatUI 引用
        gameChatUI = FindObjectOfType<GameChatUI>();
        if (sceneScript != null)
        {
            // 用子类的类名作为角色名（最简单方式）
            string roleName = GetType().Name.Replace("Player", "");
            sceneScript.RoleText.text = $"Role: {roleName}";
            sceneScript.NameText.text = $"Name: {playerName}";
            sceneScript.HealthSlider.maxValue = maxHealth;
            sceneScript.HealthSlider.value = currentHealth;
            sceneScript.ManaSlider.maxValue = maxMana;
            sceneScript.ManaSlider.value = currentMana;
            // 【核心修改】直接使用 goalText，不需要判断类型转换了
            // 因为 goalText 已经在子类的 Awake/Start 中被赋值了
            if (sceneScript.GoalText != null)
            {
                sceneScript.GoalText.text = goalText;
            }
            crosshairUI = sceneScript.Crosshair;
        }
        xRotation = 0f;
        UpdateCameraView(); // 初始化相机位置

        // 【修改】初始锁定鼠标
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        StartCoroutine(UpdatePingRoutine());
    }


    // --------------------------------------------------------
    // 逻辑循环
    // --------------------------------------------------------
    // 2. 在 GamePlayer.cs 底部添加对应的 Command
    [Command]
    private void CmdDebugTriggerWin(PlayerRole winner)
    {
        //改成英文debug
        Debug.Log($"[DEBUG] Server received win request from player {playerName}: {winner}");
        
        // 调用 GameManager 的服务器结束逻辑
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ServerEndGame(winner);
        }
    }

    public virtual void Update()
    {
        // 只有本地玩家能控制移动
        if (isLocalPlayer)
        {
            // ================== 【调试按键接口】 ==================
            // 允许 Client 玩家通过 Command 请求服务器结束游戏
            if (Application.isEditor || Debug.isDebugBuild)
            {
                if (Input.GetKeyDown(KeyCode.I))
                {
                    CmdDebugTriggerWin(PlayerRole.Witch);
                }
                if (Input.GetKeyDown(KeyCode.O))
                {
                    CmdDebugTriggerWin(PlayerRole.Hunter);
                }
            }
            // ====================================================
            // ================== 【调试按键：修改剩余时间】 ==================
            if (Input.GetKeyDown(KeyCode.P))
            {
                // 如果是编辑模式或调试版本（安全性检查，防止正式服玩家乱点）
                if (Application.isEditor || UnityEngine.Debug.isDebugBuild)
                {
                    CmdDebugSetTimer(65f); // 1分05秒 = 65秒
                }
            }
            // =============================================================
            // 【新增】如果引用为空，尝试再次查找（防空指针）
            if (sceneScript == null) sceneScript = FindObjectOfType<SceneScript>();
            if (gameChatUI == null) gameChatUI = FindObjectOfType<GameChatUI>();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (isChatting) { if (gameChatUI != null) gameChatUI.SetChatState(false); }
                else { if (sceneScript != null) sceneScript.TogglePauseMenu(); }
                return;
            }
            // --- 处理挣扎逻辑 ---
            if (isStunned && isTrappedByNet && Input.GetKeyDown(KeyCode.Space))
            {
                CmdStruggle();
            }

            // 按 T 切换第一人称 / 第三人称
            if (Input.GetKeyDown(KeyCode.T))
            {
                isFirstPerson = !isFirstPerson;
                UpdateCameraView();
            }

            // 【修改】始终调用 HandleMovement，在方法内部判断是否处理输入
            // 这样即使 Cursor 解锁了，重力代码依然会运行
            // --- 处理输入向量 ---
            Vector2 input = Vector2.zero;
            // 只有在没被控制、没在聊天、且鼠标锁定的情况下才获取 WASD 输入
            if (!isStunned && !isChatting && Cursor.lockState == CursorLockMode.Locked)
            {
                input = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            }
            HandleMovementOverride(input);
            // 执行边界约束
            ApplySphereBoundary();


            // 攻击输入还是只有锁定时才允许
            if (Cursor.lockState == CursorLockMode.Locked && !isStunned) // 只有不被晕时才能攻击
            {
                // 【核心修改】只有在非结束状态下才允许执行 HandleInput (CmdAttack)
                if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.GameOver)
                {
                    HandleInput();
                }
            }
            // // 测试用输入
            // if (Input.GetKeyDown(KeyCode.K)) CmdTakeDamage(10f); // 测试用
            // if (Input.GetKeyDown(KeyCode.J)) CmdUseMana(15f);    // 测试用

        }
        if (isServer)
        {
            ServerRegenerateMana();
        }
    }
    // 必须通过 Command 让服务器去修改 SyncVar
    [Command]
    private void CmdDebugSetTimer(float newTime)
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.gameTimer = newTime;
            UnityEngine.Debug.Log($"[Debug] Player {playerName} set game timer to {newTime}s");
        }
    }
    // --------------------------------------------------------
    // 功能函数
    // --------------------------------------------------------
    protected void ApplySphereBoundary()
    {
        // 只有本地玩家需要执行位置约束（服务器会同步结果）
        // 并且确保单例存在
        if (!isLocalPlayer || WorldBoundaryManager.Instance == null || !WorldBoundaryManager.Instance.isActive)
            return;

        // 从管理器获取约束后的位置
        // 传入 transform.position 和 CharacterController 的半径
        Vector3 constrainedPos = WorldBoundaryManager.Instance.GetConstrainedPosition(
            transform.position,
            controller.radius
        );

        // 如果位置发生了变化（说明出界了），强制拉回
        if (constrainedPos != transform.position)
        {
            // 直接设置 transform.position 对 CharacterController 有效
            transform.position = constrainedPos;
        }
    }

    // 新增方法：根据视角更新相机位置
    public virtual void UpdateCameraView()
    {
        // 如果游戏已经结束，不再强制控制相机位置
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
            return;
        if (isFirstPerson)
        {
            Camera.main.transform.SetParent(transform);
            Camera.main.transform.localPosition = new Vector3(0, 1.055f, 0.278f);
            Camera.main.transform.localRotation = Quaternion.identity;  
        }
        else
        {
            Camera.main.transform.SetParent(transform);
            Camera.main.transform.localPosition = new Vector3(0, 2.405f, -3.631f);
            Camera.main.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        }
    }

    // 将原来的 HandleMovement 改名为 HandleMovementOverride 并接受参数
    protected virtual void HandleMovementOverride(Vector2 inputOverride)
    {
        // 1. 地面检测
        float rayLength = (controller.height * 0.5f) + 0.3f;
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
        bool isHit = Physics.Raycast(rayOrigin, Vector3.down, rayLength, groundLayer);
        bool actuallyOnGround = isHit || controller.isGrounded;

        // 2. 这里的 isInputLocked 只决定是否可以进行【旋转视角】
        // 只有在聊天或者打开菜单时才锁定视角
        bool isViewLocked = isChatting || (sceneScript != null && sceneScript.pauseMenuPanel.activeSelf);

        // 【新增】应用击退外力 (在计算 targetVelocity 之前)
        if (impact.magnitude > 0.2f)
        {
            controller.Move(impact * Time.deltaTime);
            // 摩擦力衰减（数值越大停得越快，5f 适合比较滑行的击退）
            impact = Vector3.Lerp(impact, Vector3.zero, 5f * Time.deltaTime); 
        }

        // 3. 移动计算 (inputOverride 如果是 zero，这里会自动处理减速)
        Vector3 inputDir = (transform.right * inputOverride.x + transform.forward * inputOverride.y);
        if (inputDir.magnitude > 1f) inputDir.Normalize();

        Vector3 targetVelocity = inputDir * moveSpeed;
        float groundAccel = 8f;
        float groundDecel = 12f;

        float currentAccel = actuallyOnGround ? (inputDir.magnitude > 0 ? groundAccel : groundDecel) : airControl;

        velocity.x = Mathf.MoveTowards(velocity.x, targetVelocity.x, currentAccel * Time.deltaTime * moveSpeed);
        velocity.z = Mathf.MoveTowards(velocity.z, targetVelocity.z, currentAccel * Time.deltaTime * moveSpeed);

        // 4. 重力和跳跃
        if (actuallyOnGround && velocity.y < 0) velocity.y = -2f;
        else velocity.y += gravity * Time.deltaTime;

        // 注意：跳跃需要判断是否满足基础条件 (如没有被眩晕)
        if (actuallyOnGround && CanJump() && !isViewLocked && Input.GetButtonDown("Jump"))
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            // 【新增：调用钩子函数】
            OnJumpTriggered(); 
        }

        controller.Move(velocity * Time.deltaTime);

        // 【核心修复】：如果游戏结束，彻底禁止脚本触摸 Camera.main
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
            return;


        // 5. 【核心修改】旋转视角逻辑
        // 只要视角没被锁定（聊天/菜单），即使处于 stunned 状态，也可以转头
        if (!isViewLocked)
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * 100f * Time.deltaTime;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * 100f * Time.deltaTime;
            
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -80f, 80f);
            Camera.main.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
            transform.Rotate(Vector3.up * mouseX);
        }
    }
    // 判断当前状态是否允许起跳（子类可重写添加更多限制）
    protected virtual bool CanJump()
    {
        return !isStunned; // 默认只要没被禁锢就能跳
    }
    // 【新增：添加钩子函数】
    protected virtual void OnJumpTriggered() { }
    protected virtual void HandleMovement()
    {
        // 1. 更加精准的状态检测
        // 射线起点稍微高一点（从膝盖位置发射），长度稍微长一点
        float rayLength = (controller.height * 0.5f) + 0.3f;
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
        bool isHit = Physics.Raycast(rayOrigin, Vector3.down, rayLength, groundLayer);

        // 结合 Controller 的状态，防止在斜坡上判定丢失
        bool actuallyOnGround = isHit || controller.isGrounded;

        // 2. 输入锁定
        bool isInputLocked = isChatting || (sceneScript != null && Cursor.lockState != CursorLockMode.Locked);

        // 3. 获取输入方向
        float x = 0f; float z = 0f;
        if (!isInputLocked) { x = Input.GetAxis("Horizontal"); z = Input.GetAxis("Vertical"); }
        Vector3 inputDir = (transform.right * x + transform.forward * z);
        if (inputDir.magnitude > 1f) inputDir.Normalize();

        // 4. 计算目标水平速度
        Vector3 targetVelocity = inputDir * moveSpeed;

        // 5. 【核心修改】找回惯性的速度计算
        // 这里的参数决定了惯性的强弱：
        // groundAccel: 地面启动速度 (越大启动越快)
        // groundDecel: 地面摩擦力 (越大停得越快，设置小一点就有溜冰感)
        float groundAccel = 8f;
        float groundDecel = 12f;

        // 选择当前的加速度
        float currentAccel;
        if (actuallyOnGround)
        {
            // 如果有输入，用加速度；没输入（想停下来），用摩擦力
            currentAccel = (inputDir.magnitude > 0) ? groundAccel : groundDecel;
        }
        else
        {
            // 空中加速度（airControl），通常很小，产生巨大的惯性
            currentAccel = airControl;
        }

        // 平滑改变速度 (不再乘以 10f，让变化过程肉眼可见)
        velocity.x = Mathf.MoveTowards(velocity.x, targetVelocity.x, currentAccel * Time.deltaTime * moveSpeed);
        velocity.z = Mathf.MoveTowards(velocity.z, targetVelocity.z, currentAccel * Time.deltaTime * moveSpeed);

        // 6. 重力处理 (修复出生漂浮)
        if (actuallyOnGround && velocity.y < 0)
        {
            // 已经在地面时，保持一个小小的下压力
            velocity.y = -2f;
        }
        else
        {
            // 只要不在地面，重力就会一直累加，确保哪怕出生在 0.1米高度也会掉下去
            velocity.y += gravity * Time.deltaTime;
        }

        // 7. 跳跃逻辑
        if (actuallyOnGround && !isInputLocked && Input.GetButtonDown("Jump"))
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            actuallyOnGround = false; // 瞬间起跳，脱离地面判定
        }

        // 8. 执行最终移动
        controller.Move(velocity * Time.deltaTime);

        // 9. 旋转视角 (保持不变)
        if (!isInputLocked)
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * 100f * Time.deltaTime;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * 100f * Time.deltaTime;
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -80f, 80f);
            Camera.main.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
            transform.Rotate(Vector3.up * mouseX);
        }

        // 调试射线：绿色代表判定为地面，红色代表空中
        Debug.DrawRay(rayOrigin, Vector3.down * rayLength, actuallyOnGround ? Color.green : Color.red);
    }
    public virtual void HandleInput()
    {
        if (Input.GetMouseButtonDown(0)) CmdAttack();
    }

    // 虚方法，让女巫类去实现具体的变身逻辑
    protected virtual void HandleDeath()
    {
        // 默认死亡逻辑（比如猎人被打死，暂时直接重置或出局）
        isPermanentDead = true;
        UnityEngine.Debug.Log($"{playerName} has died.");
    }


    private void RefreshSceneUI()
    {
        // 尝试寻找场景脚本并刷新
        SceneScript ss = FindObjectOfType<SceneScript>();
        if (ss != null)
        {
            ss.UpdateAlivePlayerCount();
        }
    }

    // --------------------------------------------------------
    // 网络同步与命令
    // --------------------------------------------------------
    [Command]
    public void CmdSyncSkillNames(string s1, string s2)
    {
        syncedSkill1Name = s1;
        syncedSkill2Name = s2;
    }
    // 【核心方法】释放玩家并立即销毁陷阱
    [Server]
    public void ServerReleaseAndDestroyTrap()
    {
        // 1. 找到对应的陷阱并销毁
        if (caughtInTrapNetId != 0)
        {
            if (NetworkServer.spawned.TryGetValue(caughtInTrapNetId, out NetworkIdentity trapIdentity))
            {
                Debug.Log($"destroy trap: {trapIdentity.name}");
                NetworkServer.Destroy(trapIdentity.gameObject);
            }
        }

        // 2. 重置玩家状态
        isStunned = false;
        isTrappedByNet = false;
        caughtInTrapNetId = 0;
        currentClicks = 0;
        trapTimer = 0f;
        
        Debug.Log($"{playerName} is released");
    }
    // 修改捕获方法
    [Server]
    public void ServerGetTrappedByTrap(uint trapId)
    {
        if (isTrappedByNet) return; 

        isStunned = true;
        isTrappedByNet = true;
        caughtInTrapNetId = trapId; // 记录陷阱ID
        trapTimer = 0f;
        currentClicks = 0;
        Debug.Log($"{playerName} get trapped by trap:{trapId}   ！");
    }
    private IEnumerator UpdatePingRoutine()
    {
        while (true)
        {
            if (isLocalPlayer && NetworkClient.active)
            {
                // 获取 RTT 转换为毫秒并发送给服务器
                int currentPing = (int)(NetworkTime.rtt * 1000);
                CmdUpdatePing(currentPing);
            }
            yield return new WaitForSeconds(1.5f); // 每1.5秒更新一次，节省带宽
        }
    }

    [Command]
    private void CmdUpdatePing(int newPing)
    {
        ping = newPing;
    }

    // 【新增】命令：更新名字
    [Command]
    public void CmdUpdateName(string newName)
    {
        // 简单的验证
        if (string.IsNullOrWhiteSpace(newName)) return;
        if (newName.Length > 16) newName = newName.Substring(0, 16);

        // 修改 SyncVar，自动同步给所有人
        playerName = newName;

        // 服务器日志
        Debug.Log($"[Server] Player {connectionToClient.connectionId} updated name to: {newName}");
    }

    // 计时器逻辑修改
    [ServerCallback]
    void LateUpdate()
    {
        if (isStunned)
        {
            trapTimer += Time.deltaTime;

            // ★ 修改点：超时 = 自动释放 (而不是处决)
            if (trapTimer >= maxTrapTime)
            {
                ServerReleaseAndDestroyTrap();
            }
        }
    }
    // 服务器端兜网抓住
    [Server]
    public void ServerGetTrapped()
    {
        if (isStunned && isTrappedByNet) return; // 已经被抓了就不重复抓
        isStunned = true; // 继承基类的禁止移动
        isTrappedByNet = true;
        trapTimer = 0f;
        currentClicks = 0;

        Debug.Log("被抓住了！开始计时！");
    }

    // 客户端按空格 -> 呼叫服务器
    [Command]
    void CmdStruggle()
    {
        currentClicks++;

        // 判定：点击次数够了 -> 成功挣脱
        if (currentClicks >= requiredClicks)
        {
            ServerReleaseAndDestroyTrap();
        }
    }

    [Server]
    void ServerEscape()
    {
        isStunned = false;
        isTrappedByNet = false; // 清除网兜标记
        Debug.Log("成功挣脱！");
    }


    [Command] public void CmdAttack() => Attack();

    [Command]
    public void CmdTakeDamage(float amount)
    {
        currentHealth = Mathf.Max(0, currentHealth - amount);
    }
    [Command]
    public void CmdUseMana(float amount)
    {
        if (currentMana >= amount) currentMana -= amount;
    }
    //自动恢复蓝量的函数
    [Server]
    void ServerRegenerateMana()
    {
        if (currentMana < maxMana)
        {
            currentMana = Mathf.Clamp(currentMana + manaRegenRate * Time.deltaTime, 0, maxMana);
        }
    }

    // 受伤函数
    [Server]
    public virtual void ServerTakeDamage(float amount)
    {
        // 如果无敌或永久死亡，不处理伤害
        if (isInvulnerable || isPermanentDead) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        //改成英文debug
        Debug.Log($"{playerName} took {amount} damage, current health: {currentHealth}");
        if (currentHealth <= 0)
        {
            HandleDeath();
        }
    }

    // Hook 函数：当名字在服务器改变并同步到客户端时调用
    void OnNameChanged(string oldName, string newName)
    {
        // 1. 更新头顶的 3D 文字 (给别人看的)
        if (nameText != null) nameText.text = newName;

        // 2. 【核心修复】如果这是“我自己”，顺便更新左上角的 UI (给自己看的)
        if (isLocalPlayer)
        {
            // 确保引用存在
            if (sceneScript == null) sceneScript = FindObjectOfType<SceneScript>();

            if (sceneScript != null)
            {
                sceneScript.NameText.text = $"Name: {newName}";
            }
        }
    }
    void OnStunChanged(bool oldValue, bool newValue)
    {
        // 可以在这里添加被禁锢时的视觉效果或音效
        if (newValue)
        {
            Debug.Log($"{playerName} is stunned!");
        }
        else
        {
            Debug.Log($"{playerName} is no longer stunned!");
        }
    }

    void OnHealthChanged(float oldValue, float newValue)
    {
        float percent = newValue / maxHealth;

        if (isLocalPlayer && sceneScript != null)
        {
            sceneScript.HealthSlider.value = newValue;
        }
    }
    void OnManaChanged(float oldValue, float newValue)
    {
        float percent = newValue / maxMana;

        if (isLocalPlayer && sceneScript != null)
        {
            sceneScript.ManaSlider.value = newValue;
        }
    }

    // 增加钩子，当状态改变时通知视觉系统
    void OnMorphChanged(bool oldVal, bool newVal)
    {
        // 强制调用 TeamVision 的刷新逻辑（如果有必要）
        // 或者仅仅依靠 TeamVision 的协程检测
    }

    // 建议添加一个钩子函数用于调试（可选）
    protected virtual void OnMoveSpeedChanged(float oldSpeed, float newSpeed)
    {
        // 可以在这里打印日志查看速度是否真的同步过来了
        // Debug.Log($"Speed synced: {newSpeed}");
    }

    protected virtual void OnMorphedPropIDChanged(int oldID, int newID)
    {
        // 这个钩子在所有客户端运行（包括新加入的）
        // 子类 WitchPlayer 会重写这个逻辑
    }

    // 增加一个钩子方便客户端处理 UI（比如显示“快跑！”）
    protected virtual void OnSecondChanceChanged(bool oldVal, bool newVal) { }
    // 添加虚方法供子类重写
    protected virtual void OnPermanentDeadChanged(bool oldVal, bool newVal)
    {
        if (newVal)
        {
            // 通用的死亡逻辑（隐藏名字等）
            if (nameText != null) nameText.gameObject.SetActive(false);
        }
        // 只要有人永久死亡，刷新计数
        RefreshSceneUI();
    }

    protected void OnMaxHealthChanged(float oldValue, float newValue)
    {
        if (isLocalPlayer && sceneScript != null)
        {
            sceneScript.HealthSlider.maxValue = newValue;
        }
    }
    protected void OnMaxManaChanged(float oldValue, float newValue)
    {
        if (isLocalPlayer && sceneScript != null)
        {
            sceneScript.ManaSlider.maxValue = newValue;
        }
    }

    // ---------------------------------------------------
    // 聊天网络逻辑
    // ---------------------------------------------------
    [Command]
    public void CmdSendGameMessage(string message, ChatChannel channel)
    {
        // 简单防刷校验
        if (string.IsNullOrWhiteSpace(message)) return;
        if (message.Length > 100) message = message.Substring(0, 100);

        // 调用 Rpc 分发给所有客户端
        RpcReceiveGameMessage(playerName, message, channel, playerRole);
    }

    [ClientRpc]
    private void RpcReceiveGameMessage(string senderName, string msg, ChatChannel channel, PlayerRole senderRole)
    {
        // 1. 获取本地玩家
        GamePlayer localPlayer = null;
        foreach (var p in AllPlayers)
        {
            if (p.isLocalPlayer) { localPlayer = p; break; }
        }
        if (localPlayer == null) return;

        // 2. 判断是否应该显示该消息
        bool shouldShow = false;

        if (channel == ChatChannel.All)
        {
            shouldShow = true; // 全局消息谁都看
        }
        else if (channel == ChatChannel.Team)
        {
            // 只有队友或者是发送者自己才看得到
            if (localPlayer.playerRole == senderRole || localPlayer.playerName == senderName)
            {
                shouldShow = true;
            }
        }

        // 3. 显示消息
        if (shouldShow)
        {
            GameChatUI chatUI = FindObjectOfType<GameChatUI>();
            if (chatUI != null)
            {
                // 根据角色决定名字颜色
                Color roleColor = (senderRole == PlayerRole.Witch) ? Color.magenta :
                                  (senderRole == PlayerRole.Hunter) ? Color.cyan : Color.white;

                chatUI.AppendMessage(senderName, msg, channel, roleColor);
            }
        }
    }
    void OnTrappedStatusChanged(bool oldVal, bool newVal)
    {
        // 获取本地玩家（那个正在看屏幕的人）
        GamePlayer localPlayer = NetworkClient.localPlayer?.GetComponent<GamePlayer>();
        if (localPlayer == null) return;

        // 获取本地玩家身上的 TeamVision 脚本并强制刷新一次
        TeamVision tv = localPlayer.GetComponent<TeamVision>();
        if (tv != null)
        {
            // 我们在 TeamVision 里增加一个 Public 方法
            tv.ForceUpdateVisuals(); 
        }
    }
    [Command]
    protected void CmdUpdateAnimationSpeed(float speed)
    {
        syncedSpeed = speed; // 服务器更新这个值，所有客户端都会收到
    }

    //新增 TargetRpc 接收击退力
    [TargetRpc]
    public void TargetApplyKnockback(NetworkConnection target, Vector3 force)
    {
        // 将外力叠加到当前的 impact 上
        impact += force;
    }
}
```

## Player\GunWeapon.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class GunWeapon : WeaponBase
{
    [Header("猎枪特有设置")]
    public float range = 100;// 射程
    public GameObject impactEffectPrefab; // 命中特效预制体
    private void Awake()
    {
        weaponName = "Gun";
    }
    public override void OnFire(Vector3 origin, Vector3 direction)
    {

        // 1. 设置冷却
        nextFireTime = Time.time + fireRate;
        // 3. 服务器进行射线检测
        if (isServer)
        {
            // 方案：起点稍微向前偏移 0.6米，跳出猎人自己的 CharacterController 范围
            Vector3 startPos = origin + direction * 1.2f;

            if (Physics.Raycast(startPos, direction, out RaycastHit hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                // CharacterController 会被识别为 hit.collider
                // 【核心修复】使用 GetComponentInParent，因为 Collider 可能在模型子节点上
                GamePlayer target = hit.collider.GetComponentInParent<GamePlayer>();
                // 调试打印：看看由于打中了什么而没射中
                if (target == null) {
                    Debug.Log($"Shot hit object without Player script: {hit.collider.name} on Layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
                }       
                if (target != null)
                {
                    // 获取攻击者（枪是在猎人手里的，所以父级一定是 HunterPlayer）
                    GamePlayer attacker = GetComponentInParent<GamePlayer>();
                    if (target == attacker) return;
                    // --- 【队友伤害检查逻辑】 ---
                    bool isSameTeam = (target.playerRole == attacker.playerRole);
                    bool canDamage = !isSameTeam || GameManager.Instance.FriendlyFire;

                    if (canDamage)
                    {
                        target.ServerTakeDamage(damage);
                        Debug.Log($"[GunWeapon] {attacker.playerName} shot {target.playerName}. FF: {isSameTeam}");
                    }
                    else
                    {
                        Debug.Log($"[GunWeapon] Hit blocked by Friendly Fire setting!");
                    }
                }
                RpcSpawnImpact(hit.point, hit.normal);   
            }
            else
            {
                Debug.Log("[Server] Raycast hit nothing.");
            }

        }
    }
    [ClientRpc]
    void RpcSpawnImpact(Vector3 hitPoint, Vector3 surfaceNormal)
    {
        // 如果没有配特效，在控制台发出警告并返回
        if (impactEffectPrefab == null) 
        {
            Debug.LogWarning("[GunWeapon] 警告：没有在 Inspector 中分配命中特效 (Impact Effect Prefab)！");
            return;
        }

        // 【核心修复】生成位置顺着法线向外偏移 0.02 米，防止被墙体吞没或发生 Z-Fighting 闪烁
        Vector3 spawnPos = hitPoint + surfaceNormal * 0.02f;
        
        // 生成特效，LookRotation 让特效的 Z 轴朝向墙面外侧
        GameObject effect = Instantiate(impactEffectPrefab, spawnPos, Quaternion.LookRotation(surfaceNormal));
        
        // 2秒后自动销毁
        Destroy(effect, 2.0f);
    }
}

```

## Player\HunterPlayer.cs

```csharp
using Unity.VisualScripting;
using UnityEngine;
using Mirror;
using System;

public class HunterPlayer : GamePlayer
{
    [Header("Execution Settings")]
    public float executionRange = 3.0f; // 处决距离
    public float executionDamage = 40f; // 处决伤害
    public float executionRecoveryTime = 2.0f; // 猎人硬直时间
    // 用于冷却UI的辅助变量
    private bool wasCoolingDown = false;
    //定义事件
    public event Action<int> OnWeaponFired;
    // 猎人专用武器数组
    public GameObject[] hunterWeapon;
    // 当前武器索引（同步变量，变化时调用 OnWeaponChanged）
    [SyncVar(hook = nameof(OnWeaponChanged))]
    public int currentWeaponIndex = 0;
    [Header("Animation")]
    [SerializeField] private Animator hunterAnimator; // 在 Inspector 中拖入猎人的 Animator
    // 【新增 1】定义记录上一帧位置的变量
    private Vector3 lastPosition;
    private bool nextPunchIsRight = false; // 记录左右交替的状态
    [Header("Input Buffering")]
    public float attackBufferTime = 0.2f; // 缓冲窗口大小：冷却结束前 0.2s 内的点击有效
    private float lastAttackInputTime = -1f; // 上次尝试点击攻击的时间戳
    [Header("Fist Melee Settings")]
    public float fistAttackLockDuration = 1f; 
    private float meleeLockEndTime = 0f; // 记录锁定结束的具体时间点
    // 定义一个快捷属性判断是否处于锁定状态
    private bool IsInMeleeLockout => Time.time < meleeLockEndTime;
    // 【新增】重写父类的起跳许可，出拳硬直期间禁止起跳
    protected override bool CanJump()
    {
        // 必须满足父类的条件（没被禁锢），且当前没有处于出拳硬直状态
        return base.CanJump() && !IsInMeleeLockout;
    }
    // 【新增】在初始化时赋值给父类的字段
    private void Awake()
    {
        goalText = "Hunt Down The Witch Until the Time Runs Out!";
    }
    // 1. 重写移动逻辑，在硬直期间强制输入为 0
    protected override void HandleMovementOverride(Vector2 inputOverride)
    {
        if (IsInMeleeLockout)
        {
            inputOverride = Vector2.zero;
            velocity.x = 0;
            velocity.z = 0;
            if (controller.isGrounded) velocity.y = -2f;
        }
        base.HandleMovementOverride(inputOverride);
    }
    public override void UpdateCameraView()
    {
        // 【核心修复】：如果游戏已经结束，绝对不要去动相机，否则会把相机从胜利点抓回来
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
            return;
        if (isFirstPerson)
        {
            Camera.main.transform.SetParent(transform);
            Camera.main.transform.localPosition = new Vector3(0.01f, 1.12f, 0.59f);
            Camera.main.transform.localRotation = Quaternion.identity;  
        }
        else
        {
            Camera.main.transform.SetParent(transform);
            Camera.main.transform.localPosition = new Vector3(0, 3.09f, -3.74f);
            Camera.main.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        }
    }

    // 重写基类的抽象方法
    protected override void Attack()
    {
        // 这里是服务器端运行的代码
        //改成英文debug
        // Debug.Log($"<color=green>【猎人】{playerName} 释放了技能：开枪射击！</color>");
        Debug.Log($"<color=green>[Hunter] {playerName} used skill: Shoot Gun!</color>");
        // 在这里写具体的射线检测逻辑...
        // if (Physics.Raycast(...)) { ... }
    }
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        // 确保鼠标锁定在准星上
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        // 先禁用所有武器，由脚本控制active状态
        foreach (GameObject weapon in hunterWeapon)
        {
            weapon.SetActive(false);
        }
        ChangeWeapon(currentWeaponIndex);
    // 确保本地猎人看到的是隐藏的女巫相关 UI
        if (SceneScript.Instance != null)
        {
            // 1. 隐藏女巫的 F 键道具槽（你原本已有的逻辑）
            if (SceneScript.Instance.itemSlot != null)
            {
                SceneScript.Instance.itemSlot.gameObject.SetActive(false);
            }

            // 2. 【新增】隐藏女巫的变身技能槽
            if (SceneScript.Instance.morphSlot != null)
            {
                SceneScript.Instance.morphSlot.gameObject.SetActive(false);
            }
        }
    }
    public override void OnStartClient()
    {
        base.OnStartClient();
        // 记录出生时的位置，防止 00 第一帧计算出巨大的瞬移距离
        lastPosition = transform.position;
    }
    public override void OnStartServer()
    {
        base.OnStartServer();

        // moveSpeed = 7f;
        // mouseSensitivity = 2.5f;
        // manaRegenRate = 8f;
    }
    public void OnWeaponChanged(int oldWeaponIndex, int newWeaponIndex)
    {
        if (oldWeaponIndex >= 0 && oldWeaponIndex < hunterWeapon.Length)
        {
            hunterWeapon[oldWeaponIndex].SetActive(false);
        }
        if (newWeaponIndex >= 0 && newWeaponIndex < hunterWeapon.Length)
        {
            hunterWeapon[newWeaponIndex].SetActive(true);
            // 【新增】防止切枪时如果粒子正在播放卡在半空中，强制停止
            var weaponBase = hunterWeapon[newWeaponIndex].GetComponent<WeaponBase>();
            if (weaponBase != null && weaponBase.muzzleFlash != null)
            {
                weaponBase.muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            // 【核心修改】切换武器时，通知 Animator 是否正在持枪
            if (hunterAnimator != null && weaponBase != null)
            {
                bool isRifleStyle = (weaponBase.weaponName == "Gun" || weaponBase.weaponName == "NetLauncher");
                hunterAnimator.SetBool("isHoldingGun", isRifleStyle);
            }
        }
    }
    public override void Update()
    {
        base.Update();
        // 【核心修改】如果游戏结束，不处理切枪、开火和处决逻辑
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            lastAttackInputTime = -1f; // 清空可能的输入缓冲
            return;
        }
        if (isLocalPlayer)
        {
            // 同步动画速度：如果处于锁定中，强制发 0
            float horizontalSpeed = IsInMeleeLockout ? 0f : new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
            CmdUpdateAnimationSpeed(horizontalSpeed);
            // 切换武器
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ChangeWeapon(0);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                ChangeWeapon(1);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                ChangeWeapon(2);
            }
            if (Input.GetAxis("Mouse ScrollWheel") > 0f)
            {
                int nextIndex = (currentWeaponIndex + 1) % hunterWeapon.Length;
                ChangeWeapon(nextIndex);

            }
            else if (Input.GetAxis("Mouse ScrollWheel") < 0f)
            {
                int nextIndex = (currentWeaponIndex - 1 + hunterWeapon.Length) % hunterWeapon.Length;
                ChangeWeapon(nextIndex);
            }
            // 开火
            // 1. 记录玩家的点击意图
            if (Input.GetMouseButtonDown(0))
            {
                lastAttackInputTime = Time.time;
            }

            // 2. 检测是否有“存着”的指令需要触发
            if (Time.time - lastAttackInputTime <= attackBufferTime)
            {
                WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();

                if (currentWeapon != null && currentWeapon.CanFire())
                {
                    // --- 【新增】：判断是否处于地面（结合原生判断与射线容错，防止下坡误判） ---
                    bool isOnGround = controller.isGrounded || Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, (controller.height * 0.5f) + 0.3f, groundLayer);
                    // 如果武器是拳头且在空中，则拦截攻击
                    if (currentWeapon.weaponName == "Fist" && !isOnGround)
                    {
                        // 仅消耗掉输入缓冲，不执行射击指令（禁止空中出拳）
                        lastAttackInputTime = -1f;
                    }
                    else
                    {
                        lastAttackInputTime = -1f; 
                        currentWeapon.UpdateCooldown(); // 统一消耗冷却

                        // 【核心修改】区分猎枪和其他武器的开火时机
                        if (currentWeapon.weaponName == "Fist")
                        {
                            meleeLockEndTime = Time.time + fistAttackLockDuration;
                            CmdFireWeapon(Camera.main.transform.position, Camera.main.transform.forward);
                            OnWeaponFired?.Invoke(currentWeaponIndex);
                        }
                        // --- 修改这里：将 NetLauncher 加入 Gun 的逻辑 ---
                        else if (currentWeapon.weaponName == "Gun" || currentWeapon.weaponName == "NetLauncher")
                        {
                            // 统统只触发开火动画，真正的逻辑等待第11帧事件
                            CmdTriggerGunAnimation();
                        }
                        // --------------------------------------------
                        else
                        {
                            // 这里的 else 现在通常只走没有特殊定义的武器
                            CmdFireWeapon(Camera.main.transform.position, Camera.main.transform.forward);
                            OnWeaponFired?.Invoke(currentWeaponIndex);
                        }
                    }
                }
            }

            // 处理冷却UI
            HandleCooldownUI();
            // 处决检查
            HandleExecutionCheck(Camera.main.transform.position, Camera.main.transform.forward);
            // 调试绘制（放在武器切换和开火逻辑之后）
            #if UNITY_EDITOR
            if (hunterWeapon != null && currentWeaponIndex < hunterWeapon.Length)
            {
                WeaponBase current = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
                if (current != null && current.weaponName == "Gun")
                {
                    GunWeapon gun = current as GunWeapon;
                    if (gun != null)
                    {
                        Vector3 start = Camera.main.transform.position + Camera.main.transform.forward * 1.2f; // 必须与服务器偏移一致
                        Vector3 end = start + Camera.main.transform.forward * gun.range;
                        Debug.DrawLine(start, end, Color.green, 0f); // 持续到下一帧绘制前
                    }
                }
            }
            #endif
        }
        // 2. 所有人（本地和远程）都根据同步的速度值更新 Animator
        if (hunterAnimator != null)
        {
            // 注意：截图里参数名是小写 "speed"
            hunterAnimator.SetFloat("speed", syncedSpeed, 0.05f, Time.deltaTime);
        }
    }
    [Command]
    private void CmdTriggerGunAnimation()
    {
        // 告诉所有客户端播放开枪动画
        RpcTriggerGunAnimation();
    }

    [ClientRpc]
    private void RpcTriggerGunAnimation()
    {
        if (hunterAnimator != null)
        {
            hunterAnimator.SetTrigger("Shoot");
        }
    }
    // ----------------------------------------------------
    // 4. 【关键接口】供 AnimationEventBridge 在第 11 帧调用
    // ----------------------------------------------------
    public void ExecuteAttackEffect()
    {
        // 只有按下左键的本地玩家，才有资格在第11帧向服务器发送真实的开枪指令
        // 防止所有客户端上的第11帧都跑去让服务器开火，导致一次开出N枪
        if (isLocalPlayer)
        {
            WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
            
            // 确保第11帧时，玩家手里拿的还是枪（防止动画期间切枪导致 Bug）
            if (currentWeapon != null && (currentWeapon.weaponName == "Gun" || currentWeapon.weaponName == "NetLauncher"))
            {
                Vector3 origin = Camera.main.transform.position;
                Vector3 dir = Camera.main.transform.forward;
                CmdExecuteRealGunFire(origin, dir);
            }
        }
    }
    [Command]
    private void CmdExecuteRealGunFire(Vector3 origin, Vector3 direction)
    {
        WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        if (currentWeapon != null && (currentWeapon.weaponName == "Gun" || currentWeapon.weaponName == "NetLauncher"))
        {
            // 1. 服务器执行真正的伤害和射线检测
            currentWeapon.OnFire(origin, direction);
            
            // 2. 告诉所有客户端在此刻播放枪口特效和开火音效
            RpcFireEffect(currentWeaponIndex);
        }
    }
    [Command]
    void CmdChangeWeapon(int weaponIndex)
    {
        if (weaponIndex >= 0 && weaponIndex < hunterWeapon.Length)
        {
            currentWeaponIndex = weaponIndex;
        }
    }
    [Command]
    void CmdFireWeapon(Vector3 origin, Vector3 direction)
    {
        WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        if (currentWeapon != null && currentWeapon.CanFire())
        {
            // 服务器更新冷却
            currentWeapon.UpdateCooldown();
            // 多态分发具体开火逻辑
            currentWeapon.OnFire(origin, direction);
            // 3. 告诉所有客户端同步特效
            RpcFireEffect(currentWeaponIndex);
        }
    }
    [ClientRpc]
    void RpcFireEffect(int weaponIndex)
    {
        // // ★ 关键细节：如果是本地玩家，刚才在 Update 里已经播过了，就别播第二次了
        // if (isLocalPlayer) return;
        // 触发事件
        WeaponBase currentWeapon = hunterWeapon[weaponIndex].GetComponent<WeaponBase>();
        // 猎枪的声音已在 RpcTriggerGunAnimation 中播放，此处跳过
        OnWeaponFired?.Invoke(weaponIndex);
        // --- 新增：触发近战动画逻辑 ---
        if (hunterAnimator != null)
        { 
            if (currentWeapon != null && currentWeapon.weaponName == "Fist") 
            {
                // 1. 设置布尔值，决定这次走左边还是右边的动画分支
                hunterAnimator.SetBool("isPunchRight", nextPunchIsRight);

                // 2. 触发攻击 Trigger0
                hunterAnimator.SetTrigger("Punch");

                // 3. 切换状态：下次攻击换另一只手
                nextPunchIsRight = !nextPunchIsRight;
                
                UnityEngine.Debug.Log($"[Animation] Punching side: {(nextPunchIsRight ? "Left" : "Right")}");
            }
        }
    }

    private void HandleCooldownUI()
    {
        if (sceneScript == null || hunterWeapon.Length == 0) return;

        // 获取当前武器脚本
        WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();

        if (currentWeapon != null)
        {
            // 利用我们在 WeaponBase 做的修改获取冷却比例
            float ratio = currentWeapon.CooldownRatio;

            if (ratio > 0)
            {
                // 正在冷却中：显示 UI
                // ratio 从 1 变到 0，代表类似“倒计时”的效果
                // 颜色设为半透明青色 (Color.cyan) 或者 灰色 (Color.gray)
                sceneScript.UpdateRevertUI(ratio, true);
                wasCoolingDown = true;
            }
            else
            {
                // 冷却结束：隐藏 UI
                if (wasCoolingDown)
                {
                    // 只有刚结束的那一帧调用一次隐藏，避免每帧都调用
                    sceneScript.UpdateRevertUI(0, false);
                    wasCoolingDown = false;
                }
            }
        }
    }

    private void ChangeWeapon(int weaponIndex)
    {
        CmdChangeWeapon(weaponIndex);
        if (sceneScript == null) return;

        string weaponName = "None";
        if (weaponIndex >= 0 && weaponIndex < hunterWeapon.Length)
        {
            WeaponBase weaponBase = hunterWeapon[weaponIndex].GetComponent<WeaponBase>();
            if (weaponBase != null)
            {
                weaponName = weaponBase.weaponName;
            }
        }
        sceneScript.WeaponText.text = weaponName;
    }
    private void HandleExecutionCheck(Vector3 origin, Vector3 direction)
    {
        if (sceneScript == null) return;
        WitchPlayer targetWitch = null;
        Vector3 startPos = origin + direction * 0.6f;
        if (Physics.Raycast(startPos, direction, out RaycastHit hit, executionRange))
        {
            GamePlayer target = hit.collider.GetComponent<GamePlayer>();
            if (target is WitchPlayer witch)
            {
                if (witch.currentHealth > 0 && witch.isTrappedByNet)
                {
                    targetWitch = witch;
                }
            }
        }
        // UI 显示与输入处理
        if (targetWitch != null)
        {
            sceneScript.ExecutionText.gameObject.SetActive(true);

            if (Input.GetKeyDown(KeyCode.F))
            {
                // 发送处决命令
                CmdExecuteWitch(targetWitch.netId);
                // 此时本地立刻隐藏文字
                sceneScript.ExecutionText.gameObject.SetActive(false);
            }
        }
        else
        {
            sceneScript.ExecutionText.gameObject.SetActive(false);
        }
    }

    [Command]
    private void CmdExecuteWitch(uint targetNetId)
    {
        // 1. 校验：不能在硬直期间再次处决
        if (isStunned) return;

        // 2. 获取目标对象
        if (NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity identity))
        {
            WitchPlayer witch = identity.GetComponent<WitchPlayer>();

            if (witch != null && witch.isTrappedByNet)
            {
                float dist = Vector3.Distance(transform.position, witch.transform.position);
                // 允许一点点网络延迟导致的距离误差 (比如 range + 1.0f)
                if (dist <= executionRange + 1.5f)
                {
                    // A. 女巫扣血并释放
                    witch.ServerGetExecuted(executionDamage);

                    // B. 猎人进入硬直
                    isStunned = true;

                    // C. 开启协程或计时器，2秒后恢复
                    StartCoroutine(RecoverFromExecution());

                    Debug.Log($"{playerName} Executed {witch.playerName}!");
                }
            }
        }
    }

    // 服务器端恢复协程
    [Server]
    private System.Collections.IEnumerator RecoverFromExecution()
    {
        yield return new WaitForSeconds(executionRecoveryTime);
        isStunned = false;
    }
    // 致盲效果的 TargetRpc 方法
    [TargetRpc]
    public void TargetBlindEffect(NetworkConnection target, float duration)
    {
        bool wasBlindActive = sceneScript != null && sceneScript.blindPanel != null && sceneScript.blindPanel.activeSelf;
        if (!wasBlindActive)
        {
            AudioManager.Instance?.Play2D("致盲耳鸣音");
        }

        StartCoroutine(BlindRoutine(duration));
        Debug.Log($"[Hunter] {playerName} is Blinded for {duration} seconds.");
    }

    private System.Collections.IEnumerator BlindRoutine(float duration)
    {
        // 假设 SceneScript 里有个全黑的 Image 叫 BlindPanel
        if (sceneScript != null && sceneScript.blindPanel != null)
        {
            sceneScript.blindPanel.SetActive(true);
            yield return new WaitForSeconds(duration);
            sceneScript.blindPanel.SetActive(false);
        }
    }
    // ----------------------------------------------------
    // 跳跃动画触发
    // ----------------------------------------------------

    // 重写基类的跳跃钩子
    protected override void OnJumpTriggered()
    {
        // 增加严格的落地判断：只有 CharacterController 认为在地面上，才发送跳跃指令
        if (isLocalPlayer)
        {
            CmdTriggerJumpAnimation();
        }
    }

    [Command]
    void CmdTriggerJumpAnimation()
    {
        // 1. 在服务器端生成随机数 (0 或 1)
        // 使用 Random.Range(0, 2) 会得到 0 或 1
        int randomIndex = UnityEngine.Random.Range(0, 2);

        // 2. 将随机索引传给 Rpc
        RpcOnJump(randomIndex);
    }

    [ClientRpc]
    void RpcOnJump(int index)
    {
        if (hunterAnimator != null)
        {
            // 3. 先设置随机索引，再触发 Trigger
            hunterAnimator.SetInteger("JumpIndex", index);
            hunterAnimator.SetTrigger("isJump");
            AudioManager.Instance?.Play2D("jump_sound");
            
            // 调试打印，方便你查看触发了哪一个
            // Debug.Log($"[Jump] Triggered animation index: {index}");
        }
    }
}
```

## Player\InvisibilityCloak.cs

```csharp
using UnityEngine;
using Mirror;
using System.Collections;

public class InvisibilityCloak : WitchItemBase
{
    [Header("斗篷参数")]
    public float duration = 5.0f; // 隐身持续时间
    public float speedMultiplier = 1.5f; // 加速倍率
    public AudioClip witchScreamSound; // 嘲讽音效

    private void Awake()
    {
        isActive = true;
        itemName = "Invisibility Cloak";
        cooldown = 15f;
    }

    public override void OnActivate()
    {
        nextUseTime = Time.time + cooldown;
        WitchPlayer player = GetComponentInParent<WitchPlayer>();
        AudioManager.Instance?.Play2D("cloak_activate_sound");
        if (player == null)
        {
            Debug.LogError("InvisibilityCloak: No WitchPlayer found on parent.");
            return;
        }
        Debug.Log($"{player.playerName} is activating Invisibility Cloak.");
        player.CmdUseInvisibilityCloak();
    }

    [Server]
    public void ServerActivateEffect(WitchPlayer player)
    {
        UpdateCooldown();
        Debug.Log($"{player.playerName} activated Invisibility Cloak on server.");
        StartCoroutine(CloakRoutine(player));
        RpcPlayScream(player.transform.position);
    }

    [Server]
    private IEnumerator CloakRoutine(WitchPlayer player)
    {
        float originalSpeed = player.moveSpeed;

        // 1. 设置隐身状态 
        player.isStealthed = true;
        Debug.Log($"{player.playerName} Stealth ON");

        // 2. 加速
        player.moveSpeed *= speedMultiplier;

        Debug.Log($"{player.playerName} used Cloak (Stealth ON)");

        yield return new WaitForSeconds(duration);

        // 3. 恢复状态
        if (player != null)
        {
            player.isStealthed = false;
            player.moveSpeed = originalSpeed;
            Debug.Log($"{player.playerName} Stealth OFF");
        }
    }

    [ClientRpc]
    private void RpcPlayScream(Vector3 pos)
    {
        if (witchScreamSound != null)
            AudioSource.PlayClipAtPoint(witchScreamSound, pos, 1.0f);
    }
}
```

## Player\LifeAmulet.cs

```csharp
using UnityEngine;
using Mirror;

public class LifeAmulet : WitchItemBase
{
    [Header("护符设置")]
    public float protectionWindow = 30f; // 激活后持续30秒有效

    private bool hasUsed = false; // 记录本局是否已经使用过
    private void Awake()
    {
        itemName = "Life Amulet";
        isActive = true;
        cooldown = 999f;
    }
    public override void OnActivate()
    {
        // 1. 检查是否已经使用过
        if (hasUsed)
        {
            Debug.Log("生命护符本局已失效。");
            return;
        }

        // 2. 获取女巫组件
        WitchPlayer player = GetComponentInParent<WitchPlayer>();
        if (player == null) return;

        // 3. 检查女巫当前状态（如果是幽灵或小动物复活赛状态，通常不能用）
        if (player.isPermanentDead || player.isInSecondChance) return;

        // 4. 发送命令激活
        player.CmdActivateAmulet(protectionWindow);

        // 5. 标记为已使用
        hasUsed = true;

        // 更新冷却（虽然只能用一次，但为了防止连点，还是设置一下）
        UpdateCooldown();
    }
}
```

## Player\MagicBroom.cs

```csharp
using UnityEngine;
using Mirror;

public class MagicBroom : WitchItemBase
{
    [Header("魔法扫帚设置")]
    public float doubleJumpForceMultiplier = 2.0f; // 二段跳力度倍率（相对于普通跳跃）
    public void Awake()
    {
        isActive = false;
        itemName = "Magic Broom";
        cooldown = 5f;
    }
    public override void OnActivate()
    {
    }
}
```

## Player\NetBullet.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class NetBullet : MonoBehaviour
{
    [HideInInspector] public PlayerRole ownerRole; // 发射者的阵营
    [ServerCallback] // 只在服务器运行物理
    private void OnTriggerEnter(Collider other)
    {
        // 当网子碰到 CharacterController 时，other 就是该控制器
        // 使用 GetComponentInParent 是最保险的，因为脚本可能在根部
        GamePlayer target = other.GetComponent<GamePlayer>() ?? other.GetComponentInParent<GamePlayer>();

        if (target != null)
        {
            // --- 【队友伤害检查逻辑】 ---
            bool isSameTeam = (target.playerRole == ownerRole);
            bool canTrap = !isSameTeam || GameManager.Instance.FriendlyFire;
           if (canTrap)
            {
                target.ServerGetTrapped();
                UnityEngine.Debug.Log($"[NetBullet] Trapped {target.playerName}");
                Destroy(gameObject); // 抓到后销毁
            }
        }
        // 如果碰到墙壁或地面也销毁
        else if (other.gameObject.layer == LayerMask.NameToLayer("Default"))
        {
             Destroy(gameObject);
        }
    }
}

```

## Player\NetLauncherWeapon.cs

```csharp
using UnityEngine;
using Mirror;

public class NetLauncherWeapon : WeaponBase
{
  [Header("兜网设置")]
  public GameObject netPrefab; // 拖入上面的兜网 Prefab
  public float BulletSpeed = 20f; // 网的飞行速度
  public float lifeTime = 5f; // 网的存在时间

  private void Awake()
  {
    weaponName = "NetLauncher";
  }

  public override void OnFire(Vector3 origin, Vector3 direction)
  {
    // 冷却
    nextFireTime = Time.time + fireRate;

    //服务器生成实体
    if (isServer)
    {
      // 在枪口位置生成网
      // 注意：虽然射击方向是 direction (摄像机朝向)，但为了让网从枪口飞出，我们放在 firePoint
      GameObject net = Instantiate(netPrefab, firePoint.position, Quaternion.LookRotation(direction));
      net.GetComponent<Rigidbody>().velocity = direction * BulletSpeed;
      // 【新增】获取发射者的阵营并传给网子
      PlayerRole shooterRole = GetComponentInParent<GamePlayer>().playerRole;
      NetworkServer.Spawn(net);
      // 设置网的生命周期
      Destroy(net, lifeTime);
    }
  }
}
```

## Player\OnFireEffect.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.Audio;


public class OnFireEffect : MonoBehaviour
{
    [Header("引用")]
    public HunterPlayer hunterPlayer;
    public AudioSource audioSource;

    void OnEnable()
    {
        // 订阅事件
        if (hunterPlayer)
            hunterPlayer.OnWeaponFired += PlayEffects;
    }

    void OnDisable()
    {
        // ★ 记得取消订阅，防止内存泄漏
        if (hunterPlayer)
            hunterPlayer.OnWeaponFired -= PlayEffects;
    }

    // 真正的特效逻辑写在这里
    void PlayEffects(int weaponIndex)
    {
        if (weaponIndex < 0 || weaponIndex >= hunterPlayer.hunterWeapon.Length) return;
        WeaponBase currentWeapon = hunterPlayer.hunterWeapon[weaponIndex].GetComponent<WeaponBase>();
        // 1. 枪口火光
        if (currentWeapon.muzzleFlash != null)
        {
            currentWeapon.muzzleFlash.GetComponent<ParticleSystem>().Play();
        }

        // B. 播放声音
        if (currentWeapon.fireSound != null)
        {
            // PlayOneShot 允许声音重叠，适合高射速
            audioSource.PlayOneShot(currentWeapon.fireSound);
        }

        // 4. 甚至可以加屏幕震动
        // CameraShaker.Shake(0.1f); 
    }
}

```

## Player\PlayerItemManager.cs

```csharp
// --- PlayerItemManager.cs ---
using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class PlayerItemManager : NetworkBehaviour
{
    [Header("Data")]
    public List<WitchItemData> itemDatabase; // 【新增】拖入所有女巫道具的 ScriptableObject
    
    private WitchPlayer witch;
    private WitchItemBase activeItemInstance; // 缓存当前激活的道具脚本

    public override void OnStartLocalPlayer()
    {
        witch = GetComponent<WitchPlayer>();
        string selectedClassName = PlayerSettings.Instance.selectedWitchItemName;

        if (string.IsNullOrEmpty(selectedClassName)) return;

        // 获取所有道具
        WitchItemBase[] allItems = GetComponentsInChildren<WitchItemBase>(true);
        
        foreach (var item in allItems)
        {
            bool isMatch = item.GetType().Name == selectedClassName;
            item.isActive = isMatch;
            item.enabled = isMatch;
            item.gameObject.SetActive(isMatch);
            
            if (isMatch)
            {
                activeItemInstance = item;
                
                // --- 【核心修改：初始化道具 UI】 ---
                var data = itemDatabase.Find(d => d.scriptClassName == selectedClassName);
                if (data != null && SceneScript.Instance != null && SceneScript.Instance.itemSlot != null)
                {
                    // 设置图标和按键文字 "F"
                    SceneScript.Instance.itemSlot.Setup(data.icon, "F");
                    SceneScript.Instance.itemSlot.gameObject.SetActive(true);
                }

                // 更新同步和逻辑
                witch.currentItemIndex = System.Array.IndexOf(witch.witchItems, item.gameObject);
                witch.CmdChangeItem(witch.currentItemIndex);
            }
        }
    }

    private void Update()
    {
        // 只有本地玩家且有激活道具时更新 UI 遮罩
        if (!isLocalPlayer || activeItemInstance == null) return;

        if (SceneScript.Instance != null && SceneScript.Instance.itemSlot != null)
        {
            SceneScript.Instance.itemSlot.UpdateCooldown(activeItemInstance.CooldownRatio);
        }
    }
}
```

## Player\PlayerScript.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using TMPro;
using System;
using UnityEngine.SceneManagement;
using System.Diagnostics; // 必须引用


public class PlayerScript : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnPingChanged))] 
    public int ping = 0; // 【新增】同步 Ping 值
    private LobbyScript lobbyScript;//大厅脚本引用
    // 不再需要手动设置 isInGame，改为只读属性或自动判断
    public bool IsInGameScene => SceneManager.GetActiveScene().name == "MyScene"; // 假设你的游戏场景叫 GameScene
    // 状态标志
    [SyncVar(hook = nameof(OnReadyChanged))] public bool isReady = false;
    // public int isInLobby=0;//是否在大厅标志位  
    // public int isHostPlayer=0;//是否是主机玩家标志位
    public TextMeshPro nameText;//名字文本
    // public GameObject floatingInfo;//悬浮信息
    private  Material playerMaterialClone;//玩家材质克隆体

    [SyncVar(hook = nameof(OnPlayerNameChanged))]
    public string playerName = "Unknown"; // 给个默认值

    
    [SyncVar(hook = nameof(OnPlayerColorChanged))]
    private Color playerColor;//玩家颜色

    [SyncVar(hook = nameof(OnRoleChanged))]
    public PlayerRole role; // 角色类型
    [SyncVar] public Gender myGender = Gender.Male;
    [SyncVar] public string selectedWitchItemName;
    private void OnPingChanged(int oldPing, int newPing)
    {
        // 当延迟变化时，刷新 UI 行
        if (lobbyScript != null) lobbyScript.UpdatePlayerRow(this);
    }

    //玩家名字同步变量
    private void OnPlayerNameChanged(string oldName, string newName)
    {
        if(nameText != null) nameText.text = newName; 
        
        // 【保险修复】如果此时 lobbyScript 为空（例如远程玩家刚生成），尝试找一下
        if (lobbyScript == null && !IsInGameScene) 
        {
            lobbyScript = FindObjectOfType<LobbyScript>();
        }

        // 刷新大厅列表的显示
        if (lobbyScript != null) 
        {
            lobbyScript.UpdatePlayerRow(this);
        }
        // 【新增】本地玩家持久化新名
        if (isLocalPlayer && PlayerSettings.Instance != null)
        {
            PlayerSettings.Instance.PlayerName = newName;
        }

    }
    //玩家颜色同步变量
    private void OnPlayerColorChanged(Color oldColor, Color newColor)
    {
        if(nameText != null) nameText.color = newColor;
        if(GetComponent<Renderer>() != null)
        {
            playerMaterialClone = new Material(GetComponent<Renderer>().material);
            playerMaterialClone.color = newColor;
            GetComponent<Renderer>().material = playerMaterialClone;
        }
    }
    // 角色变化钩子
    private void OnRoleChanged(PlayerRole oldRole, PlayerRole newRole)
    {
        // 可在此更新UI、模型等
        UnityEngine.Debug.Log($"Player {playerName} role changed to {newRole}");
    }

    override public void OnStartLocalPlayer()
    {
        base.OnStartClient();
        // 如果在大厅，尝试查找大厅脚本 (OnStartClient可能没找到)
        if (!IsInGameScene && lobbyScript == null)
        {
            lobbyScript = FindObjectOfType<LobbyScript>();
        }        
        // ──────────────── 關鍵修改 ────────────────
        // 從 PlayerSettings 讀取名字，而不是隨機產生
        string finalName = "Player";
        Color finalColor = new Color(
            UnityEngine.Random.Range(0f, 1f),
            UnityEngine.Random.Range(0f, 1f),
            UnityEngine.Random.Range(0f, 1f),
            1f
        );

        if (PlayerSettings.Instance != null && !string.IsNullOrWhiteSpace(PlayerSettings.Instance.PlayerName))
        {
            finalName = PlayerSettings.Instance.PlayerName;
        }

        // 送給伺服器
        CmdSetupPlayer(finalName, finalColor);

        // 可选：连线成功后清空（避免下次重连还带旧名字）
        if (PlayerSettings.Instance != null)
        {
            PlayerSettings.Instance.Clear();
        }
        // 【新增】开始定期更新 Ping
        StartCoroutine(UpdatePingRoutine());
        CmdUpdateGender(PlayerSettings.Instance.selectedGender);
        // 【新增】将本地选择的道具名同步给服务器
        if (PlayerSettings.Instance != null)
        {
            CmdUpdateSelectedItem(PlayerSettings.Instance.selectedWitchItemName);
        }
    }
    [Command]
    public void CmdUpdateSelectedItem(string itemName)
    {
        selectedWitchItemName = itemName;
    }
    // 【新增】协程：每 2 秒更新一次延迟（不需要太频繁，节省带宽）
    private IEnumerator UpdatePingRoutine()
    {
        while (true)
        {
            if (isLocalPlayer && NetworkClient.active)
            {
                // NetworkTime.rtt 是往返时延（秒），乘以 1000 得到毫秒
                int currentPing = (int)(NetworkTime.rtt * 1000);
                CmdUpdatePing(currentPing);
            }
            yield return new WaitForSeconds(2f);
        }
    }

    [Command]
    private void CmdUpdatePing(int newPing)
    {
        ping = newPing;
    }

    [Command]//客户端给服务器发送命令
    private void CmdSetupPlayer(string name, Color color)//设置玩家信息命令
    { 
        playerName = name;
        playerColor = color;
    }

    private void ChangePlayerNameAndColor()//更改玩家名字和颜色
    {
        var tempName = $"Player{UnityEngine.Random.Range(1, 999)}";
        var tempColor = new Color(
            UnityEngine.Random.Range(0f, 1f),
            UnityEngine.Random.Range(0f, 1f),
            UnityEngine.Random.Range(0f, 1f),
            1f
        );
        CmdSetupPlayer(tempName, tempColor);
    }   


    // 客戶端本地呼叫這個來切換準備狀態
    [Command]
    public void CmdSetReady(bool ready)
    {
        isReady = ready;
    }
    [Command]
    public void CmdStartGame()
    {
        // 1. 服务器端校验：再次统计一遍是否所有人都 Ready 了
        // 防止某个客户端通过作弊手段在没准备好时发送了 Start 命令
        
        int total = 0;
        int ready = 0;

        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn != null && conn.identity != null)
            {
                var player = conn.identity.GetComponent<PlayerScript>();
                if (player != null)
                {
                    total++;
                    if (player.isReady) ready++;
                }
            }
        }

        // 2. 只有校验通过才开始倒计时
        if (total > 0 && total == ready)
        {
            // 【修改】不再直接切换场景，而是调用 LobbyScript 的倒计时
            LobbyScript lobby = FindObjectOfType<LobbyScript>();
            if (lobby != null)
            {
                lobby.StartGameCountdown();
            }
            else
            {
                UnityEngine.Debug.LogError("LobbyScript not found on Server!");
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("Not all players are ready!");
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
    }
    // 1. 当这个玩家对象在客户端被创建时（无论是自己还是别人）
    public override void OnStartClient()
    {
        base.OnStartClient();

        // 尝试找大厅脚本 (只在 Lobby 场景有效)
        lobbyScript = FindObjectOfType<LobbyScript>();
        
        if (lobbyScript != null)
        {
            // 告诉大厅：我来了，给我加一行
            lobbyScript.AddPlayerRow(this);
        }
        // 如果在游戏场景，获取已添加的角色组件
        // if (IsInGameScene)
        // {
        //     playerBase = GetComponent<PlayerBase>();
        // }
    }
    // 2. 当这个玩家对象在客户端被销毁时（断线或离开）
    public override void OnStopClient()
    {
        // 1. 清理大厅 UI
        if (lobbyScript != null)
        {
            lobbyScript.RemovePlayerRow(this);
        }
        
        // 2. 执行基类逻辑
        base.OnStopClient();
        
        // 【关键】删除下面所有 NetworkManager.singleton.Stop... 的代码
        // 这里是清理现场的地方，不是发号施令的地方
    }
    private void OnReadyChanged(bool oldReady, bool newReady)
    {
        if (lobbyScript == null && !IsInGameScene) lobbyScript = FindObjectOfType<LobbyScript>();
        
        if (lobbyScript != null)
        {
            // 这行代码会调用 rowScript.UpdateInfo
            // 在 UpdateInfo 里，我们已经写了 if(isLocalPlayer) 更新按钮文字的逻辑
            lobbyScript.UpdatePlayerRow(this); 
            
            // lobbyScript.UpdateMyReadyStatus(newReady); // <--- 删除这行
        }
    }

    // =========================================================
    // 【新增】 聊天系统逻辑
    // =========================================================

    [Command]
    public void CmdSendChatMessage(string message)
    {
        // 1. (可选) 服务器端验证：防止垃圾信息、长度限制等
        if (string.IsNullOrWhiteSpace(message)) return;
        if (message.Length > 100) message = message.Substring(0, 100);

        // 2. 广播给所有客户端
        RpcReceiveChatMessage(playerName, message, playerColor);
    }

    [ClientRpc]
    public void RpcReceiveChatMessage(string senderName, string message, Color color)
    {
        // 3. 在客户端找到聊天 UI 并显示
        // 因为 UI 是本地场景的一部分，用 FindObjectOfType 找
        LobbyChat chatUI = FindObjectOfType<LobbyChat>();
        
        if (chatUI != null)
        {
            chatUI.AppendMessage(senderName, message, color);
        }
    }

    [Command]
    public void CmdChangePlayerName(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;

        newName = newName.Trim();
        if (newName.Length > 16) newName = newName.Substring(0, 16);
        if (newName.Length == 0) newName = "Player";

        playerName = newName;  // 因為是 SyncVar，會自動同步 + 觸發 hook
        UnityEngine.Debug.Log($"[Server] Player {connectionToClient.connectionId} changed name to: {newName}");
    }
    [Command]
    public void CmdUpdateLobbySettings(int type, float floatVal, bool boolVal, int intVal)
    {
        LobbyScript lobby = FindObjectOfType<LobbyScript>();
        if (lobby == null) return;

        // 根据类型修改 LobbyScript 上的同步变量
        switch (type)
        {
            case 0:
                lobby.syncedGameTimer = floatVal;
                break;
            case 1:
                lobby.syncedAnimalsNumber = (int)intVal;
                break;
            case 2:
                lobby.syncedFriendlyFire = boolVal;
                break;
            case 3:
                lobby.syncedWitchHP = floatVal;
                break;
            case 4:
                lobby.syncedWitchMana = floatVal;
                break;
            case 5:
                lobby.syncedHunterSpeed = floatVal;
                break;
            case 6:
                lobby.syncedTrapDifficulty = (int)intVal;
                break;
            case 7:
                lobby.syncedManaRegen = floatVal;
                break;
            case 8:
                lobby.syncedHunterRatio = floatVal;
                break;
            case 9:
                lobby.syncedAncientRatio = floatVal;
                break;
            default:
                UnityEngine.Debug.LogWarning($"Unknown lobby setting type: {type}");
                break;
        }
    }
    [Command]
    public void CmdCancelStart()
    {
        // 在服务器上寻找 LobbyScript 并执行取消逻辑
        LobbyScript lobby = FindObjectOfType<LobbyScript>();
        if (lobby != null)
        {
            lobby.CancelCountdown();
        }
    }
    [Command]
    public void CmdUpdateGender(Gender g)
    {
        myGender = g;
    }
}

```

## Player\PlayerSettings.cs

```csharp
using UnityEngine;

using System.Collections.Generic;

public enum Gender { Male, Female }

public class PlayerSettings : MonoBehaviour
{
    public static PlayerSettings Instance { get; private set; }
    public string PlayerName { get; set; } = "";

    public Gender selectedGender = Gender.Male; // 默认男性
    // 存储选中的技能名称（或者 ID）
    public List<string> selectedWitchSkillNames = new List<string>();
    public List<string> selectedHunterSkillNames = new List<string>();
    public string selectedWitchItemName = ""; // 存储选中的道具类名

    private void Awake() {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (selectedWitchSkillNames.Count < 2) {
            selectedWitchSkillNames.Clear();
            selectedWitchSkillNames.Add("WitchSkill_Mist"); // 默认值
            selectedWitchSkillNames.Add("WitchSkill_Decoy");
        }
        if (selectedHunterSkillNames.Count < 2) {
            selectedHunterSkillNames.Clear();
            selectedHunterSkillNames.Add("HunterSkill_Trap");
            selectedHunterSkillNames.Add("HunterSkill_Scan");
        }
        // 核心修改：在 Awake 阶段就锁定默认值，不要等 UI 脚本初始化
        if (string.IsNullOrEmpty(selectedWitchItemName)) {
            selectedWitchItemName = "InvisibilityCloak"; // 或者你想要的默认类名
        }
        DontDestroyOnLoad(gameObject);
    }

    // 可選：提供清除方法（斷線重連時用）
    public void Clear()
    {
        PlayerName = "Player";
    }
    // 供 UI 调用
    public void SetGender(int index)
    {
        selectedGender = (Gender)index;
    }
}
```

## Player\Weapon.cs

```csharp
using UnityEngine;
using Mirror;

public abstract class WeaponBase : NetworkBehaviour
{
    [Header("通用设置")]
    public string weaponName;
    public float damage = 20f;       // 伤害（兜网可能没伤害，但有禁锢效果）
    public float fireRate = 1.0f;   // 射击间隔
    public Transform firePoint;     // 枪口位置（子弹/射线发出的地方）
    public ParticleSystem muzzleFlash; // 枪口火光特效
    public AudioClip fireSound;    // 开火声音

    // 内部冷却计时
    public float nextFireTime = 0f;

    // 返回冷却进度（0~1）
    public float CooldownRatio
    {
        get
        {
            float timeLeft = nextFireTime - Time.time;
            if (timeLeft <= 0) return 0f;
            return Mathf.Clamp01(timeLeft / fireRate);
        }
    }

    // 判断是否冷却完毕
    public bool CanFire()
    {
        return Time.time >= nextFireTime;
    }

    // ★ 抽象方法：具体开火逻辑交给子类实现
    // origin: 射击起点（通常是摄像机位置）
    // direction: 射击方向（通常是摄像机正前方）
    public void UpdateCooldown()
    {
        nextFireTime = Time.time + fireRate;
    }
    public abstract void OnFire(Vector3 origin, Vector3 direction);


}

```

## Player\WitchItemBase.cs

```csharp
using UnityEngine;
using Mirror;

public abstract class WitchItemBase : NetworkBehaviour
{
    [Header("道具通用设置")]
    public bool isActive;
    public string itemName;
    public float cooldown = 0f; // 冷却时间 (对于被动道具可能为0或无限)
    // 内部冷却计时
    public float nextUseTime = 0f;
    // 判断是否冷却完毕
    public bool CanUse()
    {
        return Time.time >= nextUseTime;
    }

    // ★ 抽象方法：具体开火逻辑交给子类实现
    // origin: 射击起点（通常是摄像机位置）
    // direction: 射击方向（通常是摄像机正前方）
    public void UpdateCooldown()
    {
        nextUseTime = Time.time + cooldown;
    }
    // 【新增】获取冷却比例 (1为刚开始冷却，0为就绪)
    public float CooldownRatio
    {
        get
        {
            float timeLeft = nextUseTime - Time.time;
            if (timeLeft <= 0 || cooldown <= 0) return 0f;
            return Mathf.Clamp01(timeLeft / cooldown);
        }
    }

    // 道具激活入口 (主动道具用)
    public virtual void OnActivate() { }

    // 道具被动更新 (每帧调用)
    public virtual void OnPassiveUpdate(WitchPlayer witch) { }

}
```

## Player\WitchItemData.cs

```csharp
using UnityEngine;

[CreateAssetMenu(fileName = "New Witch Item", menuName = "Game/Witch Item Data")]
public class WitchItemData : ScriptableObject
{
    public string itemName;       // UI显示的道具名
    public string scriptClassName; // 对应的类名 (如 "InvisibilityCloak")
    public Sprite icon;           // 道具图片
    [TextArea] public string description; // 道具描述
}
```

## Player\WitchPlayer.cs

```csharp
using UnityEngine;
using Mirror;
using System.Diagnostics;
using Controller; // 确保引用了动物控制器的命名空间
using System.Collections.Generic; // 引用 List
using System.Collections;

public class WitchPlayer : GamePlayer
{
    [Header("Status Effects")]
    // 【新增】同步隐身状态，带 Hook
    [SyncVar(hook = nameof(OnStealthChanged))]
    public bool isStealthed = false;
    //生命护符保护状态
    [SyncVar(hook = nameof(OnAmuletProtectionChanged))]
    public bool isProtectedByAmulet = false; // 是否处于30秒保护期
    public float amuletSpeedMultiplier = 1.5f; // 护符加速倍率
    // 【新增】二段跳标记
    private bool doubleJumpUsed = false;

    [Header("Witch Skill Settings")]
    public GameObject[] witchItems;// 女巫道具数组
    [SyncVar(hook = nameof(OnItemChanged))]
    public int currentItemIndex = 0;
    public float interactionDistance = 5f;
    public LayerMask propLayer;
    public float revertLongPressTime = 1.5f; // 长按多久恢复原状

    private PropTarget currentFocusProp; // 当前聚焦的道具物体
    private MeshFilter myMeshFilter;
    private Renderer myRenderer;
    public GameObject HideGroup;//隐藏物体组
    private MeshCollider myMeshCollider;//玩家身上的网格碰撞器

    // --- 还原备份数据 ---
    private Mesh originalMesh;
    private Material[] originalMaterials;
    private Vector3 originalScale;
    private float originalCCHeight;
    private float originalCCRadius;
    private Vector3 originalCCCenter;
    private float lmbHoldTimer = 0f; // 左键按住计时器

    [Header("Morph Animation Settings")]
    public Transform propContainer; // 玩家预制体下的一个空物体，用于装载变身后的模型
    private GameObject currentVisualProp; // 当前生成的动物模型实例
    private Animator propAnimator; // 变身后获取的动画组件引用
    private string currentVerticalParam = "Speed"; // 默认值
    private string currentStateParam = "State";
    [Header("Morphed Stats")]
    private float morphedWalkSpeed = 5f;
    private float morphedRunSpeed = 8f;
    private float originalHumanSpeed = 5f; // 备份人类速度
    private bool isMorphedIntoAnimal = false; // 记录当前变身的是否为有动画的动物
    private Vector3 lastPosition;
    [Header("复活赛设置")]
    public int frogPropID = 1; // 假设 PropDatabase 中 ID 1 是青蛙
    public float frogHealth = 20f; // 小动物形态血量
    private float scoutTimer = 0f;
    public const float SCOUT_TIME_THRESHOLD = 0.5f;

    // ========================================================================
    // 【新增】多人共乘（抢方向盘）核心变量
    // ========================================================================
    [Header("Multi-Witch Control")]
    // 自身携带的 PropTarget 组件，用于变身后让别人瞄准
    private PropTarget myPropTarget;

    // 当前我是谁的乘客？(0 表示自己是独立的)
    [SyncVar(hook = nameof(OnHostNetIdChanged))]
    public uint hostNetId = 0;

    // 只有宿主才用这个列表：记录谁在我的车上
    public readonly SyncList<uint> passengerNetIds = new SyncList<uint>();

    // 宿主用来同步所有乘客的总输入向量 (X, Z)
    [SyncVar]
    private Vector2 combinedPassengerInput;

    [Header("Possession Settings")]
    public float possessLongPressTime = 1.0f; // 右键长按多久附身
    private float rmbHoldTimer = 0f;

    [SyncVar]
    public uint possessedTreeNetId = 0; // 记录当前附身的树的 NetId
    [Header("Delivery Progress")]
    [SyncVar(hook = nameof(OnDeliveryStatusChanged))]
    public bool hasDeliveredTree = false; // 是否已经作为驾驶员带回过古树
    [Header("新层级引用")]
    public GameObject humanModelGroup; // 将 tripo_node 和 Armature 所在的父物体拖到这里
    private BoxCollider humanBoxCollider; // 人形时的 BoxCollider

    [Header("Camera Smoothing")]
    private Vector3 targetCamPos = new Vector3(0, 1.055f, 0.278f); 
    private bool isCamInitialized = false; // 用于初始化第一帧位置
    [Header("Morph Cooldown")]
    public float morphCooldown = 1.0f; // 1秒冷却
    private float nextMorphTime = 0f;  // 下一次允许变身的时间
    [Header("Reward Settings")]
    public int treesPerReward = 20; // 每检视20棵树获得一次奖励
    [SyncVar] public int pendingRewards = 0; // 待领取的奖励次数
    [SyncVar] public int scoutedCount = 0; 
    // 增加一个列表，专门让服务器记住发给客户端的是哪三个奖励
    private List<RewardOption> serverRewardPool = new List<RewardOption>();
    // ========================================================================
    [SerializeField] private Animator animator; // 在Inspector中拖入你的Animator

    // 计算当前的冷却百分比 (1为刚开始冷却，0为就绪)
    public float MorphCooldownRatio
    {
        get
        {
            float timeLeft = nextMorphTime - Time.time;
            if (timeLeft <= 0) return 0f;
            return Mathf.Clamp01(timeLeft / morphCooldown);
        }
    }
    private void Awake()
    {
        goalText = "Get Your Own Tree And Assemble at the Gates!";
        myMeshFilter = GetComponentInChildren<MeshFilter>();
        myRenderer = GetComponentInChildren<Renderer>();

        // 1. 备份初始人类数据
        if (myMeshFilter != null) originalMesh = myMeshFilter.sharedMesh;
        if (myRenderer != null)
        {
            originalMaterials = myRenderer.sharedMaterials;
            originalScale = myRenderer.transform.localScale;
        }

        // 【修改点 2】确保玩家根物体(Parent)上有一个 MeshCollider 用于变身
        myMeshCollider = GetComponent<MeshCollider>();
        if (myMeshCollider == null)
        {
            myMeshCollider = gameObject.AddComponent<MeshCollider>();
        }
        myMeshCollider.convex = true; // 动态物体必须开启 convex
        myMeshCollider.enabled = false; // 默认禁用，变身才开

        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            originalCCHeight = cc.height;
            originalCCRadius = cc.radius;
            originalCCCenter = cc.center;
        }
        // 【新增】给玩家挂载 PropTarget，但默认禁用
        myPropTarget = GetComponent<PropTarget>();
        if (myPropTarget == null) myPropTarget = gameObject.AddComponent<PropTarget>();
        myPropTarget.enabled = false; // 还没变身，不可被当做道具

        // 如果没有手动指定 HideGroup，默认尝试找子物体
        if (humanModelGroup == null)
        {
            // 假设 tripo_node 是第一个子物体
            humanModelGroup = transform.Find("tripo_node")?.gameObject;
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        lastPosition = transform.position;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // moveSpeed = 5f;
        // mouseSensitivity = 2f;
        // manaRegenRate = 5f;
    }

    public override void Update()
    {
        // 1. 如果永久死亡或游戏结束，不执行任何交互按键逻辑
        if (isPermanentDead || (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver))
        {
            // 如果是本地玩家，确保清理掉可能存在的进度条 UI
            if (isLocalPlayer && sceneScript != null) sceneScript.UpdateRevertUI(0, false);
            
            // 仍然允许执行基类的 Update 以保持重力/位置同步（如果没切换相机的话）
            // 但根据你的 RpcNotifyVictorySequence，主相机会断开父子关系，所以这里直接返回即可
            if (GameManager.Instance != null && GameManager.Instance.CurrentState != GameManager.GameState.GameOver)
                base.Update();
            return;
        }
        // =========================================================
        // 【新增】乘客逻辑：如果我是乘客，我不需要跑物理移动
        // =========================================================
        if (isLocalPlayer && hostNetId != 0)
        {
            HandlePassengerLogic();
            HandleMorphInput();     // 【新增】处理长按左键下车 (复用变身输入的进度条逻辑)
            return; // 乘客不执行后续的 base.Update() (不跑物理移动)
        }
        // =========================================================
        // 【新增】宿主逻辑：更新变身后的 PropTarget 可视状态
        // =========================================================
        if (isMorphed && myPropTarget != null && currentVisualProp != null)
        {
            // 修改前：if (myPropTarget.targetRenderer == null)
            // 修改后：使用我们刚才在 PropTarget 里加的属性
            if (!myPropTarget.IsInitialized)
            {
                myPropTarget.ManualInit(morphedPropID, currentVisualProp);
            }
        }
        // =========================================================
        // 如果变身了，根据按键实时更新基础移动速度
        if (isLocalPlayer && isMorphed)
        {
            bool isRunning = Input.GetKey(KeyCode.LeftShift);
            float targetSpeed = isRunning ? morphedRunSpeed : morphedWalkSpeed;

            // 只有当速度发生变化时才发送命令，节省带宽
            if (Mathf.Abs(moveSpeed - targetSpeed) > 0.01f)
            {
                moveSpeed = targetSpeed; // 本地先变，保证手感
                CmdUpdateMoveSpeed(targetSpeed); // 通知服务器变
            }
        }

        base.Update();

        if (isLocalPlayer) // 只有自己计算速度
        {
            float horizontalSpeed = new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
            // 把速度传给服务器进行同步
            CmdUpdateAnimationSpeed(horizontalSpeed);
        }

        // 所有人（包括本地和远程客户端）都根据同步的速度值更新动画
        // 修改这段逻辑
        if (!isMorphed && animator != null)
        {
            // 增加参数检查，防止报错
            if (HasParameter(animator, "speed"))
            {
                animator.SetFloat("speed", syncedSpeed, 0.1f, Time.deltaTime);
            }
        }
        // --- 新增：本地玩家更新 UI 冷却进度 ---
        if (isLocalPlayer && SceneScript.Instance != null && SceneScript.Instance.morphSlot != null)
        {
            SceneScript.Instance.morphSlot.UpdateCooldown(MorphCooldownRatio);
        }

        // ----------------------------------------------------------------
        // 【核心修复】计算速度并同步动画参数
        // ----------------------------------------------------------------
        if (isMorphed && isMorphedIntoAnimal && propAnimator != null)
        {
            float speedMagnitude;

            if (isLocalPlayer)
            {
                speedMagnitude = new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
            }
            else
            {
                // 远程玩家：使用位置差推算
                float distance = Vector3.Distance(transform.position, lastPosition);
                speedMagnitude = distance / Time.deltaTime;
                // 只有当距离变化超过一个小阈值才认为在移动，防止抖动
                if (distance < 0.001f) speedMagnitude = 0;
            }

            lastPosition = transform.position;

            // 只要有位移，Vert 就给 1
            float animVert = speedMagnitude > 0.05f ? 1.0f : 0.0f;
            propAnimator.SetFloat(currentVerticalParam, animVert);

            // 通过 moveSpeed (SyncVar) 判断远程玩家是否在按 Shift
            bool isRunning = (moveSpeed >= morphedRunSpeed - 0.1f) && speedMagnitude > 0.1f;
            propAnimator.SetFloat(currentStateParam, isRunning ? 1f : 0f);
        }

        if (!isLocalPlayer) return;

        // 如果正在聊天或暂停，不处理交互
        if (isChatting || Cursor.lockState != CursorLockMode.Locked) return;

        HandleInteraction(); // 只有非乘客才进行射线检测
        HandleMorphInput();  // 处理变身/还原输入
        HandleItemActivation(); // 处理道具使用输入

        // --- 在 Update 的最后添加平滑移动逻辑 ---
        // 【核心修复】：增加 GameOver 判断，防止插值逻辑把相机拉回玩家身边
        if (isLocalPlayer && Camera.main != null && GameManager.Instance.CurrentState != GameManager.GameState.GameOver)
        {
            Camera.main.transform.localPosition = Vector3.Lerp(
                Camera.main.transform.localPosition, 
                targetCamPos, 
                Time.deltaTime * 5f
            );
        }
    }
    // 建议在类中添加一个辅助方法
    private bool HasParameter(Animator anim, string paramName)
    {
        if (anim == null) return false;
        foreach (AnimatorControllerParameter param in anim.parameters)
        {
            if (param.name == paramName) return true;
        }
        return false;
    }
    // =========================================================
    // 【修改】重写 HandleMovementOverride 实现“抢方向盘”
    // =========================================================
    protected override void HandleMovementOverride(Vector2 inputOverride)
    {
        // 1. 获取本地输入 (来自 GamePlayer 传进来的参数)
        Vector2 finalInput = inputOverride;

        // 2. 如果是宿主，叠加乘客输入
        if (passengerNetIds.Count > 0)
        {
            finalInput += combinedPassengerInput;
            // 限制最大合力，防止速度过快
            finalInput = Vector2.ClampMagnitude(finalInput, 1.2f);
        }
        // 先检查是否着地，如果着地则重置二段跳
        if (controller.isGrounded)
        {
            doubleJumpUsed = false;
        }
        float rayLength = (controller.height * 0.5f) + 0.3f;
        Vector3 rayOrigin = transform.position + Vector3.up * 0.1f;
        bool isLikelyOnGround = Physics.Raycast(rayOrigin, Vector3.down, rayLength, groundLayer);

        if (!controller.isGrounded && !isLikelyOnGround && Input.GetButtonDown("Jump") && !doubleJumpUsed && !isStunned && !isPermanentDead)
        {
            MagicBroom broom = null;
            if (currentItemIndex == 1)
            {
                broom = witchItems[1].GetComponent<MagicBroom>();
            }
            // 检查道具、形态和冷却
            if (broom != null && !isMorphed && broom.CanUse())
            {
                // 计算二段跳向上的速度
                float jumpVel = Mathf.Sqrt(jumpHeight * broom.doubleJumpForceMultiplier * -2f * gravity);

                // 直接覆盖 Y 轴速度
                velocity.y = jumpVel;

                // 标记状态并进入冷却
                doubleJumpUsed = true;
                broom.UpdateCooldown();

                UnityEngine.Debug.Log($"<color=cyan>Double Jump Triggered! Velocity Y set to: {velocity.y}</color>");
            }
            else if (broom != null && !broom.CanUse())
            {
                // 冷却中
                UnityEngine.Debug.Log("Broom Cooldown...");
            }
        }
        // 调用基类，传入修改后的 Input
        base.HandleMovementOverride(finalInput);
    }

    // =========================================================
    // 【新增】乘客逻辑
    // =========================================================
    private void HandlePassengerLogic()
    {
        // 1. 发送输入给宿主
        if (!isChatting && Cursor.lockState == CursorLockMode.Locked)
        {
            float x = Input.GetAxis("Horizontal");
            float z = Input.GetAxis("Vertical");
            if (Mathf.Abs(x) > 0.01f || Mathf.Abs(z) > 0.01f)
            {
                CmdSendInputToHost(new Vector2(x, z));
            }
            else
            {
                CmdSendInputToHost(Vector2.zero);
            }
        }

        // 2. 视角跟随宿主
        if (NetworkClient.spawned.TryGetValue(hostNetId, out NetworkIdentity hostIdentity))
        {
            // 强制将我的位置设置在宿主位置（防止网络剔除问题）
            transform.position = hostIdentity.transform.position;

            // 相机跟随
            Camera.main.transform.SetParent(null); // 解除父子关系防止跟随旋转晕车
            // 简单的第三人称跟随
            Vector3 targetPos = hostIdentity.transform.position + Vector3.up * 2f - hostIdentity.transform.forward * 4f;
            Camera.main.transform.position = Vector3.Lerp(Camera.main.transform.position, targetPos, Time.deltaTime * 10f);
            Camera.main.transform.LookAt(hostIdentity.transform.position + Vector3.up * 1f);
        }

        // // 3. 处理退出 (空格键跳车)
        // if (Input.GetKeyDown(KeyCode.Space))
        // {
        //     CmdLeaveHost();
        // }
    }

    public override void HandleInput()
    {

    }
    private void HandleItemActivation()
    {
        if (isLocalPlayer && !isPermanentDead)
        {
            //使用道具
            // --- 【保留】 使用道具的逻辑 ---
            if (Input.GetKeyDown(KeyCode.F))
            {
                // 确保索引在范围内
                if (currentItemIndex >= 0 && currentItemIndex < witchItems.Length)
                {
                    WitchItemBase currentItem = witchItems[currentItemIndex].GetComponent<WitchItemBase>();
                    if (currentItem != null && currentItem.CanUse() && currentItem.isActive)
                    {
                        currentItem.UpdateCooldown();
                        UnityEngine.Debug.Log($"Activating item: {currentItem.itemName}");
                        currentItem.OnActivate();
                    }
                }
            }

        }
    }
    public void ChangeItem(int itemIndex)
    {
        CmdChangeItem(itemIndex);
        if (sceneScript == null) return;

        string itemName = "None";
        if (itemIndex >= 0 && itemIndex < witchItems.Length)
        {
            WitchItemBase itemBase = witchItems[itemIndex].GetComponent<WitchItemBase>();
            if (itemBase != null)
            {
                itemName = itemBase.itemName;
            }
        }
        sceneScript.WeaponText.text = itemName;
    }
    // 处理射线检测和高亮
    private void HandleInteraction()
    {
        Ray ray;
        if (sceneScript != null && sceneScript.Crosshair != null)
        {
            ray = Camera.main.ScreenPointToRay(sceneScript.Crosshair.transform.position);
        }
        else
        {
            ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        }

        RaycastHit hit;
        PropTarget hitProp = null;
        UnityEngine.Debug.DrawRay(ray.origin, ray.direction * interactionDistance, Color.green);
        // 1. 射线检测
        if (Physics.Raycast(ray, out hit, interactionDistance, propLayer))
        {
            // 只有打中带 PropTarget 的物体才算有效
            hitProp = hit.collider.GetComponentInParent<PropTarget>();
        }

        // --- 新增：侦察计时逻辑 ---
        if (hitProp != null && (hitProp.isStaticTree || hitProp.isAncientTree))
        {
            if (hitProp == currentFocusProp)
            {
                // 如果这棵树还没被标记为已发现，就开始计时
                if (!hitProp.isScouted)
                {
                    scoutTimer += Time.deltaTime;
                    if (scoutTimer >= SCOUT_TIME_THRESHOLD)
                    {
                        CmdSetTreeScouted(hitProp.netId);
                        scoutTimer = 0f; // 触发后重置
                    }
                }       
            }
            else
            {
                scoutTimer = 0f;
            }
        }
        else
        {
            scoutTimer = 0f;
        }

        // 2. 状态切换逻辑
        if (hitProp != currentFocusProp)
        {
            // 取消旧物体的光效
            if (currentFocusProp != null)
            {
                currentFocusProp.SetHighlight(false);
            }

            // 赋值新物体
            currentFocusProp = hitProp;

            // 开启新物体的光效
            if (currentFocusProp != null)
            {
                currentFocusProp.SetHighlight(true);
            }
        }
    }

    // 处理变身输入
    private void HandleMorphInput()
    {
        if (isInSecondChance) return; // 复活赛期间锁死形态，不能通过长按左键恢复
        // --- 新增：检查冷却 ---
        bool isCoolingDown = Time.time < nextMorphTime;
        // 定义当前状态
        bool isPassenger = hostNetId != 0; // 是否是乘客
        bool isHost = isMorphed && !isPassenger; // 是否是宿主
        // --- 处理左键按下 ---
        if (Input.GetMouseButton(0))
        {
            // 如果在冷却中，直接跳过
            if (isCoolingDown) 
            {
                UnityEngine.Debug.Log("Morph is on cooldown...");
                lmbHoldTimer = 0f;
                return; 
            }
            lmbHoldTimer += Time.deltaTime;
            // 【修改】如果是 变身状态(Host) 或者 乘客状态(Passenger)，都显示进度条
            if (isHost || isPassenger)
            {
                float progress = Mathf.Clamp01(lmbHoldTimer / revertLongPressTime);
                if (progress > 0.1f)
                {
                    if (sceneScript != null)
                    {
                        // 显示并更新进度条
                        sceneScript.UpdateRevertUI(progress, true);
                    }

                    if (lmbHoldTimer >= revertLongPressTime)
                    {
                        UnityEngine.Debug.Log("Long press complete.");
                        lmbHoldTimer = 0f;

                        if (sceneScript != null) sceneScript.UpdateRevertUI(0, false);

                        // 【核心分支】
                        if (isPassenger)
                        {
                            // 乘客长按 -> 下车
                            CmdLeaveHost();
                        }
                        else if (isHost)
                        {
                            // 宿主长按 -> 变回人形
                            CmdRevert();
                        }
                        // 变身触发冷却
                        nextMorphTime = Time.time + morphCooldown;
                    }
                }

            }
        }

        // --- 处理左键松开 ---
        if (Input.GetMouseButtonUp(0))
        {
            // 只要松开手，立刻隐藏进度条
            if (sceneScript != null)
            {
                sceneScript.UpdateRevertUI(0, false);
            }

            // 短按逻辑：变身
            // 【注意】乘客不能触发短按变身，必须是非乘客 (!isPassenger)
            if (!isPassenger && lmbHoldTimer > 0.01f && lmbHoldTimer < 0.3f && !isMorphed && currentFocusProp != null)
            {
                // 如果在冷却中，直接跳过
                if (isCoolingDown) 
                {
                    UnityEngine.Debug.Log("Morph is on cooldown...");
                    lmbHoldTimer = 0f;
                    return; 
                }
                // 【修改】使用 GetComponentInParent，因为脚本在父物体上
                WitchPlayer otherWitch = currentFocusProp.GetComponentInParent<WitchPlayer>();
                if (otherWitch != null && otherWitch != this)
                {
                    // 加入它！
                    UnityEngine.Debug.Log($"Detected another witch: {otherWitch.playerName}, joining...");
                    CmdJoinWitch(otherWitch.netId);
                    // 只有成功操作才触发冷却
                    nextMorphTime = Time.time + morphCooldown;
                }
                else
                {
                    // 普通变身
                    CmdMorph(currentFocusProp.propID);

                    // 变身触发冷却
                    nextMorphTime = Time.time + morphCooldown;
                }
            }

            lmbHoldTimer = 0f;
        }
        // --- 【右键逻辑：新增附身检测】 ---
        if (!isPassenger) // 乘客不能主动附身其他东西
        {
            if (Input.GetMouseButton(1)) // 右键按住
            {
                // 只有指向古树时才处理
                if (currentFocusProp != null && currentFocusProp.isAncientTree)
                {
                    rmbHoldTimer += Time.deltaTime;
                    float progress = Mathf.Clamp01(rmbHoldTimer / possessLongPressTime);

                    if (sceneScript != null)
                        sceneScript.UpdateRevertUI(progress, true); // 复用进度条UI

                    if (rmbHoldTimer >= possessLongPressTime)
                    {
                        rmbHoldTimer = 0f;
                        if (sceneScript != null) sceneScript.UpdateRevertUI(0, false);

                        // 执行附身命令
                        CmdPossessAncientTree(currentFocusProp.netId);
                    }
                }
            }

            if (Input.GetMouseButtonUp(1))
            {
                rmbHoldTimer = 0f;
                if (sceneScript != null) sceneScript.UpdateRevertUI(0, false);
            }
        }
    }

    // ----------------------------------------------------
    // 网络同步：变身
    // ----------------------------------------------------

    [Command]
    private void CmdJoinWitch(uint targetNetId)
    {
        if (!NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity targetIdentity)) return;

        WitchPlayer targetWitch = targetIdentity.GetComponent<WitchPlayer>();
        if (targetWitch == null || !targetWitch.isMorphed) return; // 只能加入已变身的女巫

        // 1. 设置状态
        hostNetId = targetNetId;

        // 2. 通知宿主添加乘客
        targetWitch.ServerAddPassenger(netId);

        // 3. 隐藏我自己
        RpcSetVisible(false);
    }

    [Command]
    private void CmdLeaveHost()
    {
        if (hostNetId == 0) return;

        if (NetworkServer.spawned.TryGetValue(hostNetId, out NetworkIdentity hostIdentity))
        {
            WitchPlayer hostWitch = hostIdentity.GetComponent<WitchPlayer>();
            if (hostWitch != null)
            {
                hostWitch.ServerRemovePassenger(netId);
            }
        }

        hostNetId = 0;
        RpcSetVisible(true);

        // 3. 【关键】调用 TargetRpc，让客户端自己计算弹射位置
        // 这样可以确保位置突变平滑，且方向正确
        TargetForceLeave(connectionToClient);
    }

    [Command]
    private void CmdSendInputToHost(Vector2 input)
    {
        // 只有乘客才能发
        if (hostNetId == 0) return;

        // 找到宿主并更新
        if (NetworkServer.spawned.TryGetValue(hostNetId, out NetworkIdentity hostIdentity))
        {
            WitchPlayer hostWitch = hostIdentity.GetComponent<WitchPlayer>();
            if (hostWitch != null)
            {
                hostWitch.ServerUpdatePassengerInput(netId, input);
            }
        }
    }



    [Command]
    private void CmdMorph(int propID)
    {
        
        // // 1. 先在服务器修改同步变量
        isMorphed = true;
        // // 2. 广播 Rpc 处理视觉
        // RpcMorph(propID);
        morphedPropID = propID; // 修改 SyncVar，自动触发所有人的钩子
        // 【核心修复】服务器自己也要执行一遍逻辑，否则服务器物理世界里女巫没变
        ApplyMorph(propID);
    }

    private void ApplyMorph(int propID)
    {
        if (currentVisualProp != null) Destroy(currentVisualProp);
        if (humanModelGroup != null) humanModelGroup.SetActive(false);
        if (HideGroup != null) HideGroup.SetActive(false);
        if (humanBoxCollider != null) humanBoxCollider.enabled = false;

        // 3. 生成新物体
        if (PropDatabase.Instance.GetPropPrefab(propID, out GameObject prefab))
        {
            // 检查容器是否存在
            if (propContainer == null)
            {
                UnityEngine.Debug.LogError("Prop Container is null!");
                return;
            }

            currentVisualProp = Instantiate(prefab, propContainer);
            currentVisualProp.transform.localPosition = Vector3.zero;
            currentVisualProp.transform.localRotation = Quaternion.identity;

            // 【新增逻辑】获取动物原有的控制参数
            var animalMover = currentVisualProp.GetComponent<Controller.CreatureMover>();
            if (animalMover != null)
            {
                // 是动物：使用动物的速度设置
                isMorphedIntoAnimal = true;
                // 获取私有变量的值（如果变量是私有的，请去 CreatureMover.cs 将 m_WalkSpeed 改为 public）
                // 注意：CreatureMover 内部使用了 / 3.6f 转换，我们也需要同步转换以匹配数值
                morphedWalkSpeed = animalMover.m_WalkSpeed;
                morphedRunSpeed = animalMover.m_RunSpeed;
                // 获取动画参数名
                currentVerticalParam = animalMover.m_VerticalID; // 获取 "Vert"
                currentStateParam = animalMover.m_StateID;      // 获取 "State"
            }
            else
            {
                // 不是动物（如石头、树木）：将速度设为原始人类速度
                isMorphedIntoAnimal = false;
                morphedWalkSpeed = originalHumanSpeed;
                morphedRunSpeed = originalHumanSpeed; // 静态物体通常不提供跑步加成，设为一致
                // 如果不是动物（是石头等静态物体），重置回默认或空
                currentVerticalParam = "Speed";
            }

            // 4. 【核心修复】禁用脚本但保留动画
            // 遍历 Behaviour 能够同时覆盖 MonoBehaviour 和 Animator
            // 2. 找到 ApplyMorph 方法中大约 743 行的循环，修改为：
            // 将原来的 Behaviour[] 改为 Component[] 或者是更精准的逻辑
            Component[] allComps = currentVisualProp.GetComponentsInChildren<Component>();
            foreach (var comp in allComps)
            {
                // 如果是脚本(MonoBehaviour) 且不是 Animator，则禁用
                // 注意：Renderer 根本没有 .enabled 属性在 Behaviour 级别，它在 Renderer 级别
                if (comp is MonoBehaviour script && !(comp is Animator))
                {
                    script.enabled = false;
                }
            }

            // 禁用所有物理碰撞器，防止动物自身的碰撞器干扰玩家
            Collider[] allColliders = currentVisualProp.GetComponentsInChildren<Collider>();
            foreach (var c in allColliders) c.enabled = false;

            // 5. 获取并设置 Animator
            propAnimator = currentVisualProp.GetComponent<Animator>();

            // 6. 更新玩家自身的 CharacterController 大小
            // 尝试从新模型中找一个渲染器来计算大小
            Mesh targetMesh = null;

            // 优先找 MeshCollider (因为有些物品可能 MeshFilter 是空的或者为了碰撞做了简化 Mesh)
            MeshCollider propMC = currentVisualProp.GetComponentInChildren<MeshCollider>();
            if (propMC != null) targetMesh = propMC.sharedMesh;

            // 找不到 MeshCollider 再找 MeshFilter
            if (targetMesh == null)
            {
                MeshFilter mf = currentVisualProp.GetComponentInChildren<MeshFilter>();
                if (mf != null) targetMesh = mf.sharedMesh;
            }

            // 还是找不到，试试 SkinnedMeshRenderer (针对动物)
            if (targetMesh == null)
            {
                SkinnedMeshRenderer smr = currentVisualProp.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr != null) targetMesh = smr.sharedMesh;
            }

            if (targetMesh != null)
            {
                // 【核心修复】解决 Mesh 不可读导致的报错
                if (myMeshCollider != null)
                {
                    myMeshCollider.enabled = false;

                    // 检查网格是否允许读写
                    if (targetMesh.isReadable)
                    {
                        myMeshCollider.sharedMesh = targetMesh;
                        myMeshCollider.convex = true; // 必须是凸包才能动
                        myMeshCollider.isTrigger = false;
                        myMeshCollider.enabled = true; // 启用父物体上的 MeshCollider

                        UnityEngine.Debug.Log($"[Physics] Copied MeshCollider from {currentVisualProp.name} to Player Root.");

                        // 根据 Mesh 大小调整 CharacterController (保留你原有的辅助逻辑)
                        UpdateCollider(targetMesh, currentVisualProp.transform.localScale);
                    }
                    else
                    {
                        // Mesh 不可读回退方案
                        UnityEngine.Debug.LogError($"[Physics] Mesh '{targetMesh.name}' is NOT readable!");
                        if (humanBoxCollider != null)
                        {
                            humanBoxCollider.enabled = true;
                            humanBoxCollider.center = targetMesh.bounds.center;
                            humanBoxCollider.size = Vector3.Scale(targetMesh.bounds.size, currentVisualProp.transform.localScale);
                        }
                    }
                }
            }
            else
            {
                // 实在找不到 Mesh，回退到 BoxCollider
                if (humanBoxCollider != null) humanBoxCollider.enabled = true;
            }
            // 7. 刷新轮廓 (修改此段)
            var outline = GetComponent<PlayerOutline>();
            if (outline != null && currentVisualProp != null) 
            {
                // 【核心修复】：健壮的 Renderer 查找逻辑
                Renderer[] allRenderers = currentVisualProp.GetComponentsInChildren<Renderer>();
                Renderer targetR = null;

                // 优先级 1：寻找名字里带 LOD0 的（针对分层级模型）
                foreach (var r in allRenderers)
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (r.name.Contains("LOD0")) { targetR = r; break; }
                }

                // 优先级 2：如果没有 LOD0，找第一个非粒子的渲染器（针对单模型物体）
                if (targetR == null)
                {
                    foreach (var r in allRenderers)
                    {
                        if (r is ParticleSystemRenderer) continue;
                        targetR = r;
                        break;
                    }
                }
                
                if (targetR != null) 
                {
                    outline.RefreshRenderer(targetR); 
                }
            }
            
            // 8. 【新增】启用我的 PropTarget，允许别人瞄准我变身后的模型
            myPropTarget.enabled = true;
            // 修改这一行调用：传入整个 GameObject 而不是单个 Renderer
            myPropTarget.ManualInit(propID, currentVisualProp);
            gameObject.layer = LayerMask.NameToLayer("Prop"); // 确保层级能被射线打到
            if (isStealthed)
            {
                Renderer[] newRenderers = currentVisualProp.GetComponentsInChildren<Renderer>(true);
                foreach (var r in newRenderers) r.enabled = false;

                // 本地玩家如果是方案3（自己看得到半透明），这里要做额外处理
                if (isLocalPlayer) SetLocalVisibility(true); // 让自己可见
            }
        }
        
        GameManager.Instance?.ServerPlay3DAt("女巫变身", transform.position);

        // 确保这段代码在 UpdateCollider 之后执行
        
        if (isLocalPlayer)
        {
            // 强制刷新一次目标位置
            UpdateCameraView(); 
        }
    }


    // =========================================================
    // 宿主专用服务器逻辑
    // =========================================================

    // 缓存每个乘客的当前帧输入 <netId, input>
    private Dictionary<uint, Vector2> passengerInputs = new Dictionary<uint, Vector2>();

    [Server]
    public void ServerAddPassenger(uint pid)
    {
        if (!passengerNetIds.Contains(pid))
        {
            passengerNetIds.Add(pid);
            passengerInputs[pid] = Vector2.zero;
        }
    }

    [Server]
    public void ServerRemovePassenger(uint pid)
    {
        if (passengerNetIds.Contains(pid))
        {
            passengerNetIds.Remove(pid);
            passengerInputs.Remove(pid);
            RecalculateCombinedInput();
        }
    }

    [Server]
    public void ServerUpdatePassengerInput(uint pid, Vector2 input)
    {
        if (passengerNetIds.Contains(pid))
        {
            passengerInputs[pid] = input;
            RecalculateCombinedInput();
        }
    }

    [Server]
    private void RecalculateCombinedInput()
    {
        Vector2 sum = Vector2.zero;
        foreach (var kvp in passengerInputs)
        {
            sum += kvp.Value;
        }
        combinedPassengerInput = sum; // 更新 SyncVar，所有客户端都会收到最新的合力
    }

    // =========================================================
    // 视觉处理
    // =========================================================

    [ClientRpc]
    private void RpcSetVisible(bool visible)
    {
        // 调用上面的本地方法
        SetLocalVisibility(visible);
    }

    // 钩子：当宿主ID变化时（乘客端执行）
    void OnHostNetIdChanged(uint oldId, uint newId)
    {
        if (isLocalPlayer)
        {
            if (newId != 0)
            {
                // 刚上车
                if (sceneScript != null && sceneScript.RunText != null)
                {
                    sceneScript.RunText.gameObject.SetActive(true);
                    sceneScript.RunText.text = "Press WASD to help move!\nPress SPACE to exit!";
                }
            }
            else
            {
                // 刚下车
                if (sceneScript != null && sceneScript.RunText != null)
                    sceneScript.RunText.gameObject.SetActive(false);

                // 恢复摄像机
                UpdateCameraView();
            }
        }
    }

    // ----------------------------------------------------
    // 网络同步：恢复原状
    // ----------------------------------------------------
    [Server]
    public void ServerOnReachPortal()
    {
        GameManager.Instance?.ServerPlay3DAt("传送门", transform.position);
        // 只有当前正在驾驶古树的人才能触发回收逻辑
        if (possessedTreeNetId != 0)
        {
            // 1. 记录该女巫完成任务
            hasDeliveredTree = true;

            // 2. 彻底移除这棵古树（回收）
            if (NetworkServer.spawned.TryGetValue(possessedTreeNetId, out NetworkIdentity treeIdentity))
            {
                // 将树隐藏并放到极远位置（或者直接 Destroy，但隐藏更安全防止引用报错）
                PropTarget tree = treeIdentity.GetComponent<PropTarget>();
                if (tree != null)
                {
                    tree.ServerSetHidden(true);
                    tree.transform.position = Vector3.down * 1000f;
                    // 【核心修改】古树被回收，地图上可用的古树数量减 1
                    if (GameManager.Instance != null)
                    {
                        GameManager.Instance.availableAncientTreesCount--;
                    }
                }
            }
            possessedTreeNetId = 0;

            // 3. 增加全局计数
            GameManager.Instance.RegisterTreeDelivery();

            // 4. 强制所有人下车
            ServerKickAllPassengers();

            // 5. 自身恢复人形
            isMorphed = false;
            morphedPropID = -1;
        }
    }
    void OnDeliveryStatusChanged(bool oldVal, bool newVal)
    {
        if (newVal && isLocalPlayer)
        {
            goalText = "Goal Accomplished! Help your sisters as a passenger!";
            if (sceneScript != null) sceneScript.GoalText.text = goalText;
        }
    }
    void OnItemChanged(int oldIndex, int newIndex)
    {
        // 处理物品变化的逻辑
        if (oldIndex >= 0 && oldIndex < witchItems.Length)
        {
            witchItems[oldIndex].SetActive(false);
        }
        if (newIndex >= 0 && newIndex < witchItems.Length)
        {
            witchItems[newIndex].SetActive(true);
        }
    }
    [Command]
    public void CmdChangeItem(int itemIndex)
    {
        if (itemIndex >= 0 && itemIndex < witchItems.Length)
        {
            currentItemIndex = itemIndex;
        }
    }
    [Command]
    private void CmdPossessAncientTree(uint treeNetId)
    {
        // 【新增限制】如果已经带回过古树，不能再次成为宿主（驾驶员）
        if (hasDeliveredTree)
        {
            UnityEngine.Debug.Log($"[Server] {playerName} has already delivered a tree and cannot drive again.");
            return;
        }
        if (!NetworkServer.spawned.TryGetValue(treeNetId, out NetworkIdentity treeIdentity)) return;
        PropTarget tree = treeIdentity.GetComponent<PropTarget>();

        if (tree == null || !tree.isAncientTree) return;

        // --- 核心逻辑：判断树是否已经被别人附身 ---
        WitchPlayer existingHost = null;
        foreach (var player in AllPlayers)
        {
            if (player is WitchPlayer witch && witch.possessedTreeNetId == treeNetId && witch.possessedTreeNetId != 0)
            {
                existingHost = witch;
                break;
            }
        }

        if (existingHost != null)
        {
            // 情况 A: 树已被附身 -> 加入成为乘客 (实现多人附身)
            if (existingHost.netId == this.netId) return; // 不能附身自己

            this.hostNetId = existingHost.netId;
            existingHost.ServerAddPassenger(this.netId);
            RpcSetVisible(false); // 隐藏自己
            UnityEngine.Debug.Log($"[Server] {playerName} joined tree host {existingHost.playerName}");
        }
        else
        {
            // 情况 B: 树是空的 -> 我成为宿主
            // 1. 让场景里的树消失
            tree.ServerSetHidden(true);

            // 2. 我变身成这棵树
            this.possessedTreeNetId = treeNetId;
            this.isMorphed = true;
            this.morphedPropID = tree.propID; // 使用树的 PropID

            // 3. 瞬间移动到树的位置，保证无缝衔接
            this.transform.position = tree.transform.position;
            this.transform.rotation = tree.transform.rotation;
            // =========================================================
            // 【核心修复】必须在服务器端立刻执行物理变身逻辑！
            // 否则服务器上的碰撞体还是人形，猎人打不到树干。
            // =========================================================
            ApplyMorph(tree.propID);
            UnityEngine.Debug.Log($"[Server] {playerName} possessed Ancient Tree: {tree.name}");
        }
    }


    // 重写基类的钩子函数
    protected override void OnMorphedPropIDChanged(int oldID, int newID)
    {
        if (isServer) return; // 服务器已经在 Cmd 里跑过了，跳过
        if (newID >= 0)
        {
            isMorphed = true;
            ApplyMorph(newID);
        }
        else
        {
            isMorphed = false;
            ApplyRevert();
        }
    }


    [Command]
    public void CmdUpdateMoveSpeed(float newSpeed)
    {
        // 服务器收到命令，修改 SyncVar，随后会自动同步给所有客户端
        moveSpeed = newSpeed;
    }

    [Command]
    private void CmdRevert()
    {
        GameManager.Instance?.ServerPlay3DAt("女巫变人", transform.position);
        // 使用新提炼的方法
        ServerReleaseTreeAtCurrentPosition();

        ServerKickAllPassengers(); // 踢掉所有同乘的女巫
        isMorphed = false;
        morphedPropID = -1;
        // 【核心修复】服务器自己也要恢复
        ApplyRevert();
    }

    private void ApplyRevert()
    {
        if (currentVisualProp != null) Destroy(currentVisualProp);
        propAnimator = null;

        // 1. 暂时禁用 CC 以便安全修改位置和参数
        controller.enabled = false;

        // 1. 获取一个安全弹开的方向
        // 如果是刚从古树变回来，我们往后方和上方弹得更远一些
        Vector3 escapeDir = -transform.forward; 
        if (possessedTreeNetId != 0)
        {
            // 如果是古树，弹开距离要大于树的半径（假设树半径1.5米，我们弹开2米）
            transform.position += escapeDir * 2.0f + Vector3.up * 1.0f;
        }
        else
        {
            transform.position += Vector3.up * (originalCCHeight * 0.5f);
        }

        // 2. 物理扫描排空（防止新位置依然卡在其他树里）
        // 尝试在周围找一个空地
        Collider[] overlaps = new Collider[5];
        int count = Physics.OverlapSphereNonAlloc(transform.position, originalCCRadius, overlaps, propLayer | groundLayer);
        if (count > 0)
        {
            // 如果还卡着，随机找个方向推
            transform.position += Quaternion.Euler(0, Random.Range(0, 360), 0) * Vector3.forward * 1.5f;
        }

        // 2. 【核心修复】解决掉下地板问题
        // 在恢复人形前，将坐标向上抬升（通常抬升人类高度的一半，防止下半身卡进地里）
        // 如果是从很矮的物体恢复，这个位移是必须的
        transform.position += Vector3.up * (originalCCHeight * 0.5f);
        // 3. 检查头顶是否有东西，如果有，尝试向后退一点
        if (Physics.Raycast(transform.position, Vector3.up, out RaycastHit headHit, originalCCHeight)) {
            // 如果头顶有树枝等碰撞体，将人稍微推离
            transform.position -= transform.forward * 0.5f;
        }


        // 3. 关闭变身用的 MeshCollider
        if (myMeshCollider != null)
        {
            myMeshCollider.sharedMesh = null;
            myMeshCollider.enabled = false;
        }

        // 4. 视觉恢复
        if (humanModelGroup != null)
        {
            humanModelGroup.SetActive(true);
            Renderer[] humanRenderers = humanModelGroup.GetComponentsInChildren<Renderer>(true);
            foreach (var r in humanRenderers) r.enabled = true;
            
            // 【核心修复】：重新从人类模型组里提取主渲染器
            // 巫师模型通常由 SkinnedMeshRenderer 组成
            foreach (var r in humanRenderers)
            {
                if (r is ParticleSystemRenderer) continue;
                myRenderer = r; // 重新给 myRenderer 赋值，确保它是活的
                break;
            }
        }

        if (HideGroup != null) HideGroup.SetActive(true);

        // 5. 【核心修复】恢复 CC 原始参数
        controller.height = originalCCHeight;
        controller.radius = originalCCRadius;
        controller.center = originalCCCenter;

        // 6. 重置重力速度，防止累积的重力瞬间把人拍进地底
        velocity.y = 0;

        // 7. 重新启用 CC
        controller.enabled = true;

        // 8. 恢复速度逻辑
        moveSpeed = originalHumanSpeed;
        if (isLocalPlayer) CmdUpdateMoveSpeed(originalHumanSpeed);

        // 刷新轮廓和层级

        if (myPropTarget != null) myPropTarget.enabled = false;

        int playerLayer = LayerMask.NameToLayer("Player");
        gameObject.layer = (playerLayer == -1) ? 0 : playerLayer;
        // 【新增】恢复人形相机目标
        if (isLocalPlayer)
        {
            UpdateCameraView();
        }
        // 确保描边脚本重新指向人类的 Renderer (myRenderer 是tripo_node上的)
        var outline = GetComponent<PlayerOutline>();
        if (outline != null && myRenderer != null)
        {
            // 确保 outline 脚本指向新的（恢复的）人类渲染器
            outline.RefreshRenderer(myRenderer); 
        }

        // 强制刷新本地所有玩家的视觉状态
        if (isLocalPlayer) {
            GetComponent<TeamVision>()?.ForceUpdateVisuals();
        }
        else {
            // 如果是远程玩家，本地控制权在 NetworkClient.localPlayer 身上
            NetworkClient.localPlayer?.GetComponent<TeamVision>()?.ForceUpdateVisuals();
        }
    }
    // 隐身状态改变时调用
    void OnStealthChanged(bool oldVal, bool newVal)
    {
        // 即使是后加入的玩家，也会自动调用这个 Hook，看到正确的隐身状态
        UpdateStealthVisuals(newVal);

        // 处理本地 UI 提示
        if (isLocalPlayer && sceneScript != null && sceneScript.RunText != null)
        {
            UnityEngine.Debug.Log($"[Client] Stealth status changed: {newVal}");
            sceneScript.RunText.gameObject.SetActive(newVal);
            if (newVal) sceneScript.RunText.text = "INVISIBILITY ACTIVE";
        }
    }
    //激活生命护符
    [Command]
    public void CmdActivateAmulet(float duration)
    {
        if (isProtectedByAmulet) return;

        isProtectedByAmulet = true;
        // 开启30秒倒计时
        StartCoroutine(AmuletTimerRoutine(duration));
    }

    [Server]
    private IEnumerator AmuletTimerRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);

        // 时间到，且没有被消耗掉（消耗掉时会设为false）
        if (isProtectedByAmulet)
        {
            isProtectedByAmulet = false;
        }
    }
    //生命护符状态改变时调用的
    void OnAmuletProtectionChanged(bool oldVal, bool newVal)
    {
        if (isLocalPlayer && sceneScript != null && sceneScript.RunText != null)
        {
            if (newVal)
            {
                sceneScript.RunText.gameObject.SetActive(true);
                sceneScript.RunText.text = "LIFE AMULET ACTIVE";
            }
            else
            {
                sceneScript.RunText.gameObject.SetActive(false);
            }
        }
    }
    [Server]
    public override void ServerTakeDamage(float amount)
    {
        // 如果有护符保护，且伤害足以致死
        if (isProtectedByAmulet && (currentHealth - amount) <= 0)
        {
            TriggerAmuletSave(); // 触发救命逻辑
            return; // 关键：直接返回，不扣血，不死亡
        }

        // 否则正常受伤
        base.ServerTakeDamage(amount);
    }
    [Server]
    private void TriggerAmuletSave()
    {
        UnityEngine.Debug.Log($"<color=green>[Server] {playerName} saved by Life Amulet!</color>");
        AudioManager.Instance?.Play3D("护符碎裂", transform.position);
        // 1. 消耗保护状态
        isProtectedByAmulet = false;

        // 2. 锁血为 1
        currentHealth = 1f;

        // 3. 开启 Buff (无敌 + 加速)
        StartCoroutine(AmuletBuffRoutine());
    }
    [Server]
    private IEnumerator AmuletBuffRoutine()
    {
        float originalSpeed = moveSpeed;
        isInvulnerable = true; // 开启基类无敌
        UnityEngine.Debug.Log("Buff Activate!");
        moveSpeed *= amuletSpeedMultiplier;
        yield return new WaitForSeconds(3.0f); // 持续3秒
        isInvulnerable = false;
        moveSpeed = originalSpeed;
        UnityEngine.Debug.Log("Buff End!");
    }


    // 【新增】服务器专用：强制踢出所有乘客
    [Server]
    private void ServerKickAllPassengers()
    {
        // 1. 复制列表，防止遍历时修改集合报错
        List<uint> passengersToKick = new List<uint>(passengerNetIds);

        foreach (uint pid in passengersToKick)
        {
            if (NetworkServer.spawned.TryGetValue(pid, out NetworkIdentity pIdentity))
            {
                WitchPlayer pWitch = pIdentity.GetComponent<WitchPlayer>();
                if (pWitch != null)
                {
                    // 修改乘客的 SyncVar，让它知道自己下车了
                    pWitch.hostNetId = 0;

                    // 恢复乘客的可见性
                    pWitch.RpcSetVisible(true);

                    // 强制客户端重置状态（位置、摄像机）
                    pWitch.TargetForceLeave(pIdentity.connectionToClient);
                }
            }
        }

        // 2. 清空宿主的乘客列表
        passengerNetIds.Clear();
        combinedPassengerInput = Vector2.zero;
    }

    // 辅助 Rpc：用于强制乘客端重置状态 (可选，增加鲁棒性)
    [TargetRpc]
    public void TargetForceLeave(NetworkConnection target)
    {
        // 1. 恢复显示
        SetLocalVisibility(true);

        // 2. 计算弹射方向 
        // 使用 Random.onUnitSphere 并在平面上归一化，保证是向四周弹开
        // 【修改】增大半径从 1.5f -> 2.5f，防止卡在体积较大的古树或石头里
        Vector2 randomCircle = UnityEngine.Random.insideUnitCircle.normalized * 2.5f;

        // 【修改】增加一点 Y 轴偏移 (Vector3.up * 1.5f)，相当于稍微往天上跳一下，避免卡在地板或树根里
        Vector3 ejectOffset = new Vector3(randomCircle.x, 1.5f, randomCircle.y);

        // 3. 应用位置偏移 
        // 注意：此时 transform.position 还是宿主的位置（因为刚停止 Update 跟随）
        transform.position += ejectOffset;

        // 4. 重置摄像机
        UpdateCameraView();

        // 5. 【新增】重置速度
        // 防止下车时继承了奇怪的动量滑行
        if (controller != null)
        {
            // 这里无法直接修改 controller.velocity，但可以重置我们在 Update 里计算的 velocity 变量
            // 如果你有定义 private Vector3 velocity; 建议在这里重置:
            // velocity = Vector3.zero; 
        }

        UnityEngine.Debug.Log("Exited vehicle via TargetForceLeave");
    }

    // 【新增】本地辅助方法：只负责改状态，不涉及网络通信
    private void SetLocalVisibility(bool visible)
    {
        // 1. 处理基础组件
        if (controller != null) controller.enabled = visible;

        // 获取身上所有的 Renderer (包括子物体)
        Renderer[] allRenderers = GetComponentsInChildren<Renderer>(true);

        if (!visible)
        {
            // 如果是隐藏（上车），全部关掉
            foreach (var r in allRenderers) r.enabled = false;
            if (humanModelGroup != null) humanModelGroup.SetActive(false);
            if (nameText != null) nameText.gameObject.SetActive(false);
            // 关闭父级碰撞体，防止挡住“驾驶员”
            if (myMeshCollider != null) myMeshCollider.enabled = false;
            if (humanBoxCollider != null) humanBoxCollider.enabled = false;
        }
        else
        {
            // 如果是显示（下车），根据当前状态智能恢复
            if (isMorphed)
            {
                // 如果我还在变身状态，显示变身后的模型，保持人类模型隐藏
                if (humanModelGroup != null) humanModelGroup.SetActive(false);
                if (currentVisualProp != null)
                {
                    currentVisualProp.SetActive(true);
                    foreach (var r in currentVisualProp.GetComponentsInChildren<Renderer>()) r.enabled = true;
                }
                if (myMeshCollider != null && morphedPropID != -1) myMeshCollider.enabled = true;
            }
            else
            {
                // 如果我是人类状态，恢复人类模型
                if (humanModelGroup != null)
                {
                    humanModelGroup.SetActive(true);

                    // 【关键修复】必须重新启用 humanModelGroup 下的所有 Renderer 组件
                    // 因为在上车时我们把它们暴力设为了 enabled = false
                    Renderer[] humanRenderers = humanModelGroup.GetComponentsInChildren<Renderer>(true);
                    foreach (var r in humanRenderers)
                    {
                        r.enabled = true;
                    }
                }

                // 这一行其实可以保留作为保险，或者有了上面的循环可以删掉
                if (myRenderer != null) myRenderer.enabled = true;

                if (humanBoxCollider != null) humanBoxCollider.enabled = true;
                if (nameText != null) nameText.gameObject.SetActive(true);
            }
        }
    }

    private void UpdateCollider(Mesh mesh, Vector3 scale)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc == null) return;

        // 1. 暂时禁用以安全修改参数
        cc.enabled = false;

        float meshHeight = mesh.bounds.size.y * scale.y;
        float meshWidth = Mathf.Max(mesh.bounds.size.x * scale.x, mesh.bounds.size.z * scale.z);

        // 稍微收缩半径，防止变身后变成“推土机”
        // 【修改】如果是古树（通过 possessedTreeNetId 判断），允许更大的半径
        float maxR = (possessedTreeNetId != 0) ? 2.5f : 0.6f; 
        float newRadius = Mathf.Clamp(meshWidth * 0.35f, 0.15f, maxR);
        float newHeight = meshHeight;

        // 2. 应用参数
        cc.height = newHeight;
        cc.radius = newRadius;
        cc.center = new Vector3(0, newHeight * 0.5f, 0);
        cc.stepOffset = Mathf.Min(0.3f, cc.height * 0.4f);

        // 3. 执行简单的位移补偿（后退弹开）
        ResolveOverlapSimple(cc);

        // 4. 重新启用
        cc.enabled = true;

        // 强制刷新物理状态
        cc.Move(Vector3.down * 0.01f); 
    }

    /// 检测变身后是否与环境重叠，并将其强制弹开
    // 修改函数签名，接受 CharacterController 作为参数
    private void ResolveOverlapSimple(CharacterController cc)
    {
        // 检测范围定义（稍微比 CC 大一点点，预留容错）
        Vector3 p1 = transform.position + Vector3.up * cc.radius;
        Vector3 p2 = transform.position + Vector3.up * (cc.height - cc.radius);

        // 如果检测到当前位置会和树木或地面重叠
        if (Physics.CheckCapsule(p1, p2, cc.radius * 0.9f, propLayer | groundLayer))
        {
            // 方案：直接向玩家当前的后方弹开 0.8米，并向上微调 0.2米防止陷入地表
            // 这样可以有效跳出树叶的覆盖范围
            Vector3 escapeVector = (-transform.forward * 0.8f) + (Vector3.up * 0.2f);
            
            // 检查后方是否有空间，如果后方也是死路（比如背靠墙），则只往上弹
            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, -transform.forward, 1.0f, propLayer | groundLayer))
            {
                // 后方有墙，改为垂直向上弹
                transform.position += Vector3.up * 0.5f;
                UnityEngine.Debug.Log($"[Witch] Morph stuck! Backwards blocked, popping UP.");
            }
            else
            {
                // 正常向后弹
                transform.position += escapeVector;
                UnityEngine.Debug.Log($"[Witch] Morph stuck! Popping BACKWARDS.");
            }
        }
    }

    // 重写基类的抽象方法
    protected override void Attack()
    {
        // 这里是服务器端运行的代码 (因为被 CmdAttack 调用)
        // Debug.Log($"<color=purple>【女巫】{playerName} 释放了技能：扔毒药！</color>");
        UnityEngine.Debug.Log($"<color=purple>[Witch] {playerName} used skill: Throw Poison!</color>");

        // 在这里写具体的实例化药水逻辑...
        // GameObject potion = Instantiate(potionPrefab, ...);
        // NetworkServer.Spawn(potion);
    }
    protected override void HandleDeath()
    {
        // =================================================================
        // 【新增修复】死亡时，如果手里有古树，先把它种在原地！
        // =================================================================
        if (isServer)
        {
            ServerReleaseTreeAtCurrentPosition();
        }
        // =================================================================
        // 【新增修复】当宿主死亡（无论是变青蛙还是彻底死亡）时，必须强制踢出所有乘客
        // =================================================================
        if (isServer && passengerNetIds.Count > 0)
        {
            // 这会让所有乘客：hostNetId归零、恢复可见、弹射出去
            ServerKickAllPassengers();
            UnityEngine.Debug.Log($"[Server] {playerName} died/transformed, ejecting all passengers.");
        }
        // =================================================================
        if (!isInSecondChance)
        {
            // --- 第一次死亡：进入复活赛 ---
            UnityEngine.Debug.Log($"{playerName} entered second chance mode!");
            isInSecondChance = true;

            // 恢复少量血量供逃跑
            currentHealth = frogHealth;

            // 强制变身为小动物
            morphedPropID = frogPropID;
            isMorphed = true;

            // 开启 3 秒无敌（仅在服务器执行）
            if (isServer)
            {
                StartCoroutine(ServerInvulnerabilityRoutine(3.0f));
            }
        }
        else
        {
            // --- 第二次死亡：彻底出局 ---
            UnityEngine.Debug.Log($"{playerName} is permanently dead!");
            isPermanentDead = true;
            // 死亡时确保提示文字消失
            if (isLocalPlayer && sceneScript != null && sceneScript.RunText != null)
                sceneScript.RunText.gameObject.SetActive(false);
        }
    }
    [Server]
    // 服务器端：处决玩家
    public void ServerGetExecuted(float damage)
    {
        // 1. 扣血
        ServerTakeDamage(damage);

        // 2. 强制解除禁锢 
        // 处决也会导致陷阱销毁
        if (isTrappedByNet)
        {
            ServerReleaseAndDestroyTrap();
        }
        // if (isTrappedByNet)
        // {
        //     isStunned = false;
        //     isTrappedByNet = false;
        //     currentClicks = 0; // 重置挣扎次数
        //     UnityEngine.Debug.Log($"<color=red>{playerName} 被处决并强制释放！</color>");
        // }
    }

    // 服务器端无敌协程
    [Server]
    private System.Collections.IEnumerator ServerInvulnerabilityRoutine(float duration)
    {
        isInvulnerable = true;
        UnityEngine.Debug.Log($"{playerName} is now invulnerable for {duration}s");

        yield return new WaitForSeconds(duration);

        isInvulnerable = false;
        UnityEngine.Debug.Log($"{playerName} is no longer invulnerable");
    }
    protected override void OnSecondChanceChanged(bool oldVal, bool newVal)
    {
        // 只有本地玩家且 SceneScript 存在时处理
        if (isLocalPlayer && sceneScript != null && sceneScript.RunText != null)
        {
            sceneScript.RunText.gameObject.SetActive(newVal);
            if (newVal)
            {
                sceneScript.RunText.text = "<color=red>YOU ARE HURT!</color>\nRUN TO THE PORTAL TO REVIVE!";
            }
        }
    }
    // 隐身斗篷的网络命令
    [Command]
    public void CmdUseInvisibilityCloak()
    {
        if (currentItemIndex >= 0 && currentItemIndex < witchItems.Length)
        {
            var cloak = witchItems[currentItemIndex].GetComponent<InvisibilityCloak>();

            if (cloak != null)
            {
                UnityEngine.Debug.Log($"[Server] {playerName} is using Invisibility Cloak via Index {currentItemIndex}");
                cloak.ServerActivateEffect(this);
                return;
            }
        }
    }

    private void UpdateStealthVisuals(bool isStealth)
    {
        if (isLocalPlayer) return;
        // 【核心修复 1】：获取当前看着屏幕的本地玩家，判断是不是队友
        GamePlayer myLocalViewPlayer = NetworkClient.localPlayer?.GetComponent<GamePlayer>();
        bool isTeammate = (myLocalViewPlayer != null && myLocalViewPlayer.playerRole == PlayerRole.Witch);

        // 猎人看隐身是不可见(!isStealth)，女巫看隐身的队友永远是可见(true)
        bool isVisible = isTeammate ? true : !isStealth;

        // 1. 隐藏头顶名字（隐身时，连队友也暂时不看名字，全靠高亮颜色认人）
        if (nameText != null) nameText.gameObject.SetActive(isVisible && !isStealth);

        // 2. 根据当前形态隐藏对应的模型
        if (isMorphed)
        {
            // 如果是变身状态，隐藏道具模型
            if (currentVisualProp != null)
            {
                Renderer[] renderers = currentVisualProp.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers) r.enabled = isVisible;
                UnityEngine.Debug.Log($"[Client] Stealth change: Setting prop renderers to {isVisible} for {playerName}");
            }
        }
        else
        {
            // 如果是人类状态
            if (humanModelGroup != null)
            {
                Renderer[] renderers = humanModelGroup.GetComponentsInChildren<Renderer>();
                foreach (var r in renderers) r.enabled = isVisible;
                UnityEngine.Debug.Log($"[Client] Stealth change: Setting human renderers to {isVisible} for {playerName}");
            }

            if (myRenderer != null) myRenderer.enabled = isVisible;
        }

        // 3. 隐藏描边
        // var outline = GetComponent<PlayerOutline>();
        // if (outline != null) outline.enabled = isVisible;
    }
    // 服务器端：由传送门调用
    [Server]
    public void ServerRevive()
    {
        if (!isInSecondChance || isPermanentDead) return;

        GameManager.Instance?.ServerPlay3DAt("传送门", transform.position);

        isInSecondChance = false;
        currentHealth = maxHealth;
        morphedPropID = -1; // 变回人类
        isMorphed = false;
        UnityEngine.Debug.Log($"{playerName} has been revived at the portal!");
    }
    protected override void OnPermanentDeadChanged(bool oldVal, bool newVal)
    {
        base.OnPermanentDeadChanged(oldVal, newVal);
        if (newVal)
        {
            SetPermanentDeath();
        }
    }

    private void SetPermanentDeath()
    {
        UnityEngine.Debug.Log($"[Client] {playerName} is now a spectator.");
        moveSpeed = 10f; // 允许观察者快速移动

        // 只有本地玩家且 SceneScript 存在时处理
        if (isLocalPlayer && sceneScript != null && sceneScript.RunText != null)
        {
            sceneScript.RunText.gameObject.SetActive(true);
            // 提示玩家他是观察者（Spectator）用英文写text
            sceneScript.RunText.text = "<color=yellow>You are now a spectator!</color>";
        }


        // 所有人不可见：禁用所有渲染器
        // 隐藏人类模型
        if (HideGroup != null) HideGroup.SetActive(false);
        // 隐藏可能存在的动物模型
        if (currentVisualProp != null) currentVisualProp.SetActive(false);
        // 隐藏原始渲染器
        if (myRenderer != null) myRenderer.enabled = false;
        // 隐藏名字
        // 只有在非结算状态下才隐藏名字，结算时（VictoryZone）名字必须留着
        if (nameText != null) 
        {
            bool isVictorySequence = GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver;
            nameText.gameObject.SetActive(isVictorySequence); 
        }

        // 2. 禁用交互：修改物理层级
        // 建议在 Unity 中创建一个 Layer 叫 "Spectator"，并在 Physics Matrix 中设置它不与 Player 碰撞
        gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

        // 3. 禁用碰撞体（针对非本地玩家直接禁用 CC）
        if (!isLocalPlayer)
        {
            if (controller != null) controller.enabled = false;
        }
        else
        {
            // 4. 本地玩家：作为观察者逻辑
            // 我们可以让本地玩家依然有碰撞，以便在场景中走动但不卡住别人
            // 或者你可以将 CC 的半径设为 0
            if (controller != null)
            {
                controller.radius = 0.01f;
            }

            // 提示 UI
            if (sceneScript != null)
            {
                // 假设你在 SceneScript 中有一个提示文本
                // sceneScript.GoalText.text = "<color=red>YOU ARE ELIMINATED (SPECTATING)</color>";
            }
        }

        // 5. 确保不再触发变身或还原
        isMorphed = false;
        isMorphedIntoAnimal = false;
    }
    public override void UpdateCameraView()
    {
        // 只有本地玩家才计算相机
        if (!isLocalPlayer) return;
        Camera.main.transform.SetParent(transform);
        if (isMorphed)
        {
            if (isFirstPerson)
            {
                // --- 变身状态：动态计算 ---
                // Y轴：高度的 0.9 倍
                float targetY = controller.height * 0.9f;
                // Z轴：半径距离（设为负数即在身后）。
                // 建议 * 2.5f 以防相机卡在模型内部，如果你严格想要 "radius" 距离，去掉 "* 2.5f" 即可
                float targetZ = controller.radius * 2.5f; 
                targetCamPos = new Vector3(0, targetY, targetZ);   
            }
            else
            {
                float targetY = controller.height * 1.3f;
                float targetZ = -controller.radius * 6f; 
                targetCamPos = new Vector3(0, targetY, targetZ);   
            } 
        }
        else
        {
            // --- 人类状态：恢复默认 ---
            if (isFirstPerson)
                targetCamPos = new Vector3(0.079f, 1.055f, 0.663f);
            else
                targetCamPos = new Vector3(0, 2.405f, -3.631f);
        }

        // 如果是第一次运行，直接瞬移，不要平滑（防止出生时相机乱飞）
        if (!isCamInitialized && Camera.main != null)
        {
            Camera.main.transform.localPosition = targetCamPos;
            isCamInitialized = true;
        }
    }
    [Command]
    void CmdSetTreeScouted(uint treeNetId)
    {
        if (NetworkServer.spawned.TryGetValue(treeNetId, out NetworkIdentity ni))
        {
            PropTarget prop = ni.GetComponent<PropTarget>();
            if (prop != null && !prop.isScouted)
            {
                prop.isScouted = true;
                scoutedCount++;
                
                if (scoutedCount % treesPerReward == 0)
                {
                    pendingRewards++;
                    
                    // --- 【核心修改：在服务器生成奖励】 ---
                    serverRewardPool.Clear();
                    serverRewardPool.Add(CreateAttributeReward());
                    serverRewardPool.Add(CreateSkillReward());
                    serverRewardPool.Add(CreateExtraReward());

                    // 将生成好的数组通过 RPC 发送给客户端
                    TargetShowRewardUI(connectionToClient, serverRewardPool.ToArray());
                }
            }
        }
    }
    // --- 奖励生成逻辑 ---

    private List<RewardOption> currentRewardPool = new List<RewardOption>();

    [TargetRpc]
    void TargetShowRewardUI(NetworkConnection target, RewardOption[] options)
    {
        AudioManager.Instance?.Play2D("叮");
        // 客户端存一份，用于 UI 显示
        currentRewardPool = new List<RewardOption>(options);
        
        // 显示 UI
        RewardUI.Instance.Show(options);
    }

    private RewardOption CreateAttributeReward()
    {
        int rand = Random.Range(0, 5);
        string[] titles = { "Healing", "Vitality", "Mana Soul", "Arcane Flow", "Celerity" };
        string[] keys = { "AddHP", "MaxHP", "AddMana", "MaxMana", "MoveSpeed" };
        float[] values = { 30f, 50f, 40f, 50f, 1.5f };
        
        return new RewardOption { 
            title = titles[rand], 
            description = $"Permanent {keys[rand]} +{values[rand]}", 
            category = RewardCategory.Attribute, 
            rewardKey = keys[rand], 
            value = values[rand],
            id = 0 // UI索引
        };
    }

    private RewardOption CreateSkillReward()
    {
        List<RewardOption> validOptions = new List<RewardOption>();

        // 辅助函数：检查某个类名是否在玩家选中的两个技能之中
        System.Func<string, bool> isSkillEquipped = (className) => {
            return syncedSkill1Name == className || syncedSkill2Name == className;
        };

        // 1. 检查迷雾 (Mist)
        if (isSkillEquipped("WitchSkill_Mist"))
        {
            validOptions.Add(new RewardOption { 
                title = "Abyssal Fog", 
                description = "Mist radius doubled (2x size)", 
                category = RewardCategory.Skill, 
                rewardKey = "MistRadius", 
                value = 2.0f 
            });
        }

        // 2. 检查诅咒 (Curse)
        if (isSkillEquipped("WitchSkill_Curse"))
        {
            validOptions.Add(new RewardOption { 
                title = "Extended Hex", 
                description = "Curse casting range +10m", 
                category = RewardCategory.Skill, 
                rewardKey = "CurseRange", 
                value = 10f 
            });
        }

        // 3. 检查分身 (Decoy)
        if (isSkillEquipped("WitchSkill_Decoy"))
        {
            validOptions.Add(new RewardOption { 
                title = "Triple Illusion", 
                description = "Decoy spawns 3 clones per use", 
                category = RewardCategory.Skill, 
                rewardKey = "DecoyCount", 
                value = 3f 
            });
        }

        // 4. 检查混沌 (Chaos)
        if (isSkillEquipped("WitchSkill_Chaos"))
        {
            validOptions.Add(new RewardOption { 
                title = "Chaos Mastery", 
                description = "Chaos disturbance radius +5m", 
                category = RewardCategory.Skill, 
                rewardKey = "ChaosRadius", 
                value = 5f 
            });
        }

        // 兜底逻辑：如果什么都没带（或是同步还没完成），给一个法力值相关的奖励
        if (validOptions.Count == 0)
        {
            return new RewardOption { 
                title = "Arcane Surge", 
                description = "Recover 50 Mana immediately", 
                category = RewardCategory.Attribute, 
                rewardKey = "AddMana", 
                value = 50f 
            };
        }

        // 随机返回一个已装备技能的增益
        return validOptions[Random.Range(0, validOptions.Count)];
    }

    private RewardOption CreateExtraReward()
    {
        if (Random.value > 0.5f)
            return new RewardOption { title = "Hunter Scent", description = "Reveal Hunters for 10s", category = RewardCategory.Extra, rewardKey = "HunterVision", value = 10f };
        else
            return new RewardOption { title = "Forest Spirit", description = "Reveal Ancient Trees for 5s", category = RewardCategory.Extra, rewardKey = "AncientVision", value = 5f };
    }

    [Command]
    public void CmdSelectReward(int index)
    {
        // 使用服务器自己的 serverRewardPool 进行校验
        if (pendingRewards <= 0 || index >= serverRewardPool.Count) return;
        
        pendingRewards--;
        
        var choice = serverRewardPool[index];
        ApplyRewardEffect(choice.rewardKey, choice.value);
        
        // 选完后清空服务器缓存
        serverRewardPool.Clear(); 
    }

    [Server]
    private void ApplyRewardEffect(string key, float val)
    {
        switch (key)
        {
            // ======= 属性类 =======
            case "AddHP":
                currentHealth = Mathf.Min(maxHealth, currentHealth + val);
                break;
            case "MaxHP":
                maxHealth += val;
                currentHealth += val;
                break;
            case "AddMana":
                currentMana = Mathf.Min(maxMana, currentMana + val);
                break;
            case "MaxMana":
                maxMana += val;
                currentMana += val;
                break;
            case "MoveSpeed":
                originalHumanSpeed += val; // 永久提升基础速度
                if (!isMorphed) moveSpeed = originalHumanSpeed;
                break;

        // ======= 迷雾增强 =======
            case "MistRadius":
                var mistSkill = GetComponent<WitchSkill_Mist>();
                if (mistSkill) mistSkill.mistScale = val; // 将倍率设为 2.0
                break;

            // ======= 诅咒增强 =======
            case "CurseRange":
                var curseSkill = GetComponent<WitchSkill_Curse>();
                if (curseSkill) curseSkill.range += val; // 增加射程
                break;

            // ======= 原有的技能增强 =======
            case "DecoyCount":
                var decoySkill = GetComponent<WitchSkill_Decoy>();
                if (decoySkill) decoySkill.spawnCount = (int)val;
                break;
            case "ChaosRadius":
                var chaosSkill = GetComponent<WitchSkill_Chaos>();
                if (chaosSkill) chaosSkill.radius += val;
                break;

            // ======= 额外奖励类 =======
            case "AncientVision":
                TargetTempRevealAncient(connectionToClient, val);
                break;
            case "HunterVision":
                TargetTempRevealHunters(connectionToClient, val);
                break;
        }
    }

    // --- 客户端辅助逻辑：临时透视 --- 
    //debug

    [TargetRpc]
    private void TargetTempRevealAncient(NetworkConnection target, float duration)
    {
        StartCoroutine(TempAncientHighlightRoutine(duration));
    }

    private IEnumerator TempAncientHighlightRoutine(float duration)
    {
        // 获取场景中所有的 PropTarget
        PropTarget[] all = Object.FindObjectsOfType<PropTarget>();
        List<PropTarget> ancients = new List<PropTarget>();
        
        foreach (var p in all)
        {
            // 找到古树且当前没被发现的
            if (p.isAncientTree && !p.isScouted)
            {
                ancients.Add(p);
                p.isLocalTempRevealed = true; // 修改本地临时变量
            }
        }
        
        // 【关键】手动通知本地的 TeamVision 刷新一次视觉
        GetComponent<TeamVision>()?.ForceUpdateVisuals();
        
        yield return new WaitForSeconds(duration);
        
        // 恢复
        foreach (var p in ancients) 
        {
            if (p != null) p.isLocalTempRevealed = false;
        }
        
        // 【关键】再次刷新视觉
        GetComponent<TeamVision>()?.ForceUpdateVisuals();
    }
    [TargetRpc]
    private void TargetTempRevealHunters(NetworkConnection target, float duration)
    {
        var vision = GetComponent<TeamVision>();
        if (vision != null) StartCoroutine(vision.TempShowEnemies(duration));
    }



    [Server]
    private void ServerReleaseTreeAtCurrentPosition()
    {
        if (possessedTreeNetId != 0)
        {
            // 找到那棵被隐藏的树
            if (NetworkServer.spawned.TryGetValue(possessedTreeNetId, out NetworkIdentity treeIdentity))
            {
                PropTarget tree = treeIdentity.GetComponent<PropTarget>();
                if (tree != null)
                {
                    // 将树“种”在女巫当前倒下的位置
                    tree.transform.position = this.transform.position;
                    tree.transform.rotation = this.transform.rotation;
                    tree.ServerSetHidden(false); // 重新显示树
                    UnityEngine.Debug.Log($"[Server] {playerName} died/reverted, tree {possessedTreeNetId} planted at {transform.position}");
                }
            }
            possessedTreeNetId = 0; // 清除 ID
        }
    }

}
```

## Player\Rewards\WitchReward.cs

```csharp
using UnityEngine;

public enum RewardCategory { Attribute, Skill, Extra }

[System.Serializable]
public struct RewardOption
{
    public string title;
    public string description;
    public RewardCategory category;
    public string rewardKey; // 用于标识具体的逻辑，例如 "MaxHP", "DecoyCount"
    public float value;      // 奖励数值
    public int id;           // 传递给服务器的唯一索引
}
```

## Skill\PlayerSkillManager.cs

```csharp
using UnityEngine;
using Mirror;
using System.Collections; // 必须引用协程
using System.Collections.Generic;

public class PlayerSkillManager : NetworkBehaviour
{
    [Header("Skill Configuration")]
    public List<SkillData> skillDatabase; // 在预制体里把 7 个 SkillData 资产拖进去
    
    private SkillBase[] activeSkillsArray; 
    private GamePlayer player;

    public override void OnStartLocalPlayer()
    {
        player = GetComponent<GamePlayer>();
        
        // 使用协程确保 SceneScript 已经初始化完成
        StartCoroutine(InitSkillsAndUIRoutine());
    }

    private IEnumerator InitSkillsAndUIRoutine()
    {
        // 1. 等待场景中的 SceneScript 准备就绪
        while (SceneScript.Instance == null || SceneScript.Instance.skillSlots == null)
        {
            yield return null;
        }

        // 2. 获取选中的脚本名称列表（从持久化单例读取）
        List<string> selectedClasses = (player is WitchPlayer) 
            ? PlayerSettings.Instance.selectedWitchSkillNames 
            : PlayerSettings.Instance.selectedHunterSkillNames;

        // --- 【新增：同步给其他玩家】 ---
        if (selectedClasses != null && selectedClasses.Count >= 2)
        {
            player.CmdSyncSkillNames(selectedClasses[0], selectedClasses[1]);
        }

        // 如果是大厅直接进游戏测试，列表可能为空，做一个保底逻辑
        if (selectedClasses == null || selectedClasses.Count == 0)
        {
            Debug.LogWarning("[PlayerSkillManager] 选中的技能列表为空，请检查 Lobby 选择逻辑。");
            yield break;
        }

        List<SkillBase> runtimeSkills = new List<SkillBase>();

        // 3. 激活并映射技能
        for (int i = 0; i < selectedClasses.Count; i++)
        {
            string className = selectedClasses[i];
            
            // 获取挂在玩家预制体身上的对应脚本组件
            SkillBase skillComp = GetComponent(className) as SkillBase;

            if (skillComp != null)
            {
                //Debug.Log($"[SkillDebug] 成功找到组件: {className}");
                // 激活脚本逻辑
                skillComp.enabled = true;
                skillComp.Init(player);
                
                // 强制分配按键：第一个选中的是 Q，第二个选中的是 E
                skillComp.triggerKey = (i == 0) ? KeyCode.Q : KeyCode.E;
                
                runtimeSkills.Add(skillComp);

                // --- 【核心修改：更新游戏内 UI】 ---
                // 从数据库中根据脚本类名找到对应的图标资产
                var data = skillDatabase.Find(d => d.scriptClassName == className);
                if (data != null)
                {
                    // 将图标和分配的按键名称（"Q" 或 "E"）传给 SceneScript 的 UI 槽位
                    if (i < SceneScript.Instance.skillSlots.Length)
                    {
                        SceneScript.Instance.skillSlots[i].Setup(data.icon, skillComp.triggerKey.ToString());
                        SceneScript.Instance.skillSlots[i].gameObject.SetActive(true);
                    }
                }
            }
            else
            {
                Debug.LogError($"[SkillDebug] 未找到组件: {className}，请确保已挂载在玩家预制体上。(可能是skillData那里有空格！！！！！！！！！！！！！！！！)");
            }
        }
        
        activeSkillsArray = runtimeSkills.ToArray();
    }

    public override void OnStartServer()
    {
        player = GetComponent<GamePlayer>();
        foreach (var s in GetComponents<SkillBase>())
        {
            s.Init(player);
        }
    }

    // public override void OnStartClient()
    // {
    //     base.OnStartClient();
    //     // 如果是本地玩家，OnStartLocalPlayer 已经处理过了，这里跳过避免重复
    //     if (isLocalPlayer) return;

    //     player = GetComponent<GamePlayer>();
    //     foreach (var s in GetComponents<SkillBase>())
    //     {
    //         s.Init(player);
    //     }
    // }

    private void Update()
    {
        if (!isLocalPlayer || activeSkillsArray == null) return;
        // 2. 【核心修改】如果游戏已结束，直接返回，不处理任何技能按键
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
            return;
        // 处理技能按键触发
        if (Cursor.lockState == CursorLockMode.Locked && !player.isChatting && !player.isStunned)
        {
            foreach (var skill in activeSkillsArray)
            {
                if (skill != null && Input.GetKeyDown(skill.triggerKey))
                {
                    skill.TryCast();
                }
            }
        }

        // 更新 UI 冷却进度条
        if (SceneScript.Instance != null && SceneScript.Instance.skillSlots != null)
        {
            for (int i = 0; i < activeSkillsArray.Length; i++)
            {
                if (i < SceneScript.Instance.skillSlots.Length && activeSkillsArray[i] != null)
                {
                    SceneScript.Instance.skillSlots[i].UpdateCooldown(activeSkillsArray[i].CooldownRatio);
                }
            }
        }
    }
}
```

## Skill\SkillBase.cs

```csharp
using UnityEngine;
using Mirror;

public abstract class SkillBase : NetworkBehaviour
{
    [Header("Skill Settings")]
    public string skillName;
    // public Sprite icon;
    public float cooldownTime = 5f;
    public KeyCode triggerKey;
    
    [SyncVar]
    private double lastUseTime;

    protected GamePlayer ownerPlayer;

    public float CooldownRatio
    {
        get
        {
            float duration = (float)(NetworkTime.time - lastUseTime);
            if (duration >= cooldownTime) return 0f;
            return 1f - (duration / cooldownTime);
        }
    }

    public bool IsReady => (NetworkTime.time - lastUseTime) >= cooldownTime;

    public void Init(GamePlayer player)
    {
        ownerPlayer = player;
        lastUseTime = -cooldownTime; // 初始就绪
    }

    // 客户端尝试释放技能
    public void TryCast()
    {
        if (IsReady)
        {
            CmdCast();
        }
    }

    [Command]
    private void CmdCast()
    {
        if (!IsReady) return;
        
        // 记录时间 (NetworkTime 用于同步)
        lastUseTime = NetworkTime.time;

        // 执行具体逻辑
        OnCast();
    }

    // 子类实现具体的技能逻辑 (服务器端执行)
    protected abstract void OnCast();
}
```

## Skill\SkillData.cs

```csharp
using UnityEngine;

[CreateAssetMenu(fileName = "New Skill", menuName = "Game/Skill Data")]
public class SkillData : ScriptableObject
{
    public string skillName;      // UI显示的名字
    public string scriptClassName; // 脚本的类名 (例如: "WitchSkill_Mist")
    public PlayerRole role;
    public Sprite icon;
    [TextArea] public string description;
}
```

## Skill\SkillSelectionManager.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class SkillSelectionManager : MonoBehaviour
{
    [Header("Skill Data")]
    public List<SkillData> allSkills;
    public GameObject buttonPrefab;

    [Header("Witch UI References")]
    public Transform witchButtonContainer;
    public TextMeshProUGUI witchExplainText;

    [Header("Hunter UI References")]
    public Transform hunterButtonContainer;
    public TextMeshProUGUI hunterExplainText;

    [Header("Frame Colors (Base Border)")]
    public Color hunterFrameColor = new Color(0.3f, 0f, 0f); // 深暗红
    public Color witchFrameColor = new Color(0.2f, 0f, 0.3f);  // 深暗紫

    [Header("Highlight Colors (Selected Outline)")]
    public Color hunterSelectedColor = Color.red;         // 鲜红
    public Color witchSelectedColor = new Color(0.7f, 0f, 1f); // 亮紫

    private List<SkillData> currentWitchSelection = new List<SkillData>();
    private List<SkillData> currentHunterSelection = new List<SkillData>();
    private Dictionary<SkillData, Image> skillFrameImages = new Dictionary<SkillData, Image>();

    private void Start()
    {
        currentWitchSelection = allSkills.Where(s => s.role == PlayerRole.Witch).Take(2).ToList();
        currentHunterSelection = allSkills.Where(s => s.role == PlayerRole.Hunter).Take(2).ToList();

        foreach (var skill in allSkills)
        {
            Transform targetContainer = (skill.role == PlayerRole.Witch) ? witchButtonContainer : hunterButtonContainer;
            if (targetContainer == null) continue;

            GameObject go = Instantiate(buttonPrefab, targetContainer);
            go.GetComponentInChildren<TextMeshProUGUI>().text = skill.skillName;
            
            // --- 核心逻辑修改：设置图标到子物体上 ---
            // 假设你的子物体叫 "Icon"
            Transform iconTrans = go.transform.Find("Icon");
            if (iconTrans != null)
            {
                Image iconImg = iconTrans.GetComponent<Image>();
                iconImg.sprite = skill.icon;
                iconImg.preserveAspect = true;
            }

            // 获取根物体的 Image (作为边框)
            Image frameImg = go.GetComponent<Image>();
            frameImg.color = (skill.role == PlayerRole.Hunter) ? hunterFrameColor : witchFrameColor;
            
            SkillButtonUI hoverScript = go.GetComponent<SkillButtonUI>() ?? go.AddComponent<SkillButtonUI>();
            hoverScript.Setup(skill, this);

            go.GetComponent<Button>().onClick.AddListener(() => OnSkillClicked(skill));
            
            skillFrameImages.Add(skill, frameImg);
        }
        
        UpdateVisuals();
        Save();
    }

    // 统一显示逻辑：自动识别角色并更新对应的 Text
    public void ShowDescription(SkillData skill)
    {
        TextMeshProUGUI targetText = (skill.role == PlayerRole.Witch) ? witchExplainText : hunterExplainText;
        
        if (targetText != null)
        {
            string colorHex = (skill.role == PlayerRole.Hunter) ? "#FF4444" : "#BB88FF";
            targetText.text = $"<color={colorHex}><b>{skill.skillName}</b></color>\n{skill.description}";
        }
    }

    private void OnSkillClicked(SkillData skill)
    {
        var selection = (skill.role == PlayerRole.Witch) ? currentWitchSelection : currentHunterSelection;
        if (selection.Contains(skill)) return;

        ShowDescription(skill);
        selection.RemoveAt(0); 
        selection.Add(skill);  
        
        UpdateVisuals();
        Save();
    }

    private void UpdateVisuals()
    {
        foreach (var kvp in skillFrameImages)
        {
            SkillData skill = kvp.Key;
            Image frameImg = kvp.Value; // 根物体的边框图
            GameObject btnGo = frameImg.gameObject;

            bool isSelected = currentWitchSelection.Contains(skill) || currentHunterSelection.Contains(skill);

            // 1. 处理描边 (Outline)
            var outline = btnGo.GetComponent<Outline>() ?? btnGo.AddComponent<Outline>();
            outline.enabled = isSelected;
            
            // 选中时，描边颜色使用亮色系的红/紫
            if (isSelected)
            {
                outline.effectColor = (skill.role == PlayerRole.Hunter) ? hunterSelectedColor : witchSelectedColor;
                outline.effectDistance = new Vector2(5, -5); // 加厚描边
                btnGo.transform.localScale = new Vector3(1.06f, 1.06f, 1f); // 稍微变大
            }
            else
            {
                btnGo.transform.localScale = Vector3.one;
            }

            // 2. 处理边框颜色 (选中时边框也可以稍微亮一点点，或者保持暗色)
            if (isSelected)
            {
                frameImg.color = (skill.role == PlayerRole.Hunter) ? hunterFrameColor * 1.5f : witchFrameColor * 1.5f;
            }
            else
            {
                frameImg.color = (skill.role == PlayerRole.Hunter) ? hunterFrameColor : witchFrameColor;
            }
        }
    }

    private void Save()
    {
        PlayerSettings.Instance.selectedWitchSkillNames = currentWitchSelection.Select(s => s.scriptClassName).ToList();
        PlayerSettings.Instance.selectedHunterSkillNames = currentHunterSelection.Select(s => s.scriptClassName).ToList();
    }
}
```

## Skill\SkillSlotUI.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillSlotUI : MonoBehaviour
{
    [Header("UI References")]
    public Image skillIcon;        // 技能图标
    public Image cooldownOverlay;  // 冷却遮罩 (Fill Type = Radial 360)
    public TextMeshProUGUI keyText; // 按键提示 (Q, E, R)

    public float transparency = 0.5f;

    public void Setup(Sprite icon, string key)
    {
        //Debug.Log($"[UI Debug] Setup called for Key: {key}.");
        if (icon != null) skillIcon.sprite = icon;
        if (keyText != null) keyText.text = key;
        if (cooldownOverlay != null) 
        {
            // 1. 設置與圖標相同的圖片，這樣遮罩的形狀才會跟技能圖標一致
            cooldownOverlay.sprite = skillIcon.sprite;
            
            // 2. 設置顏色為黑色，並調整 Alpha 值 (透明度)
            // Color(R, G, B, A) -> 數值範圍是 0 到 1
            // 0.5f 代表 50% 的透明度
            cooldownOverlay.color = new Color(0f, 0f, 0f, transparency); 

            // 3. 設置填充模式
            cooldownOverlay.type = Image.Type.Filled;
            cooldownOverlay.fillMethod = Image.FillMethod.Radial360;
            cooldownOverlay.fillOrigin = (int)Image.Origin360.Top; // 從正上方開始轉
            
            // 4. 初始化填充比例（0 = 沒冷卻，1 = 全黑遮擋）
            cooldownOverlay.fillAmount = 0;
        }
    }

    public void UpdateCooldown(float ratio)
    {
        if (cooldownOverlay != null)
        {
            cooldownOverlay.fillAmount = ratio;
        }
    }
}
```

## Skill\TrailSnapshot.cs

```csharp
using UnityEngine;

[System.Serializable]
public struct TrailSnapshot
{
    public Vector3 position;
    public Quaternion rotation;
    public int propID; // -1 代表人类形态，>=0 代表变身物品ID
    // 注意：Mirror在序列化结构体时不需要继承NetworkBehaviour，但字段必须是基本类型
}
```

## Skill\WitchTrailRecorder.cs

```csharp
using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class WitchTrailRecorder : NetworkBehaviour
{
    [Header("记录设置")]
    public float recordTimeWindow = 15f; 
    public float recordInterval = 0.5f;

    private LinkedList<TrailSnapshot> snapshots = new LinkedList<TrailSnapshot>();
    private float timer = 0f;
    private WitchPlayer witchPlayer;

    public override void OnStartServer()
    {
        base.OnStartServer(); // 记得调用 base
        witchPlayer = GetComponent<WitchPlayer>();
        Debug.Log($"[Recorder] StartServer on {gameObject.name}. Ready to record.");
    }

    [ServerCallback]
    private void Update()
    {
        // 1. 如果没有 witchPlayer 组件，停止
        if (witchPlayer == null) return;

        timer += Time.deltaTime;

        if (timer >= recordInterval)
        {
            RecordSnapshot();
            timer = 0f;
        }
    }

    [Server]
    private void RecordSnapshot()
    {
        // 如果已经死亡，不记录
        if (witchPlayer.isPermanentDead) return;

        TrailSnapshot snap = new TrailSnapshot
        {
            position = transform.position,
            rotation = transform.rotation,
            propID = witchPlayer.isMorphed ? witchPlayer.morphedPropID : -1
        };

        snapshots.AddLast(snap);

        // 限制队列长度
        int maxSnapshots = Mathf.CeilToInt(recordTimeWindow / recordInterval);
        while (snapshots.Count > maxSnapshots)
        {
            snapshots.RemoveFirst();
        }

        // ★★★ 调试日志：每记录 10 次打印一次，防止刷屏 ★★★
        // if (snapshots.Count % 10 == 0)
        // {
        //     Debug.Log($"[Recorder] {name} recording... Count: {snapshots.Count}. Pos: {transform.position}");
        // }
    }

    [Server]
    public List<TrailSnapshot> GetTrailsInArea(Vector3 center, float radius)
    {
        List<TrailSnapshot> result = new List<TrailSnapshot>();
        float sqrRadius = radius * radius;
        
        // ★★★ 调试日志：显示当前存储了多少个点，以及正在检测的范围 ★★★
        // Debug.Log($"[Recorder] Checking {name} (Total Snapshots: {snapshots.Count}) against center {center} with radius {radius}");

        foreach (var snap in snapshots)
        {
            float distSqr = Vector3.SqrMagnitude(snap.position - center);
            if (distSqr <= sqrRadius)
            {
                result.Add(snap);
            }
        }
        
        if (result.Count == 0 && snapshots.Count > 0)
        {
            // Debug.Log($"[Recorder] {name} has points, but none in range. Closest point dist: {Mathf.Sqrt(GetClosestDistSqr(center))}");
        }

        return result;
    }

    private float GetClosestDistSqr(Vector3 center)
    {
        float min = float.MaxValue;
        foreach (var snap in snapshots)
        {
            float d = Vector3.SqrMagnitude(snap.position - center);
            if (d < min) min = d;
        }
        return min;
    }
}
```

## Skill\Hunter\DogSkillBehavior.cs

```csharp
using UnityEngine;
using Mirror;
using Controller; 

// 【新增】自动添加 LineRenderer 组件
[RequireComponent(typeof(CreatureMover))]
[RequireComponent(typeof(LineRenderer))] 
public class DogSkillBehavior : NetworkBehaviour
{
    [Header("设置")]
    public float detectRadius = 15f; // 检测半径
    public LayerMask targetLayer;    // 目标层级
    public float lifeTime = 10f;     // 存活时间

    [Header("全图追踪设置")]
    public float globalTrackTime = 3f; // 前t秒即使找不到也会往大致方向跑
    public float directionNoiseAngle = 20f; // 大致方向的角度偏移

    [Header("视觉设置")]
    public int segments = 50;        // 圆的平滑度（段数）
    public float lineWidth = 0.2f;   // 线条宽度
    public Color circleColor = Color.red; // 线条颜色

    public float stoppingDistance = 2.0f;

    private CreatureMover mover;
    private bool hasFoundWitch = false;
    private Transform targetWitch;
    
    // LineRenderer 引用
    private LineRenderer lineRenderer;

    private float trackTimer = 0f;
    private float updateDirTimer = 0f;
    private Vector3 trackDirection;

    private void Awake()
    {
        mover = GetComponent<CreatureMover>();
        lineRenderer = GetComponent<LineRenderer>();
        
        // 初始化 LineRenderer 样式
        SetupLineRenderer();
    }

    public override void OnStartServer()
    {
        // 服务器负责销毁
        Destroy(gameObject, lifeTime);
    }

    private void Update()
    {
        // --- 服务器逻辑：负责跑路和检测 ---
        if (isServer)
        {
            ServerUpdateLogic();
        }

        // --- 客户端逻辑：负责画圈圈 ---
        // 只要是客户端（包括 Host 主机）都执行
        if (isClient) 
        {
            DrawDetectionCircle();
        }
    }

    [Server]
    private void ServerUpdateLogic()
    {
        if (mover == null) return;

        if (!hasFoundWitch)
        {
            DetectWitch();
        }

        Vector2 inputAxis = Vector2.zero;
        Vector3 lookTarget = transform.position + transform.forward * 5f; 
        bool isRun = false;

        trackTimer += Time.deltaTime;

        if (targetWitch != null)
        {
            // 在侦测范围内找到了！精确追踪
            float dist = Vector3.Distance(transform.position, targetWitch.position);
            lookTarget = targetWitch.position;

            if (dist > stoppingDistance) 
            {
                inputAxis = new Vector2(0, 1f); 
                isRun = true;
            }
            else
            {
                inputAxis = Vector2.zero;
                isRun = false;
                
                if(!hasFoundWitch) 
                {
                   hasFoundWitch = true; 
                   RpcBarkEffect(targetWitch.position);
                }
            }
        }
        else
        {
            // 没找到，检查是否在 t 秒内执行大致方向追踪
            if (trackTimer <= globalTrackTime)
            {
                updateDirTimer -= Time.deltaTime;
                // 每隔1秒计算一次带噪音的方向，防止目标移动过快丢失
                if (updateDirTimer <= 0f)
                {
                    Transform nearestWitch = GetNearestWitchGlobal();
                    if (nearestWitch != null)
                    {
                        Vector3 dir = (nearestWitch.position - transform.position).normalized;
                        dir.y = 0;
                        if (dir == Vector3.zero) dir = transform.forward;
                        
                        // 加入噪音偏移
                        float noise = UnityEngine.Random.Range(-directionNoiseAngle, directionNoiseAngle);
                        trackDirection = Quaternion.Euler(0, noise, 0) * dir;
                    }
                    else
                    {
                        trackDirection = transform.forward; // 场上没女巫时直走
                    }
                    updateDirTimer = 1.0f; // 重置1秒倒计时
                }
                
                // 朝着带噪音的大致方向跑并看向那里
                lookTarget = transform.position + trackDirection * 5f;
                inputAxis = new Vector2(0, 1f);
                isRun = true;
            }
            else
            {
                // 时间到了也没找到，就普通的往前走
                lookTarget = transform.position + transform.forward * 5f;
                inputAxis = new Vector2(0, 1f); 
                isRun = true;
            }
        }

        mover.SetInput(inputAxis, lookTarget, isRun, false);
    }

    [Server]
    private Transform GetNearestWitchGlobal()
    {
        float minDist = float.MaxValue;
        Transform bestTarget = null;
        foreach (var player in GamePlayer.AllPlayers)
        {
            if (player is WitchPlayer witch && !witch.isPermanentDead && !witch.isInvulnerable)
            {
                float d = Vector3.Distance(transform.position, witch.transform.position);
                if (d < minDist)
                {
                    minDist = d;
                    bestTarget = witch.transform;
                }
            }
        }
        return bestTarget;
    }

    [Server]
    void DetectWitch()
    {
        // 使用 OverlapSphere 检测周围
        Collider[] hits = Physics.OverlapSphere(transform.position, detectRadius, targetLayer);
        float minDist = float.MaxValue;
        Transform bestTarget = null;

        foreach (var hit in hits)
        {
            // 使用 GetComponentInParent 防止遗漏
            WitchPlayer witch = hit.GetComponent<WitchPlayer>() ?? hit.GetComponentInParent<WitchPlayer>();
            
            if (witch != null && !witch.isPermanentDead && !witch.isInvulnerable)
            {
                float d = Vector3.Distance(transform.position, witch.transform.position);
                if (d < minDist)
                {
                    minDist = d;
                    bestTarget = witch.transform;
                }
            }
        }
        
        if (bestTarget != null)
        {
            targetWitch = bestTarget;
        }
    }

    [ClientRpc]
    void RpcBarkEffect(Vector3 pos)
    {
        AudioManager.Instance?.Play3D("dogBarking", pos);
        Debug.Log("Dog: Bark! Found Witch!");
    }

    private void SetupLineRenderer()
    {
        lineRenderer.useWorldSpace = true; // 使用世界坐标，防止狗歪了圈也歪了
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.positionCount = segments + 1; // +1 是为了闭合圆
        lineRenderer.loop = true;
        
        // 设置材质颜色 
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = circleColor;
        lineRenderer.endColor = circleColor;
        
        // 禁用阴影，纯视觉
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
    }

    private void DrawDetectionCircle()
    {
        if (lineRenderer == null) return;

        float angle = 0f;
        float angleStep = 360f / segments;

        for (int i = 0; i < segments + 1; i++)
        {
            float x = Mathf.Sin(Mathf.Deg2Rad * angle) * detectRadius;
            float z = Mathf.Cos(Mathf.Deg2Rad * angle) * detectRadius;

            // 以狗的中心为原点，加上偏移量
            Vector3 pos = new Vector3(x, 0.2f, z) + transform.position;

            lineRenderer.SetPosition(i, pos);

            angle += angleStep;
        }
    }
}
```

## Skill\Hunter\HunterSkill_Dog.cs

```csharp
using UnityEngine;
using Mirror;

public class HunterSkill_Dog : SkillBase
{
    [Header("技能设置")]
    public GameObject dogPrefab; // 拖入刚才做好的 HunterDog
    public float spawnDistance = 1.5f; // 生成在猎人前方多少米

    protected override void OnCast()
    {
        if (dogPrefab == null) return;

        Debug.Log($"<color=green>[Hunter] {ownerPlayer.playerName} used skill: Summon Dog!</color>");

        // 1. 计算生成位置：猎人面前一点点，防止卡在猎人身体里
        Vector3 spawnPos = ownerPlayer.transform.position + ownerPlayer.transform.forward * spawnDistance;

        // 2. 计算朝向：非常重要！
        // 猎人的 transform.rotation 是包含 Y 轴旋转的，直接用这个就可以
        // 这样猎人看向哪里，狗就面朝哪里
        Quaternion spawnRot = ownerPlayer.transform.rotation;

        GameManager.Instance?.ServerPlay3DAt("哨子音", ownerPlayer.transform.position);
        //2D 
        //AudioManager.Instance?.Play2D("哨子音");
        // 3. 生成实例
        GameObject dog = Instantiate(dogPrefab, spawnPos, spawnRot);
        
        // 4. 网络生成
        NetworkServer.Spawn(dog);
        
    }
}
```

## Skill\Hunter\HunterSkill_Scan.cs

```csharp
using UnityEngine;
using Mirror;
using System.Collections;
using System.Collections.Generic;

// 定义一个新结构体，用来打包“某一个女巫”的所有数据
[System.Serializable]
public struct WitchTrailGroup
{
    public Color trailColor;        // 这个女巫的代表色
    public TrailSnapshot[] trails;  // 这个女巫的轨迹点
}
public class HunterSkill_Scan : SkillBase
{
    public enum ScanMode
    {
        Footprints, 
        Ghost       
    }

    [Header("侦察设置")]
    public float scanRadius = 15f; 
    public float visualDuration = 2f; 
    public ScanMode currentMode = ScanMode.Ghost; 

    [Header("视觉过滤")]
    public float minVisualDistance = 1.0f; // 距离小于1米就不生成新的残影模型

    [Header("生成节奏")]
    public float spawnInterval = 0.5f; // 【新增】每个残影之间生成的间隔时间

    
    [Header("渐变设置")]
    [Range(0f, 1f)] public float minAlpha = 0.1f; 
    [Range(0f, 1f)] public float maxAlpha = 0.6f; 

    [Header("视觉资源")]
    public GameObject footprintPrefab;
    public GameObject humanGhostPrefab;
    public Material ghostMaterial;
    public GameObject fireflyParticlePrefab; 

    // Shader 属性 ID
    private static readonly int ColorPropID = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropID = Shader.PropertyToID("_BaseColor");

    protected override void OnCast()
    {
        ServerScanLogic(ownerPlayer.transform.position);
    }

    [Server] 
    private void ServerScanLogic(Vector3 center)
    {
        //  创建一个组的列表, 每个组包含一个女巫的所有轨迹和一个独特的颜色
        List<WitchTrailGroup> allGroups = new List<WitchTrailGroup>();

        foreach (var player in GamePlayer.AllPlayers)
        {
            if (player is WitchPlayer witch && !witch.isPermanentDead)
            {
                var recorder = witch.GetComponent<WitchTrailRecorder>();
                if (recorder != null)
                {
                    // 获取单个女巫的轨迹
                    var trailsList = recorder.GetTrailsInArea(center, scanRadius);
                    
                    if (trailsList.Count > 0)
                    {
                        // 生成唯一颜色 

                        Color uniqueColor = GetWitchColor(witch);

                        // 打包成组
                        WitchTrailGroup group = new WitchTrailGroup
                        {
                            trailColor = uniqueColor,
                            trails = trailsList.ToArray()
                        };
                        
                        allGroups.Add(group);
                    }
                }
            }
        }

        //Debug.Log($"[Server] Scan found {allGroups.Count} witch groups.");

        // 发送组数据
        NetworkConnection targetConn = ownerPlayer.connectionToClient;
        if (targetConn != null)
        {
            TargetShowTrails(targetConn, allGroups.ToArray());
        }
        else if (ownerPlayer.isLocalPlayer) 
        {
            //howTrailsLocal(allGroups.ToArray());
            StartCoroutine(SpawnTrailsSequentially(allGroups.ToArray()));
        }
    }

    // 辅助函数：获取女巫颜色
    private Color GetWitchColor(WitchPlayer witch)
    {
        // 使用 NetID 作为种子，确保同一个玩家每次被扫描颜色都一样
        Random.InitState((int)witch.netId);
        // 生成鲜艳的颜色 (Saturation 和 Value 调高)
        return Random.ColorHSV(0f, 1f, 0.8f, 1f, 0.8f, 1f);
    }

    [TargetRpc]
    private void TargetShowTrails(NetworkConnection target, WitchTrailGroup[] groups)
    {
        //ShowTrailsLocal(groups);
        StartCoroutine(SpawnTrailsSequentially(groups));
    }


    private IEnumerator SpawnTrailsSequentially(WitchTrailGroup[] groups)
    {
        if (groups.Length == 0) yield break;

        // 1. 找到所有女巫中最长的一条轨迹长度
        int maxTrails = 0;
        foreach (var group in groups)
        {
            if (group.trails.Length > maxTrails)
                maxTrails = group.trails.Length;
        }

        // 2. 【新增】为每个女巫准备独立的状态追踪器
        Vector3[] lastSpawnedPos = new Vector3[groups.Length];
        int[] lastPropIDs = new int[groups.Length];
        int[] stackedCounts = new int[groups.Length];

        // 初始化追踪器
        for (int w = 0; w < groups.Length; w++)
        {
            lastSpawnedPos[w] = new Vector3(9999f, 9999f, 9999f); // 初始设为极远的点
            lastPropIDs[w] = -999;
            stackedCounts[w] = 0;
        }

        // 3. 按时间顺序逐个遍历
        for (int i = 0; i < maxTrails; i++)
        {
            bool spawnedAny = false;

            // 同时遍历所有女巫
            for (int w = 0; w < groups.Length; w++)
            {
                var group = groups[w];
                
                // 如果这个女巫在当前时间节点有痕迹
                if (i < group.trails.Length)
                {
                    TrailSnapshot currentSnap = group.trails[i];

                    // 【核心过滤逻辑】计算与上一次生成点的距离
                    float distSqr = Vector3.SqrMagnitude(currentSnap.position - lastSpawnedPos[w]);
                    // 判断变身形态是否发生了改变（即使在原地，只要变身了也应该生成新残影）
                    bool propChanged = currentSnap.propID != lastPropIDs[w];

                    // 如果距离大于阈值，或者形态改变了，才生成！
                    if (distSqr >= (minVisualDistance * minVisualDistance) || propChanged)
                    {
                        GameObject spawnedObj = null;

                        if (currentMode == ScanMode.Footprints)
                            spawnedObj = SpawnFootprint(currentSnap, group.trailColor);
                        else
                            spawnedObj = SpawnGhost(currentSnap, group.trailColor);

                        // 【进阶视觉表现】如果女巫在之前的位置蹲了很久（重叠次数多），可以把这个新残影放大！
                        if (spawnedObj != null && stackedCounts[w] >= 4)
                        {
                            // 例如：原地呆了 4 个快照(2秒)以上，残影变大 1.3 倍，提示猎人她在这里龟缩过
                            spawnedObj.transform.localScale *= 1.3f;
                        }

                        // 更新追踪器状态
                        lastSpawnedPos[w] = currentSnap.position;
                        lastPropIDs[w] = currentSnap.propID;
                        stackedCounts[w] = 0; // 重置重叠计数

                        spawnedAny = true;
                    }
                    else
                    {
                        // 如果距离太近（原地发呆），不生成模型，但增加重叠计数
                        stackedCounts[w]++;
                    }
                }
            }

            // 只要这一步生成了任何东西，就等待一段时间再生成下一个
            if (spawnedAny)
            {
                yield return new WaitForSeconds(spawnInterval);
            }
        }
    }

    
    private GameObject SpawnFootprint(TrailSnapshot trail, Color color)
    {
        if (footprintPrefab == null) return null;
        GameObject fp = Instantiate(footprintPrefab, trail.position + Vector3.up * 0.1f, trail.rotation);
        
        SetupFireflyVisual(fp, color);
        return fp; // 返回生成的对象
    }

   
    private GameObject SpawnGhost(TrailSnapshot trail, Color color)
    {
        GameObject ghostObj = null;

        if (trail.propID >= 0)
        {
            if (PropDatabase.Instance != null && PropDatabase.Instance.GetPropPrefab(trail.propID, out GameObject prefab))
            {
                ghostObj = Instantiate(prefab, trail.position, trail.rotation);
                CleanupGhostObject(ghostObj);
            }
        }
        else 
        {
            if (humanGhostPrefab != null)
            {
                ghostObj = Instantiate(humanGhostPrefab, trail.position, trail.rotation);
                CleanupGhostObject(ghostObj); 
            }
        }

        if (ghostObj != null)
        {
            SetupFireflyVisual(ghostObj, color);
        }

        return ghostObj; // 返回生成的对象
    }

    



    // private IEnumerator SpawnTrailsSequentially(WitchTrailGroup[] groups)
    // {
    //     if (groups.Length == 0) yield break;

    //     // 找到所有女巫中最长的一条轨迹长度
    //     int maxTrails = 0;
    //     foreach (var group in groups)
    //     {
    //         if (group.trails.Length > maxTrails)
    //             maxTrails = group.trails.Length;
    //     }

    //     // 按时间顺序（从最老的点到最新的点）逐个遍历
    //     for (int i = 0; i < maxTrails; i++)
    //     {
    //         bool spawnedAny = false;

    //         // 同时遍历所有女巫，确保她们的痕迹是同步向前推进的
    //         foreach (var group in groups)
    //         {
    //             // 如果这个女巫在当前时间节点有痕迹，则生成
    //             if (i < group.trails.Length)
    //             {
    //                 if (currentMode == ScanMode.Footprints)
    //                     SpawnFootprint(group.trails[i], group.trailColor);
    //                 else
    //                     SpawnGhost(group.trails[i], group.trailColor);

    //                 spawnedAny = true;
    //             }
    //         }

    //         // 只要这一步生成了任何东西，就等待一段时间再生成下一个
    //         if (spawnedAny)
    //         {
    //             // 越靠近最新的点，间隔可以越短，表现出追踪的紧迫感（可选）
    //             yield return new WaitForSeconds(spawnInterval);
    //         }
    //     }
    // }

    // private void SpawnFootprint(TrailSnapshot trail, Color color)
    // {
    //     if (footprintPrefab == null) return;
    //     GameObject fp = Instantiate(footprintPrefab, trail.position + Vector3.up * 0.1f, trail.rotation);
        
    //     SetupFireflyVisual(fp, color);
    // }

    // private void SpawnGhost(TrailSnapshot trail, Color color)
    // {
    //     GameObject ghostObj = null;

    //     if (trail.propID >= 0)
    //     {
    //         if (PropDatabase.Instance != null && PropDatabase.Instance.GetPropPrefab(trail.propID, out GameObject prefab))
    //         {
    //             ghostObj = Instantiate(prefab, trail.position, trail.rotation);
    //             CleanupGhostObject(ghostObj);
    //         }
    //     }
    //     else 
    //     {
    //         if (humanGhostPrefab != null)
    //         {
    //             ghostObj = Instantiate(humanGhostPrefab, trail.position, trail.rotation);
    //             CleanupGhostObject(ghostObj); 
    //         }
    //     }

    //     if (ghostObj != null)
    //     {
    //         SetupFireflyVisual(ghostObj, color);
    //     }
    // }

    private void CleanupGhostObject(GameObject obj)
    {
        foreach (var c in obj.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (var rb in obj.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
        foreach (var script in obj.GetComponentsInChildren<MonoBehaviour>()) Destroy(script);
        foreach (var ps in obj.GetComponentsInChildren<ParticleSystem>()) Destroy(ps);
        foreach (var anim in obj.GetComponentsInChildren<Animator>()) Destroy(anim);
        
        obj.layer = LayerMask.NameToLayer("Ignore Raycast");
        foreach(Transform t in obj.GetComponentsInChildren<Transform>()) 
            t.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");
    }

    private void SetupFireflyVisual(GameObject obj, Color fireflyColor)
    {
        // 1. 替换为黑影材质
        if (ghostMaterial != null)
        {
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                Material[] newMats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < newMats.Length; i++) newMats[i] = ghostMaterial;
                r.sharedMaterials = newMats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // 2. 动态添加特效脚本，并把粒子预制体传给它
        TrailFireflyEffect effect = obj.AddComponent<TrailFireflyEffect>();
        effect.Setup(fireflyColor, visualDuration, fireflyParticlePrefab); // 【修改】多传一个参数
    }


}
```

## Skill\Hunter\HunterSkill_Shockwave.cs

```csharp
using UnityEngine;
using Mirror;
//using System.Diagnostics;


public class HunterSkill_Shockwave : SkillBase
{
    public float radius = 8f;
    public GameObject vfxPrefab; // 震地特效

    public bool hitAnyWitch = false; // 是否命中至少一个女巫s
    protected override void OnCast()
    {
        hitAnyWitch = false;
        RpcPlayVFX();
        GameManager.Instance?.ServerPlay3DAt("shockwave砸地", ownerPlayer.transform.position);

        Collider[] hits = Physics.OverlapSphere(ownerPlayer.transform.position, radius);
        Debug.Log($"<color=green>[Hunter] {ownerPlayer.playerName} used skill: Shockwave! Affected {hits.Length} targets.</color>");
        bool sentHitFeedback = false;
        foreach (var hit in hits)
        {
            // 找到女巫
            WitchPlayer witch = hit.GetComponent<WitchPlayer>();
            if (witch == null) {
                continue;
            }
            else
            {
                Debug.Log($"[Hunter] Found witch: {witch.playerName}");
            }

            if (!witch.isPermanentDead)
            {
                // 1. 强制显形
                if (witch.isMorphed)
                {
                    // 调用女巫现有的 Revert 命令逻辑
                    // 由于这是服务器端，我们不能调用 Cmd，需要把 CmdRevert 的逻辑拆分出一个 ServerRevert
                    // 或者我们这里简单暴力点，直接修改变量并调用 ApplyRevert
                    witch.isMorphed = false;
                    witch.morphedPropID = -1;
                    // 通过 Rpc 通知女巫客户端 (WitchPlayer 需要对应修改 OnMorphedPropIDChanged 钩子来处理逻辑)
                    // 目前代码里 OnMorphedPropIDChanged 已经处理了 ApplyRevert
                }

                // 2. 减速 (需要给 GamePlayer 加个 StatusEffect 系统，这里简化直接改速度，3秒后改回)
                StartCoroutine(SlowDownWitch(witch));

                // 标记命中
                hitAnyWitch = true;

                if (hitAnyWitch && !sentHitFeedback)
                {
                    // 【核心修复】获取安全的连接对象
                    // 如果 connectionToClient 为空（即 Host），则尝试使用 NetworkServer.localConnection
                    NetworkConnection targetConn = ownerPlayer.connectionToClient;

                    // 如果是 Host 模式，connectionToClient 可能为 null，需要特殊处理
                    if (targetConn == null && ownerPlayer.isLocalPlayer)
                    {
                        // 如果是 Host 自己释放技能，直接在本地打印日志或调用 UI，不走 RPC
                        AudioManager.Instance?.Play2D("叮");
                        Debug.Log("<color=yellow>[Host] Shockwave hit a witch!</color>");
                        // 你也可以直接调用本地 UI 函数，例如：
                        // SceneScript.Instance.ShowHitFeedback();
                        sentHitFeedback = true;
                    }
                    else if (targetConn != null)
                    {
                        // 如果是远程客户端，正常发送 TargetRpc
                        TargetHitFeedback(targetConn);
                        sentHitFeedback = true;
                    }
                }
            }
        }
    }

    [TargetRpc]
    void TargetHitFeedback(NetworkConnection conn)
    {
        AudioManager.Instance?.Play2D("叮");
        // UI 显示 "Hit!"
        Debug.Log("<color=yellow>[Hunter] Shockwave hit a witch!</color>");
    }

    [ClientRpc]
    void RpcPlayVFX()
    {
        if (vfxPrefab) Instantiate(vfxPrefab, transform.position, Quaternion.identity);
        else Debug.LogWarning("[HunterSkill_Shockwave] VFX Prefab is not assigned!");
    }

    [Server]
    System.Collections.IEnumerator SlowDownWitch(WitchPlayer witch)
    {
        float originalSpeed = witch.moveSpeed;
        witch.moveSpeed = 2f; // 极慢
        yield return new WaitForSeconds(3f);
        witch.moveSpeed = originalSpeed;
    }
}
```

## Skill\Hunter\HunterSkill_Trap.cs

```csharp
using UnityEngine;
using Mirror;

public class HunterSkill_Trap : SkillBase
{
    public GameObject trapPrefab;
    
    [Header("放置设置")]
    public float yOffset = 0.05f; 
    public float placeDistance = 1.5f; // 将距离提取为变量
    public float maxGroundCheckDistance = 10f; // 射线向下检测的最大距离

    protected override void OnCast()
    {
        HunterPlayer hunter = ownerPlayer as HunterPlayer;
        if (hunter == null) return;

        Debug.Log($"<color=green>[Hunter] {ownerPlayer.playerName} used skill: Place Trap!</color>");

        if (trapPrefab == null)
        {
            trapPrefab = Resources.Load<GameObject>("Prefabs/HunterTrap");
        }

        // 1. 计算目标水平坐标 (忽略 Y 轴的变化，只取 X 和 Z)
        // 这样即使猎人抬头看天，陷阱也不会试图放到天上，而是水平前方
        Vector3 forwardFlat = hunter.transform.forward;
        forwardFlat.y = 0; 
        forwardFlat.Normalize();
        
        // 初始目标点（此时 Y 值依然是猎人的脚底高度，如果在空中，这个 Y 很高）
        Vector3 potentialPos = hunter.transform.position + forwardFlat * placeDistance;

        // 2. 准备射线检测
        // 从目标点上方一点开始向下射，确保能覆盖略微不平的地面
        // 如果在空中跳跃，startPos 的 Y 会很高，向下射 50 米通常能碰到地
        Vector3 rayStart = potentialPos + Vector3.up * 1.0f; 
        
        // 【Debug】在 Scene 窗口画出射线 (红色=未命中，绿色=命中)
        // 游戏运行时去 Scene 窗口看一眼，能不能看到这条红线
        Debug.DrawRay(rayStart, Vector3.down * maxGroundCheckDistance, Color.red, 3.0f);

        // 3. 进行射线检测
        // 注意：建议在 Inspector 中检查 hunter.groundLayer，确保它【不包含】Player 层
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, maxGroundCheckDistance, hunter.groundLayer))
        {
            // --- 情况 A：检测到地面 ---
            
            // 再次确认没有打到自己（如果 GroundLayer 设置得当，这步其实是多余的，但为了保险）
            if (hit.collider.gameObject == hunter.gameObject)
            {
                Debug.LogWarning("Trap placement failed: Raycast hit the player itself. Check GroundLayer!");
                return;
            }

            // 修正生成位置为打击点 + 偏移
            Vector3 finalSpawnPos = hit.point + Vector3.up * yOffset;

            // 【Debug】画出命中位置
            Debug.DrawLine(rayStart, hit.point, Color.green, 3.0f);

            try
            {
                // 保持陷阱水平旋转（不随地面倾斜），或者你可以使用 Quaternion.FromToRotation 让陷阱贴合斜坡
                Quaternion trapRotation = Quaternion.Euler(0, hunter.transform.eulerAngles.y, 0);
                
                GameObject trap = Instantiate(trapPrefab, finalSpawnPos, trapRotation);
                NetworkServer.Spawn(trap);
                GameManager.Instance?.ServerPlay3DAt("机械click音陷阱用", finalSpawnPos);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Exception during Instantiate: {e.Message}");
            }
        }
        else
        {
            // --- 情况 B：未检测到地面 (悬崖外或跳得太高超过检测距离) ---
            Debug.LogWarning("无法放置陷阱：下方未检测到地面 (Too high or void)");
            
            // 这里我们直接 return，不再生成陷阱，从而彻底解决“浮空陷阱”的问题
            // 如果你希望即使在空中也生成（类似于丢出去），则在这里写 else 逻辑，但通常陷阱需要贴地。
        }
    }
}
```

## Skill\Hunter\TrailFireflyEffect.cs

```csharp
using UnityEngine;
using System.Collections;

public class TrailFireflyEffect : MonoBehaviour
{
    private Color themeColor;
    private float lifeTime;
    private Renderer[] renderers;
    private MaterialPropertyBlock propBlock;
    
    // Shader 属性
    private static readonly int ColorPropID = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropID = Shader.PropertyToID("_BaseColor");

    private float spawnTime;
    private GameObject particleInstance; // 保存生成的粒子实例

    // 【修改】接收传进来的 Prefab
    public void Setup(Color fireflyColor, float duration, GameObject particlePrefab)
    {
        themeColor = fireflyColor;
        lifeTime = duration;
        renderers = GetComponentsInChildren<Renderer>();
        propBlock = new MaterialPropertyBlock();
        spawnTime = Time.time;

        // 【新增】直接使用预制体生成
        if (particlePrefab != null)
        {
            // 在当前黑影的位置生成你调好的粒子
            particleInstance = Instantiate(particlePrefab, transform);
            particleInstance.transform.localPosition = Vector3.up * 1.0f; // 稍微抬高到身体中心

            // 获取粒子组件，覆盖颜色为你算好的女巫专属色
            ParticleSystem ps = particleInstance.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                // 先强行停止，防止因为预制体自带 PlayOnAwake 导致报错
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                
                // 仅覆盖颜色，大小/速度/拖尾全按你在编辑器里调的算
                var main = ps.main;
                main.startColor = themeColor; 

                // 重新播放
                ps.Play();
            }
        }

        // 开启生命周期协程
        StartCoroutine(LifeRoutine());
    }

    private void Update()
    {
        if (renderers == null || renderers.Length == 0) return;

        // 【黑影闪烁逻辑】使用正弦波制造“一闪一闪”的呼吸感 (频率 8.0f 可自己调)
        float pulse = Mathf.Abs(Mathf.Sin((Time.time - spawnTime) * 8.0f)); 
        
        // 基础透明度 0.1，最高闪烁到 0.4
        float currentAlpha = Mathf.Lerp(0.1f, 0.4f, pulse);

        // 如果快要消失了，整体淡出
        float timeLeft = (spawnTime + lifeTime) - Time.time;
        if (timeLeft < 1.0f) 
        {
            currentAlpha *= timeLeft; // 最后一秒渐渐消失
        }

        foreach (var r in renderers)
        {
            r.GetPropertyBlock(propBlock);

            Color shadowColor = themeColor * 0.2f; 
            shadowColor.a = currentAlpha;

            propBlock.SetColor(ColorPropID, shadowColor);
            propBlock.SetColor(BaseColorPropID, shadowColor);
            r.SetPropertyBlock(propBlock);
        }
    }

    private IEnumerator LifeRoutine()
    {
        // 存活指定时间后销毁自己（由于粒子是子物体，会跟着一起销毁）
        yield return new WaitForSeconds(lifeTime);
        Destroy(gameObject);
    }
}
```

## Skill\Hunter\TrapBehavior.cs

```csharp
using UnityEngine;
using Mirror;
using System.Collections;

[RequireComponent(typeof(Rigidbody))] // 确保有刚体
public class TrapBehavior : NetworkBehaviour
{
    [Header("视觉/高亮设置")]
    public PlayerOutline outlineScriptopen; 
    public PlayerOutline outlineScriptclose; 
    public Color hunterHighlightColor = new Color(0.5f, 0f, 0f);

    [Header("模型切换设置")]
    public GameObject openModel;   
    public GameObject closedModel; 
    public Animator trapAnimator;  

    [Header("设置")]
    public float destroyDelay = 5.0f; 

    [SyncVar(hook = nameof(OnTriggeredChanged))]
    public bool isTriggered = false;

    private Renderer[] myRenderers;
    private Rigidbody rb; // 引用刚体

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        myRenderers = GetComponentsInChildren<Renderer>(true);
        
        // 初始化刚体状态
        if (rb != null)
        {
            rb.isKinematic = true; // 建议放置时先设为 Kinematic，防止被撞飞
            rb.useGravity = false;
        }

        if (openModel != null)
        {
            openModel.SetActive(true);
            Collider childCol = openModel.GetComponent<Collider>();
            if (childCol != null)
            {
                childCol.isTrigger = true;
                if (childCol is MeshCollider meshCol) meshCol.convex = true; 
            }
        }

        if (closedModel != null) closedModel.SetActive(false);
    }

    public override void OnStartClient()
    {
        UpdateModelState(isTriggered);
        RefreshVisibility();
    }

    private void OnTriggeredChanged(bool oldVal, bool newVal)
    {
        UpdateModelState(newVal);
        RefreshVisibility();
    }

    private void UpdateModelState(bool triggered)
    {
        if (openModel != null) openModel.SetActive(!triggered);
        if (closedModel != null) 
        {
            bool wasActive = closedModel.activeSelf;
            closedModel.SetActive(triggered);
            if (triggered && !wasActive && trapAnimator != null)
            {
                trapAnimator.SetTrigger("Snap");
            }
        }
    }

    private void RefreshVisibility()
    {
        GamePlayer localPlayer = NetworkClient.localPlayer?.GetComponent<GamePlayer>();
        if (localPlayer == null) return;
        bool isHunter = (localPlayer.playerRole == PlayerRole.Hunter);
        foreach (var r in myRenderers) r.enabled = isTriggered || isHunter;

        if (outlineScriptopen)
        {
            if (isTriggered) outlineScriptopen.SetOutline(false, Color.clear);
            else if (isHunter) outlineScriptopen.SetOutline(true, hunterHighlightColor);
        }
        if (outlineScriptclose)
        {
            if (!isTriggered) outlineScriptclose.SetOutline(false, Color.clear);
            else if (isHunter) outlineScriptclose.SetOutline(true, hunterHighlightColor);
        }
    }

    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        if (isTriggered) return;

        WitchPlayer witch = other.GetComponent<WitchPlayer>() ?? other.GetComponentInParent<WitchPlayer>();
        
        if (witch != null && !witch.isPermanentDead && !witch.isInvulnerable)
        {
            isTriggered = true; 

            // --- 物理层面移动方案 ---
            Vector3 targetPos = witch.transform.position;
            GameManager.Instance?.ServerPlay3DAt("机械click音陷阱用", targetPos);

            // 1. 先把刚体设为 Kinematic，这样它就不会被物理引擎推走或卡住
            rb.isKinematic = true; 
            
            // 2. 使用 rb.position 强制更改物理坐标
            rb.position = targetPos;
            
            // 3. 同时更改 transform.position (双重保险)
            transform.position = targetPos;

            // 4. 调用 ClientRpc，确保所有客户端立即看到瞬移效果
            RpcSnapToPosition(targetPos);

            // --- 游戏逻辑 ---
            witch.ServerGetTrappedByTrap(this.netId); 
            
            if (witch.isMorphed)
            {
                witch.isMorphed = false;
                witch.morphedPropID = -1;
            }
            StartCoroutine(DestroyAfterDelay());
        }
    }

    // 通过 RPC 强制客户端同步物理位置
    [ClientRpc]
    private void RpcSnapToPosition(Vector3 newPos)
    {
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.position = newPos;
        }
        transform.position = newPos;
    }

    [Server]
    private System.Collections.IEnumerator DestroyAfterDelay()
    {
        yield return new WaitForSeconds(destroyDelay);
        NetworkServer.Destroy(gameObject);
    }
}
```

## Skill\Witch\CursedTreeTrigger.cs

```csharp
using UnityEngine;
using Mirror;

public class CursedTreeTrigger : NetworkBehaviour
{
    [SyncVar] public uint casterNetId;

    // 当这棵树受到伤害时调用 (需要修改 WeaponBase 或 GunWeapon 来检测这个组件)
    [Server]
    public void OnHitByHunter(HunterPlayer hunter)
    {
        // 触发惩罚：致盲 hunter
        hunter.TargetBlindEffect(hunter.connectionToClient, 3f);
        
        // 触发后移除诅咒 (是一次性的)
        Destroy(this);
    }
}
```

## Skill\Witch\DecoyBehavior.cs

```csharp
using UnityEngine;
using Mirror;

[RequireComponent(typeof(CharacterController))]
public class DecoyBehavior : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float lifeTime = 10f; // 分身存活时间
    public float moveSpeed = 5f; // 移动速度（最好和女巫走路速度一致）
    public float gravity = -9.81f; // 重力

    [Header("Sync Settings")]
    [SyncVar(hook = nameof(OnPropIDChanged))]
    public int propID = -1;
    [Header("Visual References")]
    public GameObject humanVisualRoot; // 在 Inspector 中拖入预制体的人形模型根物体
    // 内部变量
    private CharacterController cc;
    private Vector3 moveDir;
    private float verticalVelocity; // 垂直速度（处理重力）
    private float jitterTimer = 0f; // 随机转向计时器
    public Animator animator;

    [SyncVar]
    private float syncedSpeed; // 同步速度值，确保所有客户端动画一致
    private void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    public override void OnStartServer()
    {
        // 服务器端初始化
        // 初始方向：就是生成的朝向
        moveDir = transform.forward;
        
        // 销毁计时
        Destroy(gameObject, lifeTime);
        // 服务器端初始化：如果是人形态，立即校准一次物理中心
        if (propID == -1 && humanVisualRoot != null)
        {
            UpdateColliderDimensions(humanVisualRoot);
        }
    }

    [ServerCallback]
    private void Update()
    {
        if (cc == null) return;

        // 1. 处理随机转向 (模拟玩家的不规则移动)
        jitterTimer += Time.deltaTime;
        if (jitterTimer > 1.0f) // 每秒可能微调一次方向
        {
            // 随机偏转 -45 到 45 度，模拟玩家转弯
            float jitter = Random.Range(-45f, 45f);
            Quaternion turn = Quaternion.AngleAxis(jitter, Vector3.up);
            moveDir = turn * moveDir;
            jitterTimer = 0;
        }

        // 2. 处理重力
        if (cc.isGrounded && verticalVelocity < 0)
        {   
            verticalVelocity = -2f; // 贴地力
        }
        else
        {   
            Debug.Log("Applying gravity");
            verticalVelocity += gravity * Time.deltaTime;
        }

        // 3. 最终移动向量
        // 水平速度
        Vector3 finalMove = moveDir.normalized * moveSpeed;
        // 叠加垂直速度
        finalMove.y = verticalVelocity;

        // 4. 执行移动 (利用 CharacterController 的碰撞处理)
        cc.Move(finalMove * Time.deltaTime);
        // 【新增】在服务器端计算水平速度并赋值给同步变量
        // cc.velocity 会根据实际物理移动返回速度
        syncedSpeed = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;
        // 5. 让模型朝向移动方向
        // 只取水平方向，防止分身朝向地面或天空
        Vector3 faceDir = new Vector3(moveDir.x, 0, moveDir.z);
        if (faceDir != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(faceDir), Time.deltaTime * 5f);
        }
    }
    // 注意：这里去掉了 [ServerCallback]，让所有客户端都运行
    private void LateUpdate()
    {
        // 所有客户端根据同步过来的 syncedSpeed 来更新自己的动画机
        UpdateAnimator();
    }

    // 【新增方法】更新动画参数
    private void UpdateAnimator()
    {
        if (animator == null)
        {
            // 如果当前是人形态，尝试从 humanVisualRoot 获取
            if (propID == -1 && humanVisualRoot != null)
                animator = humanVisualRoot.GetComponentInChildren<Animator>();
            // 如果是变身形态，动画器在生成的子物体上，会在 OnPropIDChanged 中处理
        }

        if (animator != null)
        {
            // 这里的 "speed" 必须对应你在 Animator 窗口创建的参数名（小写）
            animator.SetFloat("speed", syncedSpeed);
        }
    }
    // --- 视觉同步逻辑 (保持不变) ---
    void OnPropIDChanged(int oldID, int newID)
    {
        animator = null; // 重置引用，迫使 Update 重新获取新的动画器
        // 1. 清理旧的变身模型 (保留人形根物体和FX)
        foreach (Transform child in transform) {
            if (child.gameObject != humanVisualRoot && child.name != "FX")
                Destroy(child.gameObject);
        }

        if (newID == -1)
        {
            // --- 恢复人形态 ---
            if (humanVisualRoot != null)
            {
                humanVisualRoot.SetActive(true);
                animator = humanVisualRoot.GetComponentInChildren<Animator>(); // 重新获取巫师动画器
                UpdateColliderDimensions(humanVisualRoot);
            }
        }
        else
        {
            // --- 变身形态 ---
            if (humanVisualRoot != null) humanVisualRoot.SetActive(false);

            if (PropDatabase.Instance != null && PropDatabase.Instance.GetPropPrefab(newID, out GameObject prefab))
            {
                GameObject visual = Instantiate(prefab, transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                // 如果变身的物体（如小动物）带有 Animator，这里获取它
                animator = visual.GetComponent<Animator>();                 
                foreach(var c in visual.GetComponentsInChildren<Collider>()) c.enabled = false;

                var pt = GetComponent<PropTarget>();
                if (pt != null) pt.ManualInit(newID, visual);

                UpdateColliderDimensions(visual);
            }
        }
    }

    // 【新增】辅助方法：调整碰撞体大小
    private void UpdateColliderDimensions(GameObject visualModel)
    {
        if (cc == null) cc = GetComponent<CharacterController>();

        // 1. 获取模型下所有的渲染器（包括人形态和变身形态）
        Renderer[] rs = visualModel.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;

        // 2. 关键：计算本地坐标系的包围盒
        // 我们要找到模型相对于分身根节点(Pivot)的最高点和最低点
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        float maxSide = 0f;

        bool foundRenderer = false;
        foreach (var r in rs)
        {
            if (r is ParticleSystemRenderer) continue; // 忽略粒子
            
            // 将世界空间的 Bounds 转为本地空间的相对坐标
            // 使用 transform.InverseTransformPoint 确保不受生成位置影响
            Bounds b = r.bounds;
            Vector3 localMin = transform.InverseTransformPoint(b.min);
            Vector3 localMax = transform.InverseTransformPoint(b.max);

            minY = Mathf.Min(minY, localMin.y);
            maxY = Mathf.Max(maxY, localMax.y);
            
            // 计算半径
            float sideX = Mathf.Max(Mathf.Abs(localMin.x), Mathf.Abs(localMax.x));
            float sideZ = Mathf.Max(Mathf.Abs(localMin.z), Mathf.Abs(localMax.z));
            maxSide = Mathf.Max(maxSide, sideX, sideZ);
            
            foundRenderer = true;
        }

        if (!foundRenderer) return;

        // 3. 计算物理参数
        float height = maxY - minY;
        // 中心点应该在 minY 和 maxY 的正中间
        float centerY = (minY + maxY) / 2f;

        // 4. 安全应用参数
        cc.enabled = false; // 修改前必须禁用
        
        cc.height = height;
        cc.center = new Vector3(0, centerY, 0);
        cc.radius = Mathf.Clamp(maxSide, 0.2f, 0.5f); // 限制半径范围
        
        // 自动调整踏步高度，防止小动物卡在小石子上
        cc.stepOffset = Mathf.Min(0.3f, height * 0.3f);

        cc.enabled = true;
        
        Debug.Log($"[Decoy] Adjusting CC: Height={height}, CenterY={centerY}, Morphed={propID != -1}");
    }
}
```

## Skill\Witch\MistBehavior.cs

```csharp
using UnityEngine;
using Mirror;

public class MistBehavior : NetworkBehaviour
{
    [Header("迷雾设置")]
    public float lifeTime = 5.0f;       // 迷雾存在时间
    public float blindRefreshRate = 0.5f; // 致盲刷新频率（每0.5秒刷新一次致盲状态）
    public float blindDuration = 1.0f;    // 单次致盲持续时间（离开迷雾后多久恢复）

    private float nextCheckTime = 0f;

    public override void OnStartServer()
    {
        // 服务器端负责销毁
        Destroy(gameObject, lifeTime);
    }

    [ServerCallback]
    private void OnTriggerStay(Collider other)
    {
        // 性能优化：限制检测频率
        if (Time.time < nextCheckTime) return;

        // 获取目标
        HunterPlayer hunter = other.GetComponent<HunterPlayer>() ?? other.GetComponentInParent<HunterPlayer>();

        if (hunter != null)
        {
            // 确保猎人活着且没有无敌
            if (!hunter.isPermanentDead && !hunter.isInvulnerable)
            {
                // 获取连接并发送致盲 RPC
                // 注意：TargetBlindEffect 已经在 HunterPlayer.cs 中定义好了
                if (hunter.connectionToClient != null)
                {
                    hunter.TargetBlindEffect(hunter.connectionToClient, blindDuration);
                    Debug.Log($"[Mist] Blinding Hunter: {hunter.playerName}");
                }
            }
        }
        
        // 重置检测计时器（简单的频率限制，防止每帧调用 RPC 导致带宽爆炸）
        nextCheckTime = Time.time + blindRefreshRate;
    }
}
```

## Skill\Witch\WitchSkill_Chaos.cs

```csharp
using UnityEngine;
using Mirror;
using System.Collections;

public class WitchSkill_Chaos : SkillBase
{
    public float radius = 15f;
    public float duration = 5f;
    public float pushForce = 15f; // 【新增】撞击猎人的力度

    protected override void OnCast()
    {
        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used skill: Chaos! Disturbing nearby trees.</color>");
        GameManager.Instance?.ServerPlay3DAt("地动声", ownerPlayer.transform.position);

        // 找到周围的普通树
        Collider[] hits = Physics.OverlapSphere(ownerPlayer.transform.position, radius);
        foreach (var hit in hits)
        {
            PropTarget prop = hit.GetComponentInParent<PropTarget>();
            if (prop != null && !prop.isAncientTree && prop.isStaticTree)
            {
                // 开启协程让它们乱动
                StartCoroutine(ChaosRoutine(prop.transform));
            }
        }
    }

    [Server]
    IEnumerator ChaosRoutine(Transform treeTrans)
    {
        float timer = 0f;
        Vector3 originalPos = treeTrans.position;
        Quaternion originalRot = treeTrans.rotation;
        
        // 分配一个随机种子，让每棵树扭动的频率和路径不同
        float randomSeed = Random.Range(0f, 100f);

        while (timer < duration)
        {
            timer += Time.deltaTime;
            
            // 1. 更加剧烈和多方位的空间位移
            float timeParam = Time.time * 20f + randomSeed; // 加快晃动频率
            float offsetX = Mathf.Sin(timeParam) * 1.5f + Mathf.PerlinNoise(timeParam, 0) * 2f - 1f;
            float offsetZ = Mathf.Cos(timeParam * 1.2f) * 1.5f + Mathf.PerlinNoise(0, timeParam) * 2f - 1f;
            float offsetY = Mathf.Abs(Mathf.Sin(timeParam * 2f)) * 0.8f; // 轻微向上跳跃

            Vector3 offset = new Vector3(offsetX, offsetY, offsetZ);
            treeTrans.position = originalPos + offset;

            // 2. 剧烈旋转（模拟左右前后摇摆，增加视觉冲击）
            float angleX = Mathf.Sin(timeParam * 0.8f) * 25f;
            float angleZ = Mathf.Cos(timeParam * 0.9f) * 25f;
            float angleY = Mathf.Sin(timeParam * 0.5f) * 45f;
            treeTrans.rotation = originalRot * Quaternion.Euler(angleX, angleY, angleZ);

            // 3. 将附近的猎人撞得动来动去
            Collider[] colliders = Physics.OverlapSphere(treeTrans.position, 3.5f); // 碰撞检测范围稍大
            foreach(var col in colliders)
            {
                HunterPlayer hunter = col.GetComponent<HunterPlayer>() ?? col.GetComponentInParent<HunterPlayer>();
                if (hunter != null)
                {
                    // CharacterController cc = hunter.GetComponent<CharacterController>();
                    // if (cc != null)
                    // {
                    //     // 计算弹开方向（从树的中心往外推）
                    //     Vector3 pushDir = hunter.transform.position - treeTrans.position;
                    //     pushDir.y = 0; // 只在水平面上施加撞击力，以免把猎人拍到地下
                        
                    //     // 稍微加点噪音让推力更不可预测
                    //     pushDir += new Vector3(Random.Range(-0.5f, 0.5f), 0, Random.Range(-0.5f, 0.5f));
                    //     pushDir.Normalize();

                    //     // 强行移动 CC 模拟撞击效果
                    //     cc.Move(pushDir * pushForce * Time.deltaTime);
                    // }

                    if (hunter.connectionToClient != null) 
                    {
                        // 计算弹开方向（从树的中心往外推）
                        Vector3 pushDir = hunter.transform.position - treeTrans.position;
                        pushDir.y = 0; 
                        pushDir += new Vector3(Random.Range(-0.5f, 0.5f), 0, Random.Range(-0.5f, 0.5f));
                        pushDir.Normalize();

                        // 因为是在协程里每帧调用，推力需要适配 Time.deltaTime，并且可以适当放大 force
                        Vector3 appliedForce = pushDir * pushForce * Time.deltaTime * 10f; 

                        // 通知猎人的客户端，让他自己推自己
                        hunter.TargetApplyKnockback(hunter.connectionToClient, appliedForce);
                    }
                    else if (hunter.isLocalPlayer) 
                    {
                        // 如果猎人就是 Host 房主，直接走本地函数
                        Vector3 pushDir = hunter.transform.position - treeTrans.position;
                        pushDir.y = 0; 
                        pushDir.Normalize();
                        hunter.TargetApplyKnockback(null, pushDir * pushForce * Time.deltaTime * 10f);
                    }
                }
            }

            yield return null;
        }
        
        // 结束时恢复原样
        treeTrans.position = originalPos;
        treeTrans.rotation = originalRot;
    }
}
```

## Skill\Witch\WitchSkill_Curse.cs

```csharp
using UnityEngine;
using Mirror;

public class WitchSkill_Curse : SkillBase
{
    public float range = 10f;
    public LayerMask treeLayer; // 确保树在这个 Layer

    protected override void OnCast()
    {
        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used skill: Curse! Attempting to curse a tree.</color>");
        // 射线检测
        Ray ray = new Ray(ownerPlayer.transform.position + Vector3.up, ownerPlayer.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, range, treeLayer))
        {
            PropTarget prop = hit.collider.GetComponentInParent<PropTarget>();
            // 只能诅咒普通树，不能诅咒古树
            if (prop != null && !prop.isAncientTree)
            {
                // 动态添加组件
                if (prop.gameObject.GetComponent<CursedTreeTrigger>() == null)
                {
                    var curse = prop.gameObject.AddComponent<CursedTreeTrigger>();
                    curse.casterNetId = ownerPlayer.netId;
                    // 不需要 NetworkServer.Spawn，因为这是添加组件，但要注意 Mirror 对于动态组件的支持有限
                    // 更好的做法是生成一个不可见的 Hitbox Prefab 罩住树
                    // 简易做法：利用 Rpc 通知客户端显示特效
                    RpcCurseEffect(prop.transform.position);
                }
            }
        }
    }

    [ClientRpc]
    void RpcCurseEffect(Vector3 pos)
    {
        
        // 播放一点紫色的粒子特效，提示女巫诅咒成功
    }
}
```

## Skill\Witch\WitchSkill_Decoy.cs

```csharp
using UnityEngine;
using Mirror;


public class WitchSkill_Decoy : SkillBase
{
    public GameObject decoyPrefab; 
    [HideInInspector] public int spawnCount = 1; // 默认生成1个
    protected override void OnCast()
    {
        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used skill: Decoy! Summoning a decoy.</color>");
        WitchPlayer witch = ownerPlayer as WitchPlayer;
        if (witch == null) return;

        GameManager.Instance?.ServerPlay3DAt("pop_sound", ownerPlayer.transform.position);
        
        // 如果没变身，就复制人类 (或者禁止使用)
        // 这里假设复制当前的 morphedPropID
        int idToCopy = witch.isMorphed ? witch.morphedPropID : -1; // -1 表示没变身
        for (int i = 0; i < spawnCount; i++)
        {
            // 在玩家前方一個身位的位置生成
            Vector3 spawnPosition = witch.transform.position + witch.transform.forward * 1.0f;
            // 2. 地面探测：从上方发射射线，确保分身生成在地面高度
            if (Physics.Raycast(spawnPosition + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 5f, witch.groundLayer))
            {
                spawnPosition = hit.point + Vector3.up * 0.05f; // 贴地并微抬防止卡入
            }
            GameObject decoy = Instantiate(decoyPrefab, spawnPosition, witch.transform.rotation);
            DecoyBehavior db = decoy.GetComponent<DecoyBehavior>();
            db.propID = idToCopy;

            NetworkServer.Spawn(decoy);            
        }

    }
}
```

## Skill\Witch\WitchSkill_Mist.cs

```csharp
using UnityEngine;
using Mirror;

public class WitchSkill_Mist : SkillBase
{
    [Header("技能参数")]
    public GameObject mistPrefab; // 迷雾预制体
    public float spawnOffset = 1.0f; // 在身后多少米生成
    public float mistScale = 1.0f; // 【新增】迷雾缩放倍率，默认为1

    protected override void OnCast()
    {
        if (mistPrefab == null)
        {
            Debug.LogError("[WitchSkill_Mist] Mist Prefab 未赋值！");
            return;
        }

        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used Mist!</color>");

        // 1. 计算生成位置：在女巫身后
        // 注意使用 -transform.forward
        Vector3 spawnPos = ownerPlayer.transform.position - ownerPlayer.transform.forward * spawnOffset;
        
        // 稍微抬高一点，防止生成在地板下
        spawnPos.y += 0.5f;
        GameManager.Instance?.ServerPlay3DAt("女巫迷雾", spawnPos);
        // 2. 生成实例
        GameObject mist = Instantiate(mistPrefab, spawnPos, Quaternion.identity);

        // 【核心修改】应用奖励带来的缩放
        // 缩放 Transform 会同时增大其子物体的 Trigger 碰撞体范围
        mist.transform.localScale *= mistScale;
        
        // 3. 网络同步
        NetworkServer.Spawn(mist);
        
    }
}
```

## UI\BGMController.cs

```csharp
using UnityEngine;
using System.Collections;

public class BGMController : MonoBehaviour
{
    public static BGMController Instance;

    [Header("Audio Settings")]
    public AudioSource musicSource;
    public float maxVolume = 0.5f;
    public float fadeDuration = 1.5f;

    private void Awake()
    {
        // 简单的单例，方便在切换场景前调用淡出
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        
        // 确保 AudioSource 设置正确
        if (musicSource == null) musicSource = GetComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.volume = 0;
    }

    private void Start()
    {
        // 场景开始时执行淡入
        StartCoroutine(FadeMusic(0, maxVolume, fadeDuration));
    }

    public void StartFadeOut()
    {
        // 供切换场景的按钮调用
        StopAllCoroutines();
        StartCoroutine(FadeMusic(musicSource.volume, 0, fadeDuration));
    }

    private IEnumerator FadeMusic(float startVol, float targetVol, float duration)
    {
        if (!musicSource.isPlaying) musicSource.Play();

        float timer = 0;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(startVol, targetVol, timer / duration);
            yield return null;
        }
        musicSource.volume = targetVol;

        if (targetVol <= 0) musicSource.Stop();
    }
}
```

## UI\CameraData.cs

```csharp
using UnityEngine;

[CreateAssetMenu(fileName = "VictoryCameraData", menuName = "Game/Camera Data")]
public class CameraData : ScriptableObject
{
    public Vector3 position;
    public Vector3 eulerRotation; // 使用欧拉角方便在 Inspector 调整
}
```

## UI\CircularProgressGlow.cs

```csharp
using UnityEngine;
using UnityEngine.UI;

public class CircularProgressGlow : MonoBehaviour
{
    [Header("References")]
    public Image fillImage;         // 拖入 Fill 物体
    public RectTransform headDot;   // 拖入 ProgressHead 物体

    [Header("Settings")]
    public float radius = 50f;      // 圆环的半径（根据你进度条的大小调整）

    public void UpdateProgress(float progress)
    {
        // 1. 设置进度条填充
        fillImage.fillAmount = progress;

        // 2. 计算末端圆点的位置
        // Unity 的 Radial Fill 0是从顶部(90度)顺时针开始
        float angle = progress * 360f;
        float rad = (90f - angle) * Mathf.Deg2Rad; // 转换为弧度

        float x = Mathf.Cos(rad) * radius;
        float y = Mathf.Sin(rad) * radius;

        // 3. 更新圆点位置
        if (headDot != null)
        {
            headDot.anchoredPosition = new Vector2(x, y);
            // 只有当进度 > 0 时才显示圆点，防止起始位置露出
            headDot.gameObject.SetActive(progress > 0.01f && progress < 0.99f);
        }
    }
}
```

## UI\ConnectUIManager.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;
using System.Collections;
using kcp2k;

public class ConnectUIManager : MonoBehaviour
{
    [Header("主界面")]
    public Button joinButton; // 加入房间按钮 (默认禁用，选中房间后启用)
    public Button openCreatePanelBtn;// 打开创建房间弹窗的按钮
    public Button refreshBtn;// 刷新列表按钮
    public Transform listContent;     // ScrollView 的 Content
    public GameObject roomItemPrefab; // 你的房间条目 Prefab

    [Header("创建房间弹窗")]
    public Button confirmCreateBtn;// 确认创建按钮
    public Button cancelCreateBtn;// 取消创建按钮
    public GameObject createPanel;    // 弹窗 Panel (默认隐藏)
    public TMP_InputField roomNameInput;
    public Toggle passwordToggle;     // "是否有密码" 勾选框
    public TMP_Text passwordToggleLabel; // 勾选框旁的文字
    public TMP_InputField passwordInput;
    public TMP_Text PasswordLabel;
    [Header("加入房间弹窗")]
    public TMP_InputField joinPwdInput;
    public Button confirmJoinPwdBtn;
    public GameObject inputPwdPanel;
    // 记录当前选中的房间信息
    private int selectedRoomId = -1;       // -1 表示未选中
    private bool selectedRoomHasPwd = false;
    // 网络是否已初始化标志
    private bool isNetworkReady = false;

    void Start()
    {
        if (Application.isBatchMode)
        {
            this.enabled = false;
            return;
        }

        //  绑定主界面按钮
        openCreatePanelBtn.onClick.AddListener(() => ControlCreatePanel(true));
        refreshBtn.onClick.AddListener(SendGetListReq);
        joinButton.onClick.AddListener(OnClickJoin);

        // 绑定弹窗按钮
        confirmCreateBtn.onClick.AddListener(SendCreateReq);
        cancelCreateBtn.onClick.AddListener(() => ControlCreatePanel(false));
        confirmJoinPwdBtn.onClick.AddListener(OnConfirmPwd);

        // 绑定 Toggle 逻辑：勾选时才显示密码输入框
        passwordToggle.onValueChanged.AddListener((isOn) =>
        {
            passwordInput.gameObject.SetActive(isOn);
            PasswordLabel.gameObject.SetActive(isOn);
            passwordToggleLabel.text = isOn ? "ON" : "OFF";
            if (!isOn) passwordInput.text = ""; // 取消勾选清空密码
        });

        // 初始状态
        ControlCreatePanel(false);
        if (inputPwdPanel) inputPwdPanel.SetActive(false);
        if (joinButton) joinButton.interactable = false; // 初始禁用加入按钮
                                                         // 注册网络回调 
        RegisterNetworkHandlers();
    }
    void RegisterNetworkHandlers()
    {
        // 移除旧的 handler 防止重复注册报错
        if (NetworkClient.active)
        {
            NetworkClient.UnregisterHandler<CreateRoomRes>();
            NetworkClient.UnregisterHandler<RoomListRes>();
            NetworkClient.UnregisterHandler<JoinRoomRes>();

            NetworkClient.RegisterHandler<CreateRoomRes>(OnCreateRes);
            NetworkClient.RegisterHandler<RoomListRes>(OnRoomListRes);
            NetworkClient.RegisterHandler<JoinRoomRes>(OnJoinRes);

            isNetworkReady = true;
            Debug.Log("[Client] 网络回调已注册");
            SendGetListReq();
        }
    }
    void Update()
    {
        // 简单的状态检测：如果连接断开又重连了，需要重新注册
        if (NetworkClient.isConnected && !isNetworkReady)
        {
            RegisterNetworkHandlers();
            // 连上大厅后自动刷新一次列表
            SendGetListReq();
        }
        else if (!NetworkClient.isConnected)
        {
            isNetworkReady = false;
        }
    }
    // --- UI 逻辑 ---
    void ControlCreatePanel(bool isOpen)
    {
        createPanel.SetActive(isOpen);
        if (isOpen)
        {
            // 重置输入框
            roomNameInput.text = "";
            passwordInput.text = "";
            passwordToggle.isOn = false;
            passwordToggleLabel.text = "OFF";
            passwordInput.gameObject.SetActive(false);
            PasswordLabel.gameObject.SetActive(false);
        }
    }

    // --- 网络请求：发送创建 ---
    void SendCreateReq()
    {
        if (!NetworkClient.isConnected) return;

        // --- 新增：空检查逻辑 ---
        string rName = roomNameInput.text.Trim();
        if (string.IsNullOrWhiteSpace(rName))
        {
            // 获取占位符文本组件
            var placeholder = roomNameInput.placeholder.GetComponent<TMP_Text>();
            placeholder.text = "<color=red>Name cannot be empty!</color>";
            roomNameInput.text = ""; // 清空空格
            // 甚至可以加个小晃动效果（可选）
            return;
        }
        // ------------------------

        AudioManager.Instance?.Play2D("UI点击（木头）");

        string pwd = (passwordToggle && passwordToggle.isOn) ? passwordInput.text : "";
        
        Debug.Log($"发送创建请求: 房间名='{rName}', 有密码={(!string.IsNullOrEmpty(pwd))}");
        NetworkClient.Send(new CreateRoomReq
        {
            roomName = rName,
            password = pwd,
            maxPlayers = 10
        });

        if (confirmCreateBtn) confirmCreateBtn.interactable = false;
    }

    // --- 网络回调：创建结果 ---
    void OnCreateRes(CreateRoomRes msg)
    {
        if (confirmCreateBtn) confirmCreateBtn.interactable = true;

        if (msg.success)
        {
            Debug.Log("创建成功！正在刷新列表...");
            ControlCreatePanel(false); // 关闭弹窗
            MyNetworkManager netManager = NetworkManager.singleton as MyNetworkManager;
            if (netManager != null)
            {
                netManager.ClientChangeRoom(msg.serverIp, msg.serverPort);
            }
        }
        else
        {
            Debug.LogError($"创建失败: {msg.message}");
        }
    }

    // --- 网络请求：获取列表 ---
    void SendGetListReq()
    {
        if (NetworkClient.isConnected)
        {
            NetworkClient.Send(new GetRoomListReq());
        }
    }

    // --- 网络回调：刷新列表 UI ---
    void OnRoomListRes(RoomListRes msg)
    {
        // 1. 清空 Content 下的所有旧条目
        foreach (Transform child in listContent) Destroy(child.gameObject);

        // 2. 生成新条目
        foreach (var info in msg.rooms)
        {
            GameObject item = Instantiate(roomItemPrefab, listContent);
            Debug.Log($"[RoomList] RoomId={info.roomId}, Name='{info.roomName}', HasPwd={info.hasPassword}, Players={info.currentPlayers}/{info.maxPlayers}");
            // 获取并初始化 RoomItemUI 脚本
            var script = item.GetComponent<RoomItemUI>();
            if (script != null)
            {
                script.Setup(info, this);
            }
        }
    }

    // --- 供 RoomItemUI 调用：处理选中逻辑 ---
    public void SelectRoom(int id, bool hasPwd)
    {
        // 记录数据
        selectedRoomId = id;
        selectedRoomHasPwd = hasPwd;

        // 激活 Join 按钮
        if (joinButton) joinButton.interactable = true;

        Debug.Log($"已选中房间: {id}, 有密码: {hasPwd}");
    }

    // --- UI 逻辑: 点击 Join 按钮 ---
    void OnClickJoin()
    {
        if (selectedRoomId == -1) return;

        AudioManager.Instance?.Play2D("UI点击（木头）");

        if (selectedRoomHasPwd)
        {
            // 有密码 -> 弹出密码输入框
            if (inputPwdPanel) inputPwdPanel.SetActive(true);
            if (joinPwdInput) joinPwdInput.text = "";
        }
        else
        {
            // 无密码 -> 直接发送加入请求
            SendJoinRequest("");
        }
    }

    // --- 发送加入请求 (提取公用方法) ---
    void SendJoinRequest(string password)
    {
        if (!NetworkClient.isConnected) return;

        NetworkClient.Send(new JoinRoomReq
        {
            roomId = selectedRoomId,
            password = password
        });

        // 发送后关闭弹窗
        if (inputPwdPanel) inputPwdPanel.SetActive(false);
    }

    // --- UI 逻辑: 密码弹窗确认 ---
    void OnConfirmPwd()
    {
        AudioManager.Instance?.Play2D("UI点击（木头）");
        if (joinPwdInput) SendJoinRequest(joinPwdInput.text);
    }

    // --- 网络回调：加入结果 (处理跳转) ---
    void OnJoinRes(JoinRoomRes msg)
    {
        if (msg.success)
        {
            Debug.Log($"加入请求成功，委托 NetworkManager 进行跳转...");

            // 找到我们的自定义 NetworkManager
            MyNetworkManager myNetManager = NetworkManager.singleton as MyNetworkManager;
            if (myNetManager != null)
            {
                myNetManager.ClientChangeRoom(msg.serverIp, msg.serverPort);
            }
            else
            {
                Debug.LogError("找不到 MyNetworkManager 实例！");
            }
        }
        else
        {
            Debug.LogError($"加入失败: {msg.message}");
        }
    }

}
```

## UI\edit_video.py

```python
from moviepy import VideoFileClip


def trim_video(input_path, output_path, start_time, end_time=None):
    """
    裁剪视频并保存
    input_path: 输入视频路径
    output_path: 输出视频路径
    start_time: 开始时间 (秒，或字符串 "00:00:13")
    end_time: 结束时间 (秒，或字符串 "00:00:30")，若为 None 则到结尾
    """
    try:
        # 使用 with 自动管理资源
        with VideoFileClip(input_path) as video:
            # 1. 截取指定时段
            # MoviePy 2.x 使用 subclipped
            # MoviePy 1.x 使用 subclip
            trimmed_clip = video.subclipped(start_time, end_time)

            # 2. 导出视频
            # codec="libx264" 是最通用的 MP4 编码
            # audio_codec="aac" 确保音频也正确编码
            trimmed_clip.write_videofile(
                output_path, codec="libx264", audio_codec="aac"
            )

        print(f"裁剪成功！视频已保存至: {output_path}")
    except Exception as e:
        print(f"裁剪失败: {e}")


# --- 使用示例 ---

# 你的原始视频路径
input_video = r"E:\downloads\b\uptown1.mp4"
# 裁剪后的保存路径
output_video = r"E:\downloads\b\uptown1_trimmed.mp4"

# 裁剪从第 13 秒到第 30 秒
trim_video(input_video, output_video, start_time=15, end_time=35)

```

## UI\extract_bgm.py

```python
from moviepy import VideoFileClip


def extract_audio_segment(video_path, output_audio_path, start_time, end_time=None):
    """
    video_path: 视频文件路径
    output_audio_path: 输出音频路径
    start_time: 开始时间，可以是秒数 (例如 10) 或者是 (分, 秒) 或者是 "00:00:10"
    end_time: 结束时间，如果不传则截取到视频结束
    """
    try:
        # 使用 with 自动管理资源
        with VideoFileClip(video_path) as video:
            # 1. 截取指定时段的视频流
            # subclipped 是 MoviePy 2.x 的新用法
            # 如果是旧版本(1.x)，请把 subclipped 改为 subclip
            segment = video.subclipped(start_time, end_time)

            # 2. 提取该段的音频
            audio = segment.audio

            if audio is None:
                print("该视频片段没有音轨！")
                return

            # 3. 写入文件
            audio.write_audiofile(output_audio_path)

            # 注意：segment 是 video 的一个视图，
            # 在 with 语句结束时，video 会被自动关闭

        print(f"提取成功！片段音频已保存至: {output_audio_path}")
    except Exception as e:
        print(f"提取失败: {e}")


# --- 使用示例 ---

# 路径
input_video = r"D:\RecordedVideos\2026-03-06 11-15-18.mp4"
output_audio = r"D:\RecordedVideos\2026-03-06 11-15-18_extracted_audio.mp3"

# 示例 1: 从第 5 秒开始，到第 15 秒结束
extract_audio_segment(input_video, output_audio, start_time=7, end_time=236)

# 示例 2: 从第 1 分 20 秒开始，直到视频结束
# extract_audio_segment(input_video, output_audio, start_time="00:01:20")

# 示例 3: 使用元组 (分, 秒)
# extract_audio_segment(input_video, output_audio, start_time=(1, 30), end_time=(2, 0))

```

## UI\GameChatUI.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public enum ChatChannel
{
    All,  // 全局
    Team  // 队伍
}

public class GameChatUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject chatPanel;        // 整个聊天界面的根物体 (含输入框)
    public TMP_InputField inputField;   // 输入框
    public Transform messageContent;    // ScrollView 的 Content
    public GameObject messagePrefab;    // 消息预制体
    public ScrollRect scrollRect;       // 用于自动滚动
    public TextMeshProUGUI channelText; // 显示当前频道 (比如 "[ALL]" 或 "[TEAM]")
    // 【新增】聊天背景图片的引用
    public Image chatBackgroundImage; 

    [Header("Settings")]
    public KeyCode openChatKey = KeyCode.Slash; // 按 '/' 打开
    public KeyCode closeChatKey = KeyCode.Escape;
    public KeyCode switchChannelKey = KeyCode.Tab; // 按 Tab 切换频道

    public bool isChatOpen = false;
    private ChatChannel currentChannel = ChatChannel.All;
    private GamePlayer localPlayer; // 缓存本地玩家引用

    private void Start()
    {
        // 初始关闭聊天输入栏，但保持消息显示区域可见（通常做法）
        // 或者你可以选择一开始全隐藏
        SetChatState(false);
        UpdateChannelUI();

        // 绑定输入框提交事件
        if (inputField != null)
            inputField.onSubmit.AddListener(OnSubmitMessage);
    }

    private void Update()
    {
        // 获取本地玩家引用（如果还没获取）
        if (localPlayer == null)
        {
            foreach (var p in GamePlayer.AllPlayers)
            {
                if (p.isLocalPlayer)
                {
                    localPlayer = p;
                    break;
                }
            }
        }

        // --- 按键监听 ---
        
        // 打开聊天
        if (!isChatOpen && Input.GetKeyDown(openChatKey))
        {
            SetChatState(true);
        }
        // // 关闭聊天
        // else if (isChatOpen && Input.GetKeyDown(closeChatKey))
        // {
        //     SetChatState(false);
        // }
        // 切换频道 (仅当聊天打开时)
        else if (isChatOpen && Input.GetKeyDown(switchChannelKey))
        {
            ToggleChannel();
        }
    }

    // 切换聊天状态
    public void SetChatState(bool isOpen)
    {
        isChatOpen = isOpen;
        
        if (chatPanel != null)
            chatPanel.SetActive(isOpen); // 控制输入框面板的显示/隐藏

        // 【新增】控制背景透明度
        if (chatBackgroundImage != null)
        {
            Color color = chatBackgroundImage.color;
            // 打开时 0.1 (微弱背景)，关闭时 0 (全透明)
            color.a = isOpen ? 0.1f : 0f; 
            chatBackgroundImage.color = color;
        }
        // 控制垂直滚动条
        // 2. 【核心修改】控制垂直滚动条的透明度与交互
        if (scrollRect != null && scrollRect.verticalScrollbar != null)
        {
            // A. 设置交互性：关闭时禁止拖动，防止误触
            scrollRect.verticalScrollbar.interactable = isOpen;

            // B. 设置透明度：获取滚动条下所有的 Image (背景槽和滑块Handle)
            Image[] scrollbarImages = scrollRect.verticalScrollbar.GetComponentsInChildren<Image>();
            foreach (var img in scrollbarImages)
            {
                Color c = img.color;
                // 这里假设滚动条完全显示时 alpha 为 1。如果你原本就是半透明，可以改为保存初始值。
                c.a = isOpen ? 1f : 0f; 
                img.color = c;
            }
        }

        if (isOpen)
        {
            // 打开：激活输入框，解锁鼠标
            inputField.ActivateInputField();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            
            // 告诉 GamePlayer 暂停移动输入 (可选，需要在 GamePlayer 里加个标志位)
            if (localPlayer != null) localPlayer.isChatting = true;
        }
        else
        {
            // 关闭：清空输入，锁定鼠标
            inputField.text = "";
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            
            // 恢复移动
            if (localPlayer != null) localPlayer.isChatting = false;
        }
    }

    // 切换频道逻辑
    private void ToggleChannel()
    {
        if (currentChannel == ChatChannel.All)
            currentChannel = ChatChannel.Team;
        else
            currentChannel = ChatChannel.All;

        UpdateChannelUI();
    }

    private void UpdateChannelUI()
    {
        if (channelText != null)
        {
            channelText.text = (currentChannel == ChatChannel.All) ? "[ALL]" : "[TEAM]";
            channelText.color = (currentChannel == ChatChannel.All) ? Color.white : Color.green;
        }
    }

    // 发送消息
    private void OnSubmitMessage(string text)
    {
        if (!string.IsNullOrWhiteSpace(text) && localPlayer != null)
        {
            localPlayer.CmdSendGameMessage(text, currentChannel);
        }

        // 发送完关闭聊天
        SetChatState(false);
        
        // 如果想发送完保持开启，可以注释上一行，改用：
        // inputField.text = ""; 
        // inputField.ActivateInputField();
    }

    // 供外部调用：显示接收到的消息
    public void AppendMessage(string senderName, string message, ChatChannel channel, Color roleColor)
    {
        if (messagePrefab == null || messageContent == null) return;

        // 1. 生成消息条目
        GameObject newMsg = Instantiate(messagePrefab, messageContent);
        
        // 【修改点】改为 GetComponentInChildren，防止 Text 在子物体上找不到
        TextMeshProUGUI tmp = newMsg.GetComponentInChildren<TextMeshProUGUI>(); 
        
        if (tmp != null)
        {
            // 2. 拼接频道信息
            string channelPrefix = (channel == ChatChannel.All) ? "[ALL]" : "[TEAM]";
            string channelColorHtml = (channel == ChatChannel.All) ? "#FFFFFF" : "#00FF00"; // 全局白色，队伍绿色
            
            // 3. 处理名字颜色
            string nameColorHtml = ColorUtility.ToHtmlStringRGB(roleColor);

            // 4. 组合最终字符串并赋值
            // 格式： [ALL] [PlayerName]: Hello World
            tmp.text = $"<color={channelColorHtml}>{channelPrefix}</color> <color=#{nameColorHtml}>[{senderName}]</color>: {message}";
        }
        else
        {
            // 如果还是没找到，打印错误方便调试
            Debug.LogError("错误：在 MessagePrefab 中找不到 TextMeshProUGUI 组件！请检查预制体结构。");
        }

        // 5. 刷新布局
        LayoutRebuilder.ForceRebuildLayoutImmediate(messageContent.GetComponent<RectTransform>());
        StartCoroutine(ScrollToBottom());
    }

    IEnumerator ScrollToBottom()
    {
        yield return new WaitForEndOfFrame();
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
    }
}
```

## UI\LoadingTipsUI.cs

```csharp
using UnityEngine;
using TMPro;

public class LoadingTipsUI : MonoBehaviour
{
    public TextMeshProUGUI tipText;
    private string[] tips = {
        "Witches can possess ancient trees to move them!",
        "Hunters use dogs to track witch footprints.",
        "Don't forget to press F to use your items!",
        "Magic brooms allow you to double jump!",
        "Working together as a witch team makes control easier."
    };

    private int lastIndex = -1; // 记录上一次显示的索引

    void Start()
    {
        ShowRandomTip();
    }

    void Update()
    {
        // 检测鼠标左键点击 (0 是左键)
        // 这也兼容手机端的单指点击
        if (Input.GetMouseButtonDown(0))
        {
            ShowRandomTip();
        }
    }

    // 将逻辑封装成方法，方便多处调用
    public void ShowRandomTip()
    {
        if (tipText == null || tips.Length == 0) return;

        int newIndex = lastIndex;

        // 如果贴士数量大于1，则通过循环确保抽到跟上次不一样的贴士
        if (tips.Length > 1)
        {
            while (newIndex == lastIndex)
            {
                newIndex = Random.Range(0, tips.Length);
            }
        }
        else
        {
            newIndex = 0;
        }

        lastIndex = newIndex;
        tipText.text = tips[newIndex];
    }
}
```

## UI\LobbyChat.cs

```csharp
using UnityEngine;
using UnityEngine.UI; // Button 还是用这个
using TMPro;          // 【关键】引用 TMP
using Mirror;

public class LobbyChat : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField chatBoxField; // 【关键】改为 TMP_InputField
    public Button sendButton;           // 发送按钮
    public Transform messageLayout;     // MessageLayout (Content 父物体)
    public GameObject messageGroupPrefab; // MessageGroup 预制体
    public ScrollRect scrollRect;       // (可选) 用于自动滚动

    private void Start()
    {
        // 绑定按钮点击
        if(sendButton) sendButton.onClick.AddListener(OnSendClicked);

        // 绑定回车发送 (TMP_InputField 的事件)
        if(chatBoxField) chatBoxField.onSubmit.AddListener(OnSubmit);
    }

    private void OnSendClicked()
    {
        SendMessageToServer(chatBoxField.text);
    }

    private void OnSubmit(string text)
    {
        SendMessageToServer(text);
        
        // 发送后保持输入框焦点，并清空，方便连续输入
        chatBoxField.ActivateInputField();
        chatBoxField.text = "";
    }

    private void SendMessageToServer(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return;

        // 获取本地玩家进行发送
        if (NetworkClient.connection != null && NetworkClient.connection.identity != null)
        {
            var localPlayer = NetworkClient.connection.identity.GetComponent<PlayerScript>();
            if (localPlayer != null)
            {
                localPlayer.CmdSendChatMessage(msg);
            }
        }
        
        // 清空输入框
        if(chatBoxField) chatBoxField.text = "";
    }

    // 供 PlayerScript 接收到消息后调用
    public void AppendMessage(string playerName, string message, Color color)
    {
        if (messageGroupPrefab == null || messageLayout == null) return;

        // 生成新消息
        GameObject newMsg = Instantiate(messageGroupPrefab, messageLayout);
        MessageItem item = newMsg.GetComponent<MessageItem>();
        
        if (item != null)
        {
            item.Setup(playerName, message, color);
        }

        // 【关键】强制刷新布局系统
        // 有时候 Content Size Fitter 需要一帧的时间来计算高度，强制刷新可以立即生效
        LayoutRebuilder.ForceRebuildLayoutImmediate(messageLayout.GetComponent<RectTransform>());

        // 滚动到底部
        StartCoroutine(ScrollToBottom());
    }
    System.Collections.IEnumerator ScrollToBottom()
    {
        // 等待一帧让UI布局刷新
        yield return new WaitForEndOfFrame();
        if(scrollRect) scrollRect.verticalNormalizedPosition = 0f;
    }
}
```

## UI\LobbyModelPreview.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class LobbyModelPreview : MonoBehaviour
{
    // 【新增】单例，方便 UI 脚本调用刷新
    public static LobbyModelPreview Instance;
    [Header("UI Buttons")]
    public Button maleButton;
    public Button femaleButton;

    [Header("Models")]
    public Animator witchMale;
    public Animator witchFemale;
    public Animator hunterMale;
    public Animator hunterFemale;
    [Header("Item Variants")]
    public Animator witchMaleCloak;
    public Animator witchFemaleCloak;
    public Animator witchMaleAmulet;   // 【新增】
    public Animator witchFemaleAmulet; // 【新增】
    public Animator witchMaleBroom;    // 【新增】
    public Animator witchFemaleBroom;  // 【新增】

    [Header("Movement Settings")]
    public float forwardZ = -1.5f; 
    public float backwardZ = 1.0f; 
    public float lerpSpeed = 10f; // 增加速度让反馈更即时

    [Header("Rotation Settings")]
    public Vector3 facingRotation = new Vector3(0, 180, 0); // 如果模型背对镜头，修改这里的 Y
    [Header("Config")]
    public string cloakItemName = "InvisibilityCloak"; // 必须与 WitchItemData 里的类名一致

    // 记录所有基础坐标
    private Vector3 wMaleBase, wFemaleBase, hMaleBase, hFemaleBase;
    private Vector3 wMaleCloakBase, wFemaleCloakBase;
    private Vector3 wMaleAmuletBase, wFemaleAmuletBase;
    private Vector3 wMaleBroomBase, wFemaleBroomBase;
    private Gender currentGender;
    private void Awake() => Instance = this; // 初始化单例
    private void Start()
    {
        // 1. 记录初始位置
        // 建议在编辑器里把这4个模型放在同一个 Z 轴坐标上
        wMaleBase = witchMale.transform.localPosition;
        wFemaleBase = witchFemale.transform.localPosition;
        hMaleBase = hunterMale.transform.localPosition;
        hFemaleBase = hunterFemale.transform.localPosition;
        // 【新增】记录斗篷版位置
        wMaleCloakBase = witchMaleCloak.transform.localPosition;
        wFemaleCloakBase = witchFemaleCloak.transform.localPosition;
        wMaleAmuletBase = witchMaleAmulet.transform.localPosition;
        wFemaleAmuletBase = witchFemaleAmulet.transform.localPosition;
        wMaleBroomBase = witchMaleBroom.transform.localPosition;
        wFemaleBroomBase = witchFemaleBroom.transform.localPosition;
        // 2. 绑定按钮
        maleButton.onClick.AddListener(() => UpdateSelection(Gender.Male));
        femaleButton.onClick.AddListener(() => UpdateSelection(Gender.Female));

        // 3. 初始读取性别并直接应用（不等待 Lerp）
        currentGender = PlayerSettings.Instance.selectedGender;
        ApplySelection(currentGender, true);
    }
    // 【新增方法】供道具选择 UI 调用，当玩家切换道具时刷新模型
    public void RefreshItemSelection()
    {
        ApplySelection(currentGender, false);
    }
    private void UpdateSelection(Gender gender)
    {
        if (currentGender == gender) return;
        ApplySelection(gender, false);
    }

    private void ApplySelection(Gender gender, bool immediate)
    {
        currentGender = gender;
        PlayerSettings.Instance.SetGender((int)gender);
        // 1. 【核心修复】在切换性别的瞬间，立刻把所有模型的旋转拉回初始方向
        // 这样可以清除上一段动画可能残留的微小旋转误差
        ResetAllRotations();
        if (NetworkClient.active && NetworkClient.localPlayer != null)
        {
            NetworkClient.localPlayer.GetComponent<PlayerScript>().CmdUpdateGender(gender);
        }

        // 按钮交互
        if (maleButton) maleButton.interactable = (gender != Gender.Male);
        if (femaleButton) femaleButton.interactable = (gender != Gender.Female);

        if (immediate)
        {
            UpdateAllPositions(true);
        }
    }
    private void UpdateAllPositions(bool immediate)
    {
        string selectedItem = PlayerSettings.Instance.selectedWitchItemName;

        // --- 核心判定：男巫组 ---
        // 只有选了对应的道具，该模型才会 SetActive(true)，并根据性别决定 forwardZ/backwardZ
        HandleModelLogic(witchMale, wMaleBase, currentGender == Gender.Male, selectedItem == "", immediate);
        HandleModelLogic(witchMaleCloak, wMaleCloakBase, currentGender == Gender.Male, selectedItem == "InvisibilityCloak", immediate);
        HandleModelLogic(witchMaleAmulet, wMaleAmuletBase, currentGender == Gender.Male, selectedItem == "LifeAmulet", immediate);
        HandleModelLogic(witchMaleBroom, wMaleBroomBase, currentGender == Gender.Male, selectedItem == "MagicBroom", immediate);

        // --- 核心判定：女巫组 ---
        HandleModelLogic(witchFemale, wFemaleBase, currentGender == Gender.Female, selectedItem == "", immediate);
        HandleModelLogic(witchFemaleCloak, wFemaleCloakBase, currentGender == Gender.Female, selectedItem == "InvisibilityCloak", immediate);
        HandleModelLogic(witchFemaleAmulet, wFemaleAmuletBase, currentGender == Gender.Female, selectedItem == "LifeAmulet", immediate);
        HandleModelLogic(witchFemaleBroom, wFemaleBroomBase, currentGender == Gender.Female, selectedItem == "MagicBroom", immediate);

        // 猎人保持原样
        HandleModelLogic(hunterMale, hMaleBase, currentGender == Gender.Male, true, immediate);
        HandleModelLogic(hunterFemale, hFemaleBase, currentGender == Gender.Female, true, immediate);
    }
    // 辅助方法：统一处理位置、动画和显隐
    private void HandleModelLogic(Animator anim, Vector3 basePos, bool isGenderSelected, bool isItemMatch, bool immediate)
    {
        if (anim == null) return;

        // 只有该性别的该道具模型应该显示
        bool shouldBeVisible = isItemMatch;

        if (anim.gameObject.activeSelf != shouldBeVisible)
        {
            anim.gameObject.SetActive(shouldBeVisible);
            if (shouldBeVisible) anim.transform.localRotation = Quaternion.Euler(facingRotation);
        }

        if (!shouldBeVisible) return;

        // 决定前后
        Vector3 targetPos = GetTargetPos(basePos, isGenderSelected);

        if (immediate)
        {
            anim.transform.localPosition = targetPos;
            anim.transform.localRotation = Quaternion.Euler(facingRotation);
        }
        else
        {
            anim.transform.localPosition = Vector3.Lerp(anim.transform.localPosition, targetPos, Time.deltaTime * lerpSpeed);
        }

        anim.SetBool("IsSelected", isGenderSelected);
    }
    private void Update()
    {
        UpdateAllPositions(false);
    }
    private Vector3 GetTargetPos(Vector3 basePos, bool isSelected)
    {
        float offset = isSelected ? forwardZ : backwardZ;
        return new Vector3(basePos.x, basePos.y, basePos.z + offset);
    }

    // 新增：只在切换时调用的重置方法
    private void ResetAllRotations()
    {
        Quaternion fixedRot = Quaternion.Euler(facingRotation);
        Animator[] all = { witchMale, witchFemale, hunterMale, hunterFemale, 
                           witchMaleCloak, witchFemaleCloak, witchMaleAmulet, 
                           witchFemaleAmulet, witchMaleBroom, witchFemaleBroom };
        foreach (var a in all) if (a != null) a.transform.localRotation = fixedRot;
    }
}
```

## UI\LobbyScript.cs

```csharp
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
    [SyncVar(hook = nameof(OnSettingChanged))] public float syncedHunterRatio = 0.3f; // 默认 30% 猎人
    [SyncVar(hook = nameof(OnSettingChanged))] public float syncedAncientRatio = 1.5f; // 默认 1.5 倍

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
    [Header("Start Button Style")]
    public TextMeshProUGUI startButtonText; // 拖入你的 StartText 对象
    public Color normalTextColor = new Color(0.788f, 0.666f, 0.541f); // 你截图中的 C9AA8A
    public Color countdownTextColor = new Color(1f, 0.73f, 0.42f);     // 琥珀金，更具魔幻感
    [Header("Room Info")]
    [SyncVar(hook = nameof(OnRoomNameChanged))]
    public string syncedRoomName = "";

    [SerializeField] private TextMeshProUGUI roomTitleText; // 在 Inspector 中拖入你的 RoomTitle UI
    private void Start()
    {
        // 【新增】进入大厅时，强制恢复鼠标显示和解锁
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;        
        // 绑定按钮事件
        if(btnStartGame) btnStartGame.onClick.AddListener(OnClickStartGame);
        // 默认隐藏开始按钮，稍后判断权限开启
        if(btnStartGame) btnStartGame.gameObject.SetActive(false);
        
        foreach (var p in FindObjectsOfType<PlayerScript>())
        {
            AddPlayerRow(p);
        }
        // 如果是服务器，从 NetworkManager 拿到刚才解析的名字
        if (isServer)
        {
            syncedRoomName = MyNetworkManager.InitialRoomName;
        }

        // 初始显示一次
        if (roomTitleText != null) roomTitleText.text = syncedRoomName;
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

    // Hook 函数：当名字同步到客户端时更新 UI
    void OnRoomNameChanged(string oldName, string newName)
    {
        if (roomTitleText != null)
        {
            roomTitleText.text = newName;
        }
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

        // 【修改】初始化显示时也传入 ping
        rowScript.UpdateInfo(player.playerName, player.isReady, player.isLocalPlayer, player.ping);

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
            playerRows[player].UpdateInfo(player.playerName, player.isReady, player.isLocalPlayer,player.ping);
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
                // btnStartGame.interactable = false; // 倒计时期间禁止重复点击
                btnStartGame.interactable = allReady || isGameStarting;
                // 获取按钮下的 TMP 文字组件
                var btnText = btnStartGame.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    // 将文字改为大的数字倒计时
                    btnText.text = countdownDisplay.ToString();
                    btnText.fontSize = 30; // 倒计时数字可以大一点，更有冲击力  
                    btnText.color = countdownTextColor; // 切换到倒计时颜色
                }
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
                if (btnText != null)
                {
                    btnText.text = "Start";
                    startButtonText.color = normalTextColor; // 恢复为你设定的原色 C9AA8A
                    startButtonText.fontSize = 20; // 恢复你图中的字体大小
                    // 如果人没齐，文字可以半透明或变灰，提示不可点
                    startButtonText.color = allReady ? normalTextColor : new Color(normalTextColor.r, normalTextColor.g, normalTextColor.b, 0.5f);
                }
            }

            if (roomStatusText != null)
            {
                if (allReady)
                {
                    roomStatusText.text = "All Ready!";
                    roomStatusText.color = Color.green;
                }
                else
                {
                    roomStatusText.text = $"Waiting for Players";
                    roomStatusText.color = Color.red;
                }
            }
        }
    }

    private void UpdateUI()
    {
        if (playerNumberText != null)
        {
            playerNumberText.text = $"{readyCount} / {playerCount}";
        }
    }
    public override void OnStartServer()
    {
        base.OnStartServer();
        // 确保服务器启动大厅对象时，立即同步名称
        syncedRoomName = MyNetworkManager.InitialRoomName;
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
        AudioManager.Instance?.Play2D("UI点击（木头）");

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
        AudioManager.Instance?.Play2D("UI点击（木头）");

        // 安全获取本地玩家
        var localPlayer = NetworkClient.connection.identity.GetComponent<PlayerScript>();
        if (localPlayer == null) return;

        if (isGameStarting)
        {
            // --- 核心修改：通过 Command 请求取消 ---
            Debug.Log("Player requested to cancel countdown.");
            localPlayer.CmdCancelStart(); 
        }
        else
        {
            // --- 正常开始游戏 (之前已有的逻辑) ---
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
```

## UI\LobbySettingsManager.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Mirror;

/**
 * 【开发者指南】增加一项新游戏设置（Setting）的完整步骤：
 * 
 * 1. LobbyScript.cs (定义真值): 
 *    - 增加 [SyncVar(hook = nameof(OnSettingChanged))] 变量 (例如: syncedWitchJumpForce)。
 *    - 确保它使用通用的 OnSettingChanged 钩子，以便数值改变时通知 UI 刷新。
 * 
 * 2. PlayerScript.cs (建立通信隧道):
 *    - 在 CmdUpdateLobbySettings 的 switch 语句中增加一个新 case (例如: case 8)。
 *    - 将传入的 floatVal/boolVal/intVal 赋给 LobbyScript 中的对应变量。
 * 
 * 3. LobbySettingsManager.cs -> BuildSettingsUI() (生成 UI):
 *    - 在对应类别下调用 CreateSlider/CreateToggle/CreateDropdown。
 *    - key 必须唯一，回调 lambda 中调用 localPlayer?.CmdUpdateLobbySettings，传入刚才定义的 case 编号。
 * 
 * 4. LobbySettingsManager.cs -> UpdateVisuals() (同步视觉效果):
 *    - 调用辅助方法 UpdateSliderVisual("你的Key", lobby.synced变量)。
 *    - 若是 Toggle，则手动编写 TryGetValue 逻辑并调用 SetIsOnWithoutNotify。
 * 
 * 5. GameManager.cs -> 数据固化 (跨场景保护):
 *    - 增加一个 private 内部变量 (例如: witchJumpForceInternal)。
 *    - 在 StartGame() 方法中，从 LobbyScript 抓取该值存入内部变量。
 *    - 在 StartGame() 的 else 分支中，为该变量设置一个默认值。
 * 
 * 6. GameManager.cs -> 逻辑应用 (实际生效):
 *    - 在 SpawnPlayerForConnection() 中，根据 role 判断，将固化的内部变量赋给刚生成的 playerScript。
 */

public class LobbySettingsManager : MonoBehaviour
{
    public static LobbySettingsManager Instance;

    [Header("UI Toggle")]
    public GameObject settingPanel;
    public Button settingBtn;
    public TextMeshProUGUI settingBtnText;

    [Header("Prefabs")]
    public GameObject sliderPrefab;   
    public GameObject togglePrefab;   
    public GameObject dropdownPrefab; 
    public GameObject headerPrefab;
    public Transform container;       

    // 用于记录已经生成的 UI 元素，避免 Destroy
    private Dictionary<string, GameObject> spawnedSettings = new Dictionary<string, GameObject>();

    private void Awake()
    {
        Instance = this;
        settingPanel.SetActive(false);
        settingBtn.onClick.AddListener(TogglePanel);
    }

    private void Start()
    {
        BuildSettingsUI();
    }

    // --- 【新增代码：检测键盘 Esc 输入】 ---
    private void Update()
    {
        // 如果面板正处于打开状态，且玩家按下了 Esc 键
        if (settingPanel.activeSelf && Input.GetKeyDown(KeyCode.Escape))
        {
            // 调用原有的 TogglePanel 方法来关闭它
            TogglePanel();
        }
    }
    public void TogglePanel()
    {
        bool isActive = !settingPanel.activeSelf;
        settingPanel.SetActive(isActive);
        settingBtnText.text = isActive ? "Close" : "Setting";
    }

    private void BuildSettingsUI()
    {
        foreach (Transform child in container) Destroy(child.gameObject);
        spawnedSettings.Clear();
        LobbyScript lobby = FindObjectOfType<LobbyScript>();
        if (lobby == null) return;
        PlayerScript localPlayer = NetworkClient.localPlayer?.GetComponent<PlayerScript>();
        //将CmdUpdateLobbySettings的type顺序编号与UI生成顺序对应起来，方便维护
        // --- 类别：核心规则 ---
        CreateHeader("--- BASIC RULES ---");
        // 游戏时间：整数 (true)
        CreateSlider("GameTime", "Game Time (sec)", 60, 600, lobby.syncedGameTimer, true, (v) => localPlayer?.CmdUpdateLobbySettings(0, v, false, 0));
        // 动物数量：整数 (true)
        CreateSlider("Animals", "Animal Count", 0, 50, lobby.syncedAnimalsNumber, true, (v) => localPlayer?.CmdUpdateLobbySettings(1, v, false, 0));
        CreateToggle("FriendlyFire", "Friendly Fire", lobby.syncedFriendlyFire, (v) => localPlayer?.CmdUpdateLobbySettings(2, 0, v, 0));

        // --- 类别：阵营平衡 ---
        CreateHeader("--- BALANCE ---");
        // 血量：整数 (true)
        CreateSlider("WitchHP", "Witch Max HP", 50, 200, lobby.syncedWitchHP, true, (v) => localPlayer?.CmdUpdateLobbySettings(3, v, false, 0));
        CreateSlider("WitchMana", "Witch Max Mana", 50, 200, lobby.syncedWitchMana, true, (v) => localPlayer?.CmdUpdateLobbySettings(4, v, false, 0));
        // 速度：小数 (false)
        CreateSlider("HunterSpeed", "Hunter Speed", 4, 12, lobby.syncedHunterSpeed, false, (v) => localPlayer?.CmdUpdateLobbySettings(5, v, false, 0));
        // 挣脱：整数 (true)
        CreateSlider("TrapDiff", "Trap Escape Clicks", 1, 10, lobby.syncedTrapDifficulty, true, (v) => localPlayer?.CmdUpdateLobbySettings(6, v, false, 0));
        // 恢复率：小数 (false)
        CreateSlider("ManaRate", "Mana Regen Rate", 1, 20, lobby.syncedManaRegen, false, (v) => localPlayer?.CmdUpdateLobbySettings(7, v, false, 0));
        // 猎人比例：小数 (false) 【这是你刚才报错的地方】
        CreateSlider("HunterRatio", "Hunter Ratio (%)", 0.1f, 0.9f, lobby.syncedHunterRatio, false, (v) => localPlayer?.CmdUpdateLobbySettings(8, v, false, 0));    
        CreateSlider("AncientRatio", "Ancient Tree Ratio (x)", 1.0f, 3.0f, lobby.syncedAncientRatio, false, (v) => localPlayer?.CmdUpdateLobbySettings(9, v, false, 0));
    }
    private void CreateHeader(string title)
    {
        // 检查 Prefab 是否分配
        if (headerPrefab == null)
        {
            Debug.LogError("LobbySettingsManager: headerPrefab 尚未在 Inspector 中分配！");
            return;
        }

        GameObject go = Instantiate(headerPrefab, container);
        
        // 使用 GetComponentInChildren 兼容子物体带有文字的情况
        TextMeshProUGUI textComp = go.GetComponentInChildren<TextMeshProUGUI>();

        if (textComp != null)
        {
            textComp.text = title;
        }
        else
        {
            Debug.LogError($"LobbySettingsManager: 在生成的 {go.name} 及其子物体中找不到 TextMeshProUGUI 组件！");
        }
    }

    // 增加 isWhole 参数
    private void CreateSlider(string key, string label, float min, float max, float current, bool isWhole, System.Action<float> onCmd)
    {
        GameObject go = Instantiate(sliderPrefab, container);
        go.name = key;
        go.transform.Find("Text (TMP)").GetComponent<TextMeshProUGUI>().text = label;
        
        Slider s = go.GetComponentInChildren<Slider>();
        TextMeshProUGUI valText = go.transform.Find("SliderGroup/SliderValue").GetComponent<TextMeshProUGUI>();

        s.minValue = min;
        s.maxValue = max;
        
        // 【关键修改】：不再写死 true，而是使用传进来的变量
        s.wholeNumbers = isWhole; 
        
        s.value = current;

        // 【视觉优化】：如果是小数，显示两位精度；如果是整数，显示为 0 精度
        valText.text = isWhole ? current.ToString("F0") : current.ToString("F2");

        s.onValueChanged.AddListener((v) => {
            valText.text = isWhole ? v.ToString("F0") : v.ToString("F2");
            onCmd?.Invoke(v);
        });

        spawnedSettings.Add(key, go);
    }

    // Toggle 和 Dropdown 的 Create 方法保持类似，给 go.name 赋值即可
    private void CreateToggle(string key, string label, bool current, System.Action<bool> onCmd)
    {
        GameObject go = Instantiate(togglePrefab, container);
        go.name = key;
        
        // 设置左侧标题文字
        go.transform.Find("Text (TMP)").GetComponent<TextMeshProUGUI>().text = label;
        
        Toggle t = go.GetComponentInChildren<Toggle>();
        // 根据你的截图层级：ToggleGroup -> Toggle -> ToggleText
        TextMeshProUGUI statusText = go.transform.Find("ToggleGroup/Toggle/ToggleText").GetComponent<TextMeshProUGUI>();

        // 初始化状态
        t.isOn = current;
        statusText.text = current ? "On" : "Off"; // 或者 "Enabled" : "Disabled"

        t.onValueChanged.AddListener((v) => {
            // 本地即时切换文字
            statusText.text = v ? "On" : "Off";
            onCmd?.Invoke(v);
        });

        spawnedSettings.Add(key, go);
    }

    private void CreateDropdown(string key, string label, List<string> options, int current, System.Action<int> onCmd)
    {
        GameObject go = Instantiate(dropdownPrefab, container);
        go.name = key;
        go.transform.Find("Text (TMP)").GetComponent<TextMeshProUGUI>().text = label;
        TMP_Dropdown d = go.GetComponentInChildren<TMP_Dropdown>();
        d.ClearOptions();
        d.AddOptions(options);
        d.value = current;
        d.onValueChanged.AddListener((v) => onCmd?.Invoke(v));
        spawnedSettings.Add(key, go);
    }

    // 【关键修改】供 Hook 调用：只更新值，不重建 UI
    public void UpdateVisuals()
    {
        LobbyScript lobby = FindObjectOfType<LobbyScript>();
        if (lobby == null) return;

        // 更新 Slider: GameTime
        if (spawnedSettings.TryGetValue("GameTime", out GameObject sliderGo))
        {
            Slider s = sliderGo.GetComponentInChildren<Slider>();
            // 重点：如果用户正在拖拽这个 Slider，不要用服务器数据覆盖它，否则会“弹回”
            if (Input.GetMouseButton(0) == false) 
            {
                s.SetValueWithoutNotify(lobby.syncedGameTimer);
                sliderGo.transform.Find("SliderGroup/SliderValue").GetComponent<TextMeshProUGUI>().text = lobby.syncedGameTimer.ToString();
            }
        }

        // 更新 Toggle: FriendlyFire
        if (spawnedSettings.TryGetValue("FriendlyFire", out GameObject toggleGo))
        {
            Toggle t = toggleGo.GetComponentInChildren<Toggle>();
            t.SetIsOnWithoutNotify(lobby.syncedFriendlyFire);
        }
        // 更新 Slider: Animals
        if (spawnedSettings.TryGetValue("Animals", out GameObject animalSliderGo))
        {
            Slider s = animalSliderGo.GetComponentInChildren<Slider>();
            // 重点：如果用户正在拖拽这个 Slider，不要用服务器数据覆盖它，否则会“弹回”
            if (Input.GetMouseButton(0) == false) 
            {
                s.SetValueWithoutNotify(lobby.syncedAnimalsNumber);
                animalSliderGo.transform.Find("SliderGroup/SliderValue").GetComponent<TextMeshProUGUI>().text = lobby.syncedAnimalsNumber.ToString();
            }
        }
        // 更新其他设置
        // 参考以下模板补全所有新参数：
        UpdateSliderVisual("WitchHP", lobby.syncedWitchHP);
        UpdateSliderVisual("WitchMana", lobby.syncedWitchMana);
        UpdateSliderVisual("HunterSpeed", lobby.syncedHunterSpeed);
        UpdateSliderVisual("TrapDiff", lobby.syncedTrapDifficulty);
        UpdateSliderVisual("ManaRate", lobby.syncedManaRegen);
        UpdateSliderVisual("HunterRatio", lobby.syncedHunterRatio);
        UpdateSliderVisual("AncientRatio", lobby.syncedAncientRatio);
    }
    // 辅助方法减少重复代码
    private void UpdateSliderVisual(string key, float value)
    {
        if (spawnedSettings.TryGetValue(key, out GameObject go))
        {
            Slider s = go.GetComponentInChildren<Slider>();
            if (Input.GetMouseButton(0) == false) 
            {
                s.SetValueWithoutNotify(value);
                // 根据滑块自身的 wholeNumbers 属性决定显示格式
                string format = s.wholeNumbers ? "F0" : "F2";
                go.transform.Find("SliderGroup/SliderValue").GetComponent<TextMeshProUGUI>().text = value.ToString(format);
            }
        }
    }
}
```

## UI\LobbySkillManager.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Mirror;
public class LobbySkillManager : MonoBehaviour
{
    public static LobbySkillManager Instance;

    [Header("Main Selection Buttons")]
    public Button witchSkill1Btn;
    public Button witchSkill2Btn;
    public Button witchItemBtn;
    public Button hunterSkill1Btn;
    public Button hunterSkill2Btn;

    [Header("Popup Panels")]
    public GameObject witchSkillPanel;
    public GameObject witchItemPanel;
    public GameObject hunterSkillPanel;
    public GameObject uiBlocker; // 拖入全屏透明遮罩

    [Header("Panel Explain Texts")]
    public TextMeshProUGUI witchSkillExplainText;
    public TextMeshProUGUI witchItemExplainText;
    public TextMeshProUGUI hunterSkillExplainText;

    [Header("Choice Button Prefab")]
    public GameObject choiceButtonPrefab;

    [Header("Databases")]
    public List<SkillData> allSkills;
    public List<WitchItemData> allItems;

    [Header("Colors")]
    public Color highlightColor = Color.yellow; // 选中的黄色高亮
    public Sprite defaultEmptyIcon; // 【新增】当没选技能时显示的默认图标（可选）
    private int currentSelectingSlot = -1;

    private void Awake() => Instance = this;

    private void Start()
    {
        CloseAllPanels();
        // --- 【核心修改：设置默认道具】 ---
        InitializeDefaultSettings();
        // 绑定主按钮
        witchSkill1Btn.onClick.AddListener(() => OpenSelectionPanel(0));
        witchSkill2Btn.onClick.AddListener(() => OpenSelectionPanel(1));
        witchItemBtn.onClick.AddListener(() => OpenSelectionPanel(2));
        hunterSkill1Btn.onClick.AddListener(() => OpenSelectionPanel(3));
        hunterSkill2Btn.onClick.AddListener(() => OpenSelectionPanel(4));

        // 绑定全屏遮罩：点击框外关闭
        if (uiBlocker != null)
        {
            uiBlocker.GetComponent<Button>().onClick.AddListener(CloseAllPanels);
        }

        RefreshMainButtonUI();
    }
    private void Update()
    {
        // 如果有任何一个子面板（技能或道具）打开，按 Esc 全部关闭
        if (IsAnyPanelOpen() && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseAllPanels();
        }
    }

    // 【新增方法】
    private void InitializeDefaultSettings()
    {
        if (PlayerSettings.Instance == null) return;

        // 1. 如果女巫道具为空，默认选第一个
        if (string.IsNullOrEmpty(PlayerSettings.Instance.selectedWitchItemName) && allItems.Count > 0)
        {
            PlayerSettings.Instance.selectedWitchItemName = allItems[0].scriptClassName;
            Debug.Log($"[Lobby] 为女巫自动选择了默认道具: {allItems[0].itemName}");
        }
        
        // 2. (可选) 如果你希望技能也有默认值，可以在这里类似处理
        // 但你在 PlayerSettings 里已经预设了 "WitchSkill_Mist" 等，所以通常不需要
    }
    private void OpenSelectionPanel(int slotIndex)
    {
        // 如果点的是当前已经打开的槽位，则关闭它（开关逻辑）
        if (currentSelectingSlot == slotIndex && IsAnyPanelOpen())
        {
            CloseAllPanels();
            return;
        }
        // 第一步：关闭所有已打开的面板，确保互斥
        CloseAllPanels();
        currentSelectingSlot = slotIndex;
        uiBlocker.SetActive(true); // 开启背景检测

        if (slotIndex <= 1)
        {
            witchSkillPanel.SetActive(true);
            PopulatePanel(witchSkillPanel.transform.Find("SkillButtonContainer"), PlayerRole.Witch, witchSkillExplainText);
        }
        else if (slotIndex == 2)
        {
            witchItemPanel.SetActive(true);
            PopulateItemPanel(witchItemPanel.transform.Find("SkillButtonContainer"), witchItemExplainText);
        }
        else
        {
            hunterSkillPanel.SetActive(true);
            PopulatePanel(hunterSkillPanel.transform.Find("SkillButtonContainer"), PlayerRole.Hunter, hunterSkillExplainText);
        }
    }
    private bool IsAnyPanelOpen()
    {
        return witchSkillPanel.activeSelf || witchItemPanel.activeSelf || hunterSkillPanel.activeSelf;
    }
    private void PopulatePanel(Transform container, PlayerRole role, TextMeshProUGUI targetText)
    {
        foreach (Transform child in container) Destroy(child.gameObject);
        if(targetText) targetText.text = "Select your power...";

        var settings = PlayerSettings.Instance;
        // 确定该职业目前选了什么，用于高亮和禁用
        List<string> currentlyEquipped = (role == PlayerRole.Witch) ? settings.selectedWitchSkillNames : settings.selectedHunterSkillNames;

        foreach (var skill in allSkills)
        {
            if (skill.role != role) continue;

            GameObject go = Instantiate(choiceButtonPrefab, container);
            go.transform.Find("Icon").GetComponent<Image>().sprite = skill.icon;
            
            Button btn = go.GetComponent<Button>();
            Outline outline = go.GetComponent<Outline>();

            // --- 核心逻辑：高亮与交互状态 ---
            bool isAlreadySelected = currentlyEquipped.Contains(skill.scriptClassName);
            
            if (isAlreadySelected)
            {
                btn.interactable = false; // 已选中的不能再点
                if (outline != null)
                {
                    outline.effectColor = highlightColor;
                    outline.effectDistance = new Vector2(4, 4); // 展现黄色外框
                }
            }

            btn.onClick.AddListener(() => OnChoiceSelected(skill.scriptClassName));
            
            SkillChoiceHover hover = go.AddComponent<SkillChoiceHover>();
            hover.targetText = targetText; 
            hover.description = $"<color=#FFD700><b>{skill.skillName}</b></color>\n{skill.description}";
        }
    }

    private void PopulateItemPanel(Transform container, TextMeshProUGUI targetText)
    {
        foreach (Transform child in container) Destroy(child.gameObject);
        if(targetText) targetText.text = "Select an item...";

        string equippedItem = PlayerSettings.Instance.selectedWitchItemName;

        foreach (var item in allItems)
        {
            GameObject go = Instantiate(choiceButtonPrefab, container);
            go.transform.Find("Icon").GetComponent<Image>().sprite = item.icon;
            
            Button btn = go.GetComponent<Button>();
            Outline outline = go.GetComponent<Outline>();

            if (item.scriptClassName == equippedItem)
            {
                btn.interactable = false;
                if (outline != null)
                {
                    outline.effectColor = highlightColor;
                    outline.effectDistance = new Vector2(4, 4);
                }
            }

            btn.onClick.AddListener(() => OnChoiceSelected(item.scriptClassName));

            SkillChoiceHover hover = go.AddComponent<SkillChoiceHover>();
            hover.targetText = targetText;
            hover.description = $"<color=#BB88FF><b>{item.itemName}</b></color>\n{item.description}";
        }
    }

    private void OnChoiceSelected(string className)
    {
        var settings = PlayerSettings.Instance;

        switch (currentSelectingSlot)
        {
            case 0: settings.selectedWitchSkillNames[0] = className; break;
            case 1: settings.selectedWitchSkillNames[1] = className; break;
            case 2: 
                settings.selectedWitchItemName = className; 
                // 【核心修复】道具选择后立即同步
                SyncItemToServer(className);
                break;
            case 3: settings.selectedHunterSkillNames[0] = className; break;
            case 4: settings.selectedHunterSkillNames[1] = className; break;
        }
        // 【新增】通知模型预览
        if (LobbyModelPreview.Instance != null)
        {
            LobbyModelPreview.Instance.RefreshItemSelection();
        }
        CloseAllPanels();
        RefreshMainButtonUI();
    }
    // 辅助方法
    // 确保这个方法也在 LobbySkillManager.cs 里的 case 2 逻辑中正确执行
    private void SyncItemToServer(string className)
    {
        // 检查 NetworkClient 是否就绪
        if (NetworkClient.active && NetworkClient.localPlayer != null)
        {
            var pScript = NetworkClient.localPlayer.GetComponent<PlayerScript>();
            if (pScript != null)
            {
                pScript.CmdUpdateSelectedItem(className); // 现在这里可以访问了
            }
        }
    }
    public void RefreshMainButtonUI()
    {
        if (PlayerSettings.Instance == null) return;

        var settings = PlayerSettings.Instance;
        // 更新所有主按钮的显示
        UpdateBtnVisual(witchSkill1Btn, settings.selectedWitchSkillNames[0]);
        UpdateBtnVisual(witchSkill2Btn, settings.selectedWitchSkillNames[1]);
        UpdateBtnVisual(witchItemBtn, settings.selectedWitchItemName);
        UpdateBtnVisual(hunterSkill1Btn, settings.selectedHunterSkillNames[0]);
        UpdateBtnVisual(hunterSkill2Btn, settings.selectedHunterSkillNames[1]);
    }
    private Sprite GetIconByClassName(string className)
    {
        if (string.IsNullOrEmpty(className)) return null;

        // 从技能数据库找
        var skill = allSkills.Find(s => s.scriptClassName == className);
        if (skill != null) return skill.icon;

        // 从道具数据库找
        var item = allItems.Find(i => i.scriptClassName == className);
        if (item != null) return item.icon;

        return null;
    }
    private void UpdateBtnVisual(Button btn, string className)
    {
        if (btn == null) return;

        // 尝试获取子物体中的 Icon Image 和 Text
        Transform iconTrans = btn.transform.Find("Icon");
        Image iconImage = iconTrans != null ? iconTrans.GetComponent<Image>() : null;
        TextMeshProUGUI textComp = btn.GetComponentInChildren<TextMeshProUGUI>();

        Sprite skillIcon = GetIconByClassName(className);

        if (skillIcon != null)
        {
            // 如果找到了图标：显示图标，隐藏文字
            if (iconImage != null)
            {
                iconImage.sprite = skillIcon;
                iconImage.enabled = true;
            }
            if (textComp != null) textComp.enabled = false;
        }
        else
        {
            // 如果没选或没找到图标：隐藏图标，显示文字（显示类名或 None）
            if (iconImage != null) iconImage.enabled = false;
            if (textComp != null)
            {
                textComp.enabled = true;
                textComp.text = "None"; 
            }
        }
    }
    private void UpdateBtnText(Button btn, string className)
    {
        if (btn == null) return;
        var textComp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (textComp != null)
        {
            textComp.text = GetDisplayName(className);
        }
    }

    private string GetDisplayName(string className)
    {
        if (string.IsNullOrEmpty(className)) return "None";
        var skill = allSkills.Find(s => s.scriptClassName == className);
        if (skill != null) return skill.skillName;
        var item = allItems.Find(i => i.scriptClassName == className);
        if (item != null) return item.itemName;
        return className;
    }

    public void CloseAllPanels()
    {
        witchSkillPanel.SetActive(false);
        witchItemPanel.SetActive(false);
        hunterSkillPanel.SetActive(false);
        if (uiBlocker != null) uiBlocker.SetActive(false);
    }
}
```

## UI\Main.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Main : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        GameManager.Instance.getCurrentState();
    }

    // Update is called once per frame
    void Update()
    {
        
    }   
}

```

## UI\Menu.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
public class Menu : MonoBehaviour
{
    NetworkManager manager;
    private void Start()
    {
        manager = FindObjectOfType<NetworkManager>();
    }
    //点击停止按钮
    public void OnClickStopBtn()
    {
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            manager.StopHost();
        }
        else if (NetworkClient.isConnected)
        {
            manager.StopClient();
        }
        else if (NetworkServer.active)
        {
            manager.StopServer();
        }
    }
}

```

## UI\MessageItem.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class MessageItem : MonoBehaviour
{
    // 拖入预制体里的 Text 组件
    public TextMeshProUGUI messageText; 

    public void Setup(string playerName, string message, Color nameColor)
    {
        // 格式化信息，例如： [PlayerName]: Hello World
        // 这里用了富文本 (Rich Text) 来给名字上色
        string hexColor = ColorUtility.ToHtmlStringRGB(nameColor);
        messageText.text = $"<color=#{hexColor}><b>[{playerName}]</b></color>: {message}";
    }
}
```

## UI\MusicManager.cs

```csharp
using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [System.Serializable]
    public struct MusicGroup
    {
        public string groupName;
        [Header("Menu & Lobby")]
        public AudioClip startMenuBGM;
        public AudioClip lobbyRoomBGM;
        [Header("In Game")]
        public AudioClip inGameNormalBGM;
        public AudioClip inGameFastBGM;
    }

    public enum SceneZone { None, Menu, Lobby, Game }

    [Header("BGM Sets (成套配对)")]
    public MusicGroup[] musicGroups;

    [Header("Settings")]
    public float maxVolume = 0.5f;
    public float fadeDuration = 1.5f;
    public float fastModeThreshold = 60f; // 剩余60秒切换

    private AudioSource sourceA;
    private AudioSource sourceB;
    private bool isSourceAActive = true;
    private Coroutine activeFadeRoutine;

    private int currentGroupIndex = -1;
    private SceneZone currentZone = SceneZone.None;
    private bool isFastModeActive = false; // 标记是否已经切到了快节奏音乐
    private bool hasHandledGameOverMusic = false; // 新增变量

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        sourceA = gameObject.AddComponent<AudioSource>();
        sourceB = gameObject.AddComponent<AudioSource>();
        SetupSource(sourceA);
        SetupSource(sourceB);

        SceneManager.activeSceneChanged += OnSceneChanged;
    }

    private void SetupSource(AudioSource source)
    {
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0;
    }

    private void Start()
    {
        HandleBGMForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
    }

    private void OnSceneChanged(Scene oldScene, Scene newScene)
    {
        HandleBGMForScene(newScene.name);
    }

    private void Update()
    {
        // 只有在游戏对局中才需要检测时间切换 BGM
        if (currentZone == SceneZone.Game)
        {
            CheckGameTimer();
            CheckGameOver();
        }
    }

    private void HandleBGMForScene(string sceneName)
    {
        if (musicGroups == null || musicGroups.Length == 0) return;

        SceneZone lastZone = currentZone;
        currentZone = GetZoneForScene(sceneName);

        // 如果是从游戏区回到非游戏区，或者初次进入，则重置随机索引
        if (lastZone == SceneZone.Game || lastZone == SceneZone.None)
        {
            currentGroupIndex = Random.Range(0, musicGroups.Length);
            isFastModeActive = false; // 重置快节奏标记
            Debug.Log($"[Music] Session Reset. Picked Music Group: {musicGroups[currentGroupIndex].groupName}");
        }

        // 播放对应分区的音乐
        if (currentZone == SceneZone.Menu)
        {
            CrossFadeTo(musicGroups[currentGroupIndex].startMenuBGM);
        }
        else if (currentZone == SceneZone.Lobby)
        {
            CrossFadeTo(musicGroups[currentGroupIndex].lobbyRoomBGM);
        }
        else if (currentZone == SceneZone.Game)
        {
            // 刚进游戏场景，播放正常的 InGame BGM
            isFastModeActive = false;
            hasHandledGameOverMusic = false; // 重置游戏结束音乐处理标志
            CrossFadeTo(musicGroups[currentGroupIndex].inGameNormalBGM);
        }
    }

    private void CheckGameTimer()
    {
        // 检查 GameManager 里的计时器
        if (GameManager.Instance != null && !isFastModeActive)
        {
            // 如果计时器小于 60s 且游戏还没结束
            if (GameManager.Instance.gameTimer > 0 && GameManager.Instance.gameTimer <= fastModeThreshold)
            {
                if (GameManager.Instance.CurrentState == GameManager.GameState.InGame)
                {
                    isFastModeActive = true;
                    Debug.Log("[Music] Time Running Out! Switching to Fast BGM.");
                    CrossFadeTo(musicGroups[currentGroupIndex].inGameFastBGM);
                }
            }
        }
    }

    private void CheckGameOver()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            if (!hasHandledGameOverMusic) // 增加判断
            {
                hasHandledGameOverMusic = true;
                Debug.Log("[Music] Victory Zone Entered. Fading out In-Game BGM.");
                CrossFadeTo(null);
            }
        }
    }

    private SceneZone GetZoneForScene(string sceneName)
    {
        if (sceneName.StartsWith("MyScene")) return SceneZone.Game;
        if (sceneName == "LobbyRoom") return SceneZone.Lobby;
        return SceneZone.Menu; // StartMenu, ConnectRoom 等
    }

    public void CrossFadeTo(AudioClip newClip)
    {
        AudioSource activeSource = isSourceAActive ? sourceA : sourceB;
        if (activeSource.clip == newClip && activeSource.isPlaying && activeSource.volume > 0) return;

        if (activeFadeRoutine != null) StopCoroutine(activeFadeRoutine);
        activeFadeRoutine = StartCoroutine(CrossFadeRoutine(newClip));
    }

    private IEnumerator CrossFadeRoutine(AudioClip newClip)
    {
        AudioSource fadeInSource = isSourceAActive ? sourceB : sourceA;
        AudioSource fadeOutSource = isSourceAActive ? sourceA : sourceB;

        if (newClip != null)
        {
            fadeInSource.clip = newClip;
            fadeInSource.Play();
        }

        float timer = 0;
        float startOutVol = fadeOutSource.volume;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float t = timer / fadeDuration;

            fadeOutSource.volume = Mathf.Lerp(startOutVol, 0, t);
            if (newClip != null) fadeInSource.volume = Mathf.Lerp(0, maxVolume, t);

            yield return null;
        }

        fadeOutSource.volume = 0;
        fadeOutSource.Stop();
        fadeOutSource.clip = null;
        
        if (newClip != null) fadeInSource.volume = maxVolume;

        isSourceAActive = !isSourceAActive;
        activeFadeRoutine = null;
    }
}
```

## UI\PlayerOutline.cs

```csharp
using UnityEngine;
using System.Collections.Generic;

public class PlayerOutline : MonoBehaviour
{
    [SerializeField] private Renderer targetRenderer; 
    [SerializeField] private Material outlineMaterialSource; 
    // 新增：需要排除的对象（比如名字文本物体）
    [SerializeField] private GameObject nameTextObject; 
    private Material outlineInstance;
    private bool isVisible = false;

    void Awake()
    {
        // 自动查找逻辑增强
        if (targetRenderer == null) 
        {
            // 尝试获取模型上的 Renderer，而不是随便找一个
            // 假设你的模型在名为 "Model" 或 "Visual" 的子物体下
            var allRenderers = GetComponentsInChildren<Renderer>();
            foreach (var r in allRenderers)
            {
                // 排除名字文本的 Renderer
                if (nameTextObject != null && r.transform.IsChildOf(nameTextObject.transform)) continue;
                // 排除 UI 或 TextMeshPro 的 Renderer
                if (r.gameObject.name.Contains("Name") || r.gameObject.name.Contains("Text")) continue;

                targetRenderer = r;
                break;
            }
        }

        if (outlineMaterialSource != null)
        {
            outlineInstance = new Material(outlineMaterialSource);
        }
    }

    public void SetOutline(bool active, Color color)
    {
        // --- 【新增：游戏结束强制关闭】 ---
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            active = false;
        }
        // ---------------------------------
        if (targetRenderer == null || outlineInstance == null) return;

        // 检查材质是否丢失
        bool materialLost = active && !System.Array.Exists(targetRenderer.sharedMaterials, m => m == outlineInstance);

        // --- 修改这里：即使isVisible没变，但只要是激活状态，就应该更新颜色 ---
        if (active)
        {
            // 总是更新颜色，防止状态切换（如：队友状态 -> 被抓状态）时颜色不刷新
            outlineInstance.SetColor("_OutlineColor", color);
            
            // 如果状态变了或者是材质丢了，才去操作材质列表
            if (!isVisible || materialLost)
            {
                isVisible = true;
                AddMaterial(outlineInstance);
            }
        }
        else
        {
            // 如果当前是可见的，现在要关闭，才执行移除
            if (isVisible)
            {
                isVisible = false;
                RemoveMaterial(outlineInstance);
            }
        }
    }

    private void AddMaterial(Material mat)
    {
        if (targetRenderer == null || mat == null) return;
        
        // 使用 sharedMaterials 避开 Prefab 访问限制
        Material[] currentShared = targetRenderer.sharedMaterials;
        List<Material> matsList = new List<Material>(currentShared);

        if (!matsList.Contains(mat))
        {
            matsList.Add(mat);
            targetRenderer.materials = matsList.ToArray(); // 赋值给 .materials 会处理实例化
        }
    }

    private void RemoveMaterial(Material mat)
    {
        if (targetRenderer == null) return;
        Material[] currentShared = targetRenderer.sharedMaterials;
        List<Material> matsList = new List<Material>(currentShared);

        if (matsList.Contains(mat))
        {
            matsList.Remove(mat);
            targetRenderer.materials = matsList.ToArray();
        }
    }

    public void RefreshRenderer(Renderer newRenderer)
    {
        if (newRenderer == null) return;
        
        // 增加一个安全检查：确保新传入的不是名字物体
        if (nameTextObject != null && newRenderer.transform.IsChildOf(nameTextObject.transform)) return;

        // 如果当前正在显示高亮，先移除旧的引用（如果旧的没被销毁）
        // 使用 try-catch 或 null 检查防止因物体已 Destroy 导致的报错
        if (isVisible && targetRenderer != null)
        {
            try { RemoveMaterial(outlineInstance); } catch { }
        }

        targetRenderer = newRenderer;

        if (isVisible)
        {
            AddMaterial(outlineInstance);
        }
    }

    void OnDestroy()
    {
        if (outlineInstance != null) Destroy(outlineInstance);
    }
}
```

## UI\PlayerRowUI.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro; // 如果你用TextMeshPro

public class PlayerRowUI : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI statusText;
    public UnityEngine.UI.Image readyCheckImage; // 【新增】拖入你新加的勾勾图片
    public TextMeshProUGUI pingText; // 【新增】拖入显示 Ping 的 TMP 文本
    public Button actionButton;       // 对应 Prefab 里的按钮
    public TextMeshProUGUI actionButtonText;     // 对应按钮里面的文字 (用于显示 Ready / Cancel)

    [Header("Inline Edit")]
    public Button btnEdit;                      // 小修改按鈕 (✎)
    public TMP_InputField nameInputField;       // 與 nameText 重疊的輸入框
    public GameObject nameContainer;            // 可選：包住 nameText + btnEdit 的容器

    private PlayerScript boundPlayer;      // 記住這行對應哪個玩家
    private bool isEditingNow = false; // 【新增】标记位
    private void Awake()
    {
        if (btnEdit != null)
        {
            btnEdit.onClick.AddListener(StartEditingName);
            btnEdit.gameObject.SetActive(false);   // 一開始隱藏
        }

        if (nameInputField != null)
        {
            nameInputField.gameObject.SetActive(false);
            nameInputField.onEndEdit.AddListener(OnNameInputEndEdit);
            // 可選：按 Escape 取消
            // nameInputField.onDeselect.AddListener(...);
        }
    }

    // 更新这一行的显示内容
    public void UpdateInfo(string playerName, bool isReady, bool isLocalPlayer,int ping) // 【修改】增加 ping 参数
    {
        // 名字显示
        if (!isEditingNow)
        {
            nameText.text = playerName + (isLocalPlayer ? " (You)" : "");
        }
        // nameText.color = isLocalPlayer ? Color.green : Color.white;

        // 状态显示
        // --- 【核心修改：状态切换逻辑】 ---
        if (isReady)
        {
            // 准备好了：显示图片，隐藏文本（或者清空文本）
            statusText.text = ""; 
            if (readyCheckImage != null) readyCheckImage.gameObject.SetActive(true);
        }
        else
        {
            // 未准备：显示红色 X，隐藏图片
            statusText.text = "<color=red><b>X</b></color>";
            if (readyCheckImage != null) readyCheckImage.gameObject.SetActive(false);
        }
        // 【新增】显示延迟逻辑
        if (pingText != null)
        {
            pingText.text = ping + "ms";
            // 根据延迟改变颜色
            if (ping < 80) pingText.color = Color.green;
            else if (ping < 150) pingText.color = Color.yellow;
            else pingText.color = Color.red;
        }        
        // 如果这行是本地玩家，我们需要更新按钮上的文字
        if (isLocalPlayer && actionButtonText != null)
        {
            actionButtonText.text = isReady ? "Cancel" : "Ready";
        }
        // 只對本地玩家顯示編輯按鈕
        if (btnEdit != null)
        {
            btnEdit.gameObject.SetActive(isLocalPlayer);
        }

        // // 確保編輯中狀態被重置（斷線重連等情況）
        // if (nameInputField != null && nameInputField.gameObject.activeSelf)
        // {
        //     StopEditing();
        // }
    }
    // 讓 LobbyScript 呼叫，綁定對應的 PlayerScript
    public void BindToPlayer(PlayerScript player)
    {
        boundPlayer = player;
    }

    private void StartEditingName()
    {
        if (boundPlayer == null || nameText == null || nameInputField == null) return;
        isEditingNow = true; // 【新增】标记开始编辑

        // 1. 把當前名字填入輸入框
        nameInputField.text = boundPlayer.playerName;

        // 2. 隱藏文字，顯示輸入框
        nameText.gameObject.SetActive(false);
        btnEdit.gameObject.SetActive(false);           // 編輯中隱藏按鈕
        nameInputField.gameObject.SetActive(true);

        // 3. 自動聚焦 + 全選
        nameInputField.ActivateInputField();
        nameInputField.Select();
    }

    private void OnNameInputEndEdit(string newName)
    {
        StopEditing();

        if (boundPlayer == null) return;

        newName = newName.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            // 可選擇不允許空名字，或保持原名
            return;
        }

        if (newName.Length > 16) newName = newName.Substring(0, 16);

        boundPlayer.CmdChangePlayerName(newName);
    }

    private void StopEditing()
    {
        isEditingNow = false; // 【新增】标记结束编辑
        if (nameText != null) nameText.gameObject.SetActive(true);
        if (btnEdit != null && boundPlayer != null && boundPlayer.isLocalPlayer)
        {
            btnEdit.gameObject.SetActive(true);
        }
        if (nameInputField != null) nameInputField.gameObject.SetActive(false);
    }

    // 可選：按 Escape 取消編輯
    private void Update()
    {
        if (nameInputField != null && nameInputField.gameObject.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                StopEditing();
            }
        }
    }
}
```

## UI\PlayMenu.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;


public class PlayMenu : MonoBehaviour
{
    public void ButtonLoadStartMenu()
    {
        SceneManager.LoadScene("StartMenu");
    } 
}

```

## UI\RandomAnimationPlayer.cs

```csharp
using UnityEngine;

public class RandomAnimationPlayer : MonoBehaviour
{
    private Animator animator;
    public string[] stateNames = { "sad_idle", "sad_idle 0", "sad_idle 1" };
    
    void Awake()
    {
        // 【关键修改】使用 GetComponentInChildren 确保能找到子物体上的 Animator
        animator = GetComponentInChildren<Animator>();
    }

    void OnEnable()
    {
        // 增加空检查
        if (animator != null) PlayRandom();
    }

    void Update()
    {
        if (animator == null) return;

        // 检查当前动画层 0 是否播放完毕
        if (animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.95f && !animator.IsInTransition(0))
        {
            PlayRandom();
        }
    }

    public void PlayRandom()
    {
        if (animator == null || stateNames == null || stateNames.Length == 0) return;

        int index = Random.Range(0, stateNames.Length);
        animator.CrossFade(stateNames[index], 0.25f);
    }
}
```

## UI\removebg.py

```python
from PIL import Image
from rembg import remove

input_path = r"Assets/Image/UI/progressbar.png"
output_path = "Assets/Image/UI/progressbar1.png"

# 打开图片
input_image = Image.open(input_path)

# 移除背景
output_image = remove(input_image)

# 保存为 PNG（必须是 PNG 才能保留透明度）
output_image.save(output_path)
print(f"背景已移除，保存为 {output_path}")

```

## UI\RewardUI.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

public class RewardUI : MonoBehaviour
{
    public static RewardUI Instance;
    public GameObject panel;
    public Button[] optionButtons;
    public TextMeshProUGUI[] optionTexts;

    private RewardOption[] currentOptions;

    void Awake() { Instance = this; panel.SetActive(false); }

    public void Show(RewardOption[] options)
    {
        currentOptions = options;
        panel.SetActive(true);
        
        // 解锁鼠标
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        for (int i = 0; i < 3; i++)
        {
            int index = i;
            optionTexts[i].text = $"<b>{options[i].title}</b>\n{options[i].description}";
            optionButtons[i].onClick.RemoveAllListeners();
            optionButtons[i].onClick.AddListener(() => OnClickOption(index));
        }
    }

    void OnClickOption(int index)
    {
        // 告知服务器我们的选择
        var localWitch = NetworkClient.localPlayer.GetComponent<WitchPlayer>();
        // 发送点击的索引 (0, 1, 或 2)
        localWitch.CmdSelectReward(index); 

        panel.SetActive(false);
        
        // 恢复鼠标锁定
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
```

## UI\RoomItemUI.cs

```csharp
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
```

## UI\SceneScript.cs

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.Video; // 必须引用
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
    [Header("Video Settings")]
    public VideoPlayer victoryVideoPlayer; // 在 Inspector 中拖入 VideoPlayer 组件
    public RawImage videoDisplay;         // 拖入用于显示视频的 RawImage
    public float videoFadeSpeed = 1.5f;    // 音频淡入淡出速度
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
    // 提供给 GameManager 调用的接口
    public void PlayVictoryVideo(float duration)
    {
        if (victoryVideoPlayer == null || videoDisplay == null) return;
        
        StartCoroutine(VideoPlaybackRoutine(duration));
    }
    private IEnumerator VideoPlaybackRoutine(float duration)
    {
        // 1. 准备阶段
        videoDisplay.gameObject.SetActive(true);
        victoryVideoPlayer.targetCameraAlpha = 1f;
        victoryVideoPlayer.SetDirectAudioVolume(0, 0f); // 初始音量 0
        victoryVideoPlayer.Play();

        // 2. 音频淡入
        float timer = 0f;
        while (timer < 1.0f)
        {
            timer += Time.deltaTime * videoFadeSpeed;
            victoryVideoPlayer.SetDirectAudioVolume(0, Mathf.Lerp(0f, 1f, timer));
            yield return null;
        }

        // 3. 等待视频播放（扣除淡入淡出的时间）
        yield return new WaitForSeconds(duration - 2.0f);

        // 4. 音频淡出
        timer = 0f;
        while (timer < 1.0f)
        {
            timer += Time.deltaTime * videoFadeSpeed;
            victoryVideoPlayer.SetDirectAudioVolume(0, Mathf.Lerp(1f, 0f, timer));
            yield return null;
        }

        // 5. 清理阶段
        victoryVideoPlayer.Stop();
        videoDisplay.gameObject.SetActive(false);
    }
}
```

## UI\SkillButtonUI.cs

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

public class SkillButtonUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private SkillData skillData;
    private SkillSelectionManager manager;

    // 由 Manager 在生成按钮时调用，初始化数据
    public void Setup(SkillData data, SkillSelectionManager selectionManager)
    {
        skillData = data;
        manager = selectionManager;
    }

    // 鼠标进入时触发
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (manager != null && skillData != null)
        {
            manager.ShowDescription(skillData);
        }
    }

    // 鼠标离开时触发
    public void OnPointerExit(PointerEventData eventData)
    {
        // if (manager != null)
        // {
        //     manager.ClearDescription();
        // }
    }
}
```

## UI\SkillChoiceHover.cs

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class SkillChoiceHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public TextMeshProUGUI targetText; // 指向所属面板的 ExplainText
    public string description;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (targetText != null)
        {
            targetText.text = description;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (targetText != null)
        {
            targetText.text = "Select your power...";
        }
    }
}
```

## UI\StartMenu.cs

```csharp
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
```

## UI\StartMenuVisuals.cs

```csharp
using UnityEngine;
using System.Collections.Generic;

public class StartMenuVisuals : MonoBehaviour
{
    [Header("树木设置")]
    public Transform treesParent; // 拖入层级中的 Vegetation/Trees 对象

    [Header("相机旋转设置")]
    public Transform cameraTransform; // 拖入 Main Camera
    public float rotationSpeed = 2.0f; // 旋转速度（度/秒）
    public Vector3 rotationAxis = Vector3.up; // 绕 Y 轴旋转

    void Start()
    {
        RandomizeTrees();
    }

    void Update()
    {
        RotateCamera();
    }

    private void RandomizeTrees()
    {
        if (treesParent == null)
        {
            Debug.LogWarning("[Visuals] 未指定 Trees Parent，无法执行随机隐藏。");
            return;
        }

        // 1. 获取所有子物体（树）
        List<GameObject> allTrees = new List<GameObject>();
        foreach (Transform child in treesParent)
        {
            allTrees.Add(child.gameObject);
        }

        if (allTrees.Count == 0) return;

        // 2. 打乱列表顺序 (洗牌算法)
        for (int i = 0; i < allTrees.Count; i++)
        {
            GameObject temp = allTrees[i];
            int randomIndex = Random.Range(i, allTrees.Count);
            allTrees[i] = allTrees[randomIndex];
            allTrees[randomIndex] = temp;
        }

        // 3. 隐藏前一半的树
        int countToHide = allTrees.Count / 2;
        for (int i = 0; i < countToHide; i++)
        {
            allTrees[i].SetActive(false);
        }

        Debug.Log($"[Visuals] 初始树木总数: {allTrees.Count}, 已隐藏: {countToHide}");
    }

    private void RotateCamera()
    {
        if (cameraTransform == null) return;

        // 缓慢进行 360 度循环旋转
        cameraTransform.Rotate(rotationAxis, rotationSpeed * Time.deltaTime, Space.World);
    }
}
```

## UI\TabInfoManager.cs

```csharp
using UnityEngine;
using System.Collections.Generic;

public class TabInfoManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject tabInfoPanel;      // 对应你的 TabInfoPanel
    public Transform rowContainer;       // TabInfoGroup 生成的父物体 (如果没有 LayoutGroup 建议加一个)
    public GameObject tabRowPrefab;      // 你的 TabInfoGroup 预制体

    private Dictionary<GamePlayer, TabRowUI> activeRows = new Dictionary<GamePlayer, TabRowUI>();
    [Header("Data")]
    public List<SkillData> skillDatabase; // 在 Inspector 中拖入所有技能的 ScriptableObject
    private void Start()
    {
        // 初始关闭
        tabInfoPanel.SetActive(false);
    }

    private void Update()
    {
        // 检测 Tab 键
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            TogglePanel(true);
        }
        if (Input.GetKeyUp(KeyCode.Tab))
        {
            TogglePanel(false);
        }

        // 如果面板打开着，实时刷新数据
        if (tabInfoPanel.activeSelf)
        {
            RefreshData();
        }
    }

    private void TogglePanel(bool show)
    {
        tabInfoPanel.SetActive(show);
        if (show)
        {
            RefreshData();
        }
    }

    private void RefreshData()
    {
        // 1. 清理已退出的玩家行
        List<GamePlayer> toRemove = new List<GamePlayer>();
        foreach (var pair in activeRows)
        {
            if (pair.Key == null) toRemove.Add(pair.Key);
        }
        foreach (var key in toRemove)
        {
            Destroy(activeRows[key].gameObject);
            activeRows.Remove(key);
        }

        // 2. 更新或生成所有玩家的信息
        foreach (var player in GamePlayer.AllPlayers)
        {
            if (player == null) continue;

            if (!activeRows.ContainsKey(player))
            {
                // 生成新行
                GameObject newRow = Instantiate(tabRowPrefab, rowContainer);
                TabRowUI script = newRow.GetComponent<TabRowUI>();
                activeRows.Add(player, script);
            }

            // 【关键修改】传递数据库引用
            activeRows[player].UpdateRow(player, skillDatabase);
        }
    }
}
```

## UI\TabRowUI.cs

```csharp
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;

public class TabRowUI : MonoBehaviour
{
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI playerRoleText;
    public TextMeshProUGUI playerPingText;
    [Header("Skill UI")]
    public Image skill1Image; // 拖入子物体 Skill1
    public Image skill2Image; // 拖入子物体 Skill2

    public void UpdateRow(GamePlayer player, List<SkillData> database)
    {
        // 更新名字
        playerNameText.text = player.playerName;
        
        // 更新角色 (根据阵营显示不同颜色)
        playerRoleText.text = player.playerRole.ToString();
        playerRoleText.color = player.playerRole == PlayerRole.Witch ? Color.magenta : Color.cyan;

        // 更新 Ping
        playerPingText.text = player.ping + "ms";
        // --- 【核心修改：设置技能图标】 ---
        SetSkillIcon(skill1Image, player.syncedSkill1Name, database);
        SetSkillIcon(skill2Image, player.syncedSkill2Name, database);
        // Ping 颜色反馈
        if (player.ping < 80) playerPingText.color = Color.green;
        else if (player.ping < 150) playerPingText.color = Color.yellow;
        else playerPingText.color = Color.red;

        // 如果玩家永久死亡，可以将整行变灰（可选）
        if (player.isPermanentDead)
        {
            playerNameText.text += " (Dead)";
            playerNameText.alpha = 0.5f;
        }
    }
    private void SetSkillIcon(Image targetImg, string className, List<SkillData> database)
    {
        if (targetImg == null) return;

        if (string.IsNullOrEmpty(className))
        {
            targetImg.gameObject.SetActive(false);
            return;
        }

        // 从数据库中查找匹配类名的 SkillData
        SkillData data = database.Find(d => d.scriptClassName == className);
        if (data != null && data.icon != null)
        {
            targetImg.sprite = data.icon;
            targetImg.gameObject.SetActive(true);
        }
        else
        {
            targetImg.gameObject.SetActive(false);
        }
    }
}
```

## UI\TeamVision.cs

```csharp
using UnityEngine;
using Mirror;
using System.Collections;

public class TeamVision : NetworkBehaviour
{
    [Header("阵营颜色")]
    public Color witchColor = Color.magenta;
    public Color hunterColor = Color.cyan;
    public Color enemyColor = Color.red; // 可选：敌人的颜色

    [Header("设置")]
    public float checkInterval = 0.5f; // 每0.5秒刷新一次，节省性能

    private GamePlayer localPlayer;

    // 【新增】标记当前是否正在强制显示猎人（防止逻辑竞争）
    private bool isEffectRevealingHunters = false;
    public override void OnStartLocalPlayer()
    {
        localPlayer = GetComponent<GamePlayer>();
        // --- 修复：本地玩家不应该看到自己的名字标签 ---
        if (localPlayer.nameText != null)
        {
            localPlayer.nameText.gameObject.SetActive(false);
        }
        StartCoroutine(VisionRoutine());
    }

    private IEnumerator VisionRoutine()
    {
        while (true)
        {
            UpdateAllOutlines();
            yield return new WaitForSeconds(checkInterval);
        }
    }

    public IEnumerator TempShowEnemies(float duration)
    {
        isEffectRevealingHunters = true; // 开启强制显示标记
        
        // 立即刷新一次
        UpdateAllOutlines();

        yield return new WaitForSeconds(duration);

        isEffectRevealingHunters = false; // 关闭强制显示
        
        // 效果结束立即刷新，清除红色描边
        UpdateAllOutlines();
    }

    private void ForceShowHuntersOnce()
    {
        foreach (var p in GamePlayer.AllPlayers)
        {
            if (p != null && p.playerRole == PlayerRole.Hunter)
            {
                var outline = p.GetComponent<PlayerOutline>();
                if (outline) outline.SetOutline(true, Color.red); // 强制显示红色描边
            }
        }
    }
    // private void UpdateAllOutlines()
    // {
    //     if (localPlayer == null) return;
        
    //     foreach (var targetPlayer in GamePlayer.AllPlayers)
    //     {
    //         if (targetPlayer == null || targetPlayer == localPlayer) continue;

    //         var outline = targetPlayer.GetComponent<PlayerOutline>();
    //         if (outline == null) continue;

    //         // 获取同步变量
    //         bool isTrapped = targetPlayer.isTrappedByNet;
    //         bool IAmHunter = (localPlayer.playerRole == PlayerRole.Hunter);
    //         bool isTeammate = (targetPlayer.playerRole == localPlayer.playerRole);
            

    //         // --- 核心逻辑优先级：被抓状态高于一切 ---
    //         if (isTrapped)
    //         {
    //             // 只要被抓了，不管是猎人看她，还是女巫队友看她，全部显示红色
    //             // 这样队友也能意识到“糟糕，她被抓了，需要掩护/解救”
    //             outline.SetOutline(true, Color.red);
    //             // if (targetPlayer.nameText != null) targetPlayer.nameText.gameObject.SetActive(false);
    //             continue; 
    //         }

    //         // --- 正常的队友显示逻辑 ---
    //         if (localPlayer.playerRole != PlayerRole.None && isTeammate)
    //         {
    //             Color c = (targetPlayer.playerRole == PlayerRole.Witch) ? witchColor : hunterColor;
    //             outline.SetOutline(true, c);
                
    //             if (targetPlayer.nameText != null)
    //             {
    //                 bool shouldShowName = !(targetPlayer is WitchPlayer w && w.isMorphed);
    //                 targetPlayer.nameText.gameObject.SetActive(shouldShowName);
    //                 targetPlayer.nameText.color = Color.green;
    //             }
    //         }
    //         // --- 正常的敌对显示逻辑 ---
    //         else
    //         {
    //             outline.SetOutline(false, Color.white);
    //             if (targetPlayer.nameText != null) targetPlayer.nameText.gameObject.SetActive(false);
    //         }
    //     }
    //     // 2. --- 处理已发现树木的常驻高亮 ---
    //     if (localPlayer.playerRole == PlayerRole.Witch)
    //     {
    //         PropTarget[] allProps = Object.FindObjectsOfType<PropTarget>();
    //         foreach (var prop in allProps)
    //         {
    //             if (prop == null) continue;

    //             // 如果是被发现的静态树，强制开启高亮渲染。
    //             // SetHighlight(false) 传入 false 是因为此时准星没指着它，
    //             // 但内部逻辑会因为 isScouted 为 true 而决定继续显示高亮。
    //             // 判定逻辑里应包含临时状态 (由于 PropTarget.SetHighlight 已经改了，这里只需确保调用)
    //             if ((prop.isScouted || prop.isLocalTempRevealed) && (prop.isStaticTree || prop.isAncientTree))
    //             {
    //                 prop.SetHighlight(false); 
    //             }
    //         }
    //     }
    // }

    private void UpdateAllOutlines()
    {
        if (localPlayer == null) return;
        // --- 【新增：胜利区域清理逻辑】 ---
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            // 1. 清理所有玩家的描边
            foreach (var p in GamePlayer.AllPlayers)
            {
                if (p != null)
                {
                    var outline = p.GetComponent<PlayerOutline>();
                    if (outline != null) outline.SetOutline(false, Color.clear);
                    if (p.nameText != null) p.nameText.gameObject.SetActive(true);
                }
            }
            // 2. 清理场景中所有道具/树木的高亮
            PropTarget[] allProps = Object.FindObjectsOfType<PropTarget>();
            foreach (var prop in allProps)
            {
                if (prop != null) prop.SetHighlight(false);
            }
            return; // 直接跳出，不再执行后续的高亮逻辑
        }
        // ---------------------------------

        // 1. 处理玩家描边
        foreach (var targetPlayer in GamePlayer.AllPlayers)
        {
            if (targetPlayer == null || targetPlayer == localPlayer) continue;
            // 关键补丁：如果角色还没同步好(None)，跳过本次循环，等下一秒再试
            if (targetPlayer.playerRole == PlayerRole.None) continue;
            var outline = targetPlayer.GetComponent<PlayerOutline>();
            if (outline == null) continue;

            bool isTrapped = targetPlayer.isTrappedByNet;
            bool isTeammate = (targetPlayer.playerRole == localPlayer.playerRole);
            bool isTargetHunter = (targetPlayer.playerRole == PlayerRole.Hunter);

            // --- 优先级逻辑 ---
            if (isTrapped)
            {
                outline.SetOutline(true, Color.red);
            }
            // 【核心修复】如果是猎人且正处于“奖励透视期”
            else if (isTargetHunter && isEffectRevealingHunters)
            {
                outline.SetOutline(true, Color.red);
            }
            else if (isTeammate)
            {
                // 队友：显示名字（如果是女巫且变身中则隐藏）
                if (targetPlayer.nameText != null)
                {
                    bool shouldShowName = !(targetPlayer is WitchPlayer w && w.isMorphed);
                    targetPlayer.nameText.gameObject.SetActive(shouldShowName);
                }
                Color c = (targetPlayer.playerRole == PlayerRole.Witch) ? witchColor : hunterColor;
                outline.SetOutline(true, c);
                // 即使脚本被别人关了，这里也强行打开
                if (!outline.enabled) outline.enabled = true; 
                outline.SetOutline(true, c);
            }
            else
            {
                // 敌人：强制隐藏名字
                if (targetPlayer.nameText != null) 
                {
                    targetPlayer.nameText.gameObject.SetActive(false);
                }
                // 正常敌对状态（非透视期且未被抓），关闭描边
                outline.SetOutline(false, Color.white);
            }
        }

        // 2. 处理树木描边
        if (localPlayer.playerRole == PlayerRole.Witch)
        {
            PropTarget[] allProps = Object.FindObjectsOfType<PropTarget>();
            foreach (var prop in allProps)
            {
                if (prop == null || (!prop.isStaticTree && !prop.isAncientTree)) continue;

                // 【核心修复】始终调用 SetHighlight，让内部去根据最新状态决定是否显示
                // 内部条件 active || isScouted || isLocalTempRevealed 只要全为 false，高亮就会消失
                prop.SetHighlight(false); 
            }
        }
    }

    public void ForceUpdateVisuals()
    {
        UpdateAllOutlines();
    }

}
```

## UI\UIButtonEffects.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class UIButtonEffects : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    private Vector3 initialScale;
    public float hoverScale = 1.05f;    // 悬浮时放大倍数
    public float pressScale = 0.95f;    // 按下时缩小倍数
    
    [Header("Color Tint (Optional)")]
    public Image targetImage;           // 按钮的背景图
    public Color hoverColor = Color.white;
    public Color pressColor = new Color(0.7f, 0.7f, 0.7f);
    private Color originalColor;

    private void Awake()
    {
        initialScale = transform.localScale;
        if (targetImage == null) targetImage = GetComponent<Image>();
        if (targetImage != null) originalColor = targetImage.color;
    }

    // 鼠标移入
    public void OnPointerEnter(PointerEventData eventData)
    {
        transform.localScale = initialScale * hoverScale;
        // 如果想做发光效果，可以在这里开启一个隐藏的 Glow 图片
        AudioManager.Instance?.Play2D("UI选择");
    }

    // 鼠标移出
    public void OnPointerExit(PointerEventData eventData)
    {
        transform.localScale = initialScale;
        if (targetImage != null) targetImage.color = originalColor;
    }

    // 鼠标按下
    public void OnPointerDown(PointerEventData eventData)
    {
        transform.localScale = initialScale * pressScale;
        if (targetImage != null) targetImage.color = pressColor;
        AudioManager.Instance?.Play2D("UI点击（木头）");
    }

    // 鼠标抬起
    public void OnPointerUp(PointerEventData eventData)
    {
        transform.localScale = initialScale * hoverScale;
        if (targetImage != null) targetImage.color = hoverColor;
    }
}
```

## UI\UIButtonRotate.cs

```csharp
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class UIButtonRotate : MonoBehaviour
{
    [Header("旋转设置")]
    public float duration = 0.5f; // 旋转一圈需要的时间
    private bool isRotating = false;

    // 这个方法绑定到 Button 的 OnClick 事件
    public void StartRotate()
    {
        if (!isRotating)
        {
            StartCoroutine(RotateRoutine());
        }
    }

    private IEnumerator RotateRoutine()
    {
        isRotating = true;
        float elapsed = 0f;
        
        // 记录初始旋转
        Quaternion startRotation = transform.localRotation;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float percent = elapsed / duration;
            
            // 顺时针旋转一圈 (从0度到-360度)
            // 如果想逆时针，把 -360 改成 360
            float zRotation = Mathf.Lerp(0, -360f, percent);
            transform.localRotation = Quaternion.Euler(0, 0, zRotation);
            
            yield return null;
        }

        // 确保最后旋转角度精准回到0
        transform.localRotation = startRotation;
        isRotating = false;
    }
}
```

## UI\UIImageSpinner.cs

```csharp
using System.Collections;
using UnityEngine;

public class UIImageSpinner : MonoBehaviour
{
    [Header("旋转设置")]
    public float duration = 1.0f;     // 转一圈所需时间
    public bool autoStart = true;     // 是否脚本启动时就开始转
    public bool isLooping = true;     // 是否循环不停
    public bool clockwise = true;     // 是否顺时针

    private RectTransform rectTransform;
    private Coroutine spinCoroutine;

    private void Awake()
    {
        // UI物体必须使用 RectTransform
        rectTransform = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        if (autoStart)
        {
            StartSpinning();
        }
    }

    // --- 外部调用接口 ---

    // 开始旋转
    public void StartSpinning()
    {
        if (spinCoroutine == null)
        {
            spinCoroutine = StartCoroutine(SpinRoutine());
        }
    }

    // 停止旋转
    public void StopSpinning()
    {
        if (spinCoroutine != null)
        {
            StopCoroutine(spinCoroutine);
            spinCoroutine = null;
        }
    }

    // --- 内部逻辑 ---

    private IEnumerator SpinRoutine()
    {
        float direction = clockwise ? -360f : 360f;
        
        while (true)
        {
            float elapsed = 0f;
            // 每次循环前重置角度，防止数值无限叠加导致精度问题
            rectTransform.localRotation = Quaternion.identity;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float percent = elapsed / duration;
                
                // 计算当前旋转
                rectTransform.localRotation = Quaternion.Euler(0, 0, percent * direction);
                yield return null;
            }

            if (!isLooping) break; // 如果不循环，转完一圈跳出
        }

        rectTransform.localRotation = Quaternion.Euler(0, 0, 0);
        spinCoroutine = null;
    }
}
```

## UI\VictoryAnimData.cs

```csharp
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct GroupDanceConfig
{
    public string danceName; // 新增：方便在编辑器里辨认（如 "Witch Party A"）
    public int playerCount; 
    public RuntimeAnimatorController[] individualAnimators; 
    public AudioClip victoryMusic; // <--- 新增：该人数舞蹈对应的背景音乐
}

[CreateAssetMenu(fileName = "VictoryAnimData", menuName = "Game/Victory Animation Data")]
public class VictoryAnimData : ScriptableObject
{
    [Header("相机配置资源")]
    public CameraData cameraSettings; // <--- 关键修改：直接拖入你的 CameraData 资源

    [Header("群舞配置列表")]
    public List<GroupDanceConfig> groupDances;

    // 【核心修改】由服务器调用：查找匹配人数的所有索引，并随机选一个
    public int GetRandomConfigIndex(int count)
    {
        List<int> matchingIndices = new List<int>();

        for (int i = 0; i < groupDances.Count; i++)
        {
            if (groupDances[i].playerCount == count)
            {
                matchingIndices.Add(i);
            }
        }

        if (matchingIndices.Count > 0)
        {
            // 随机选择一个匹配项的索引
            return matchingIndices[Random.Range(0, matchingIndices.Count)];
        }

        return -1; // 未找到匹配项
    }

    // 供 RPC 调用：根据索引获取特定配置
    public GroupDanceConfig GetConfigByIndex(int index)
    {
        if (index >= 0 && index < groupDances.Count)
            return groupDances[index];
        
        return groupDances.Count > 0 ? groupDances[0] : default;
    }
}
```

## UI\VictoryNameFollow.cs

```csharp
using UnityEngine;
using TMPro;

public class VictoryNameFollow : MonoBehaviour
{
    public Transform targetBone; // 要跟随的骨骼（如 Head）
    public Vector3 offset = new Vector3(0, 0.1f, 0); // 头顶偏移量

    void LateUpdate()
    {
        if (targetBone != null)
        {
            // 每一帧同步骨骼位置
            transform.position = targetBone.position + offset;

            // 保持文字始终面向相机
            if (Camera.main != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position);
            }
        }
    }
}
```

## UI\WitchItemSelectionManager.cs

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using Mirror;
public class WitchItemSelectionManager : MonoBehaviour
{
    [Header("Data")]
    public List<WitchItemData> allItems;
    public GameObject buttonPrefab;

    [Header("UI References")]
    public Transform itemButtonContainer; // 拖入 ItemButtonContainer
    public TextMeshProUGUI itemExplainText; // 拖入 ItemText

    [Header("Visual Settings")]
    public Color witchColor = new Color(0.2f, 0f, 0.3f); // 暗紫
    public Color highlightColor = Color.cyan;          // 道具选中用青色区分

    private WitchItemData currentSelection;
    private Dictionary<WitchItemData, Image> itemButtons = new Dictionary<WitchItemData, Image>();

    private void Start()
    {
        // 1. 默认选择第一个
        if (allItems.Count > 0) currentSelection = allItems[0];

        // 2. 生成按钮
        foreach (var item in allItems)
        {
            GameObject go = Instantiate(buttonPrefab, itemButtonContainer);
            go.GetComponentInChildren<TextMeshProUGUI>().text = ""; // 隐藏文字，只看图

            // 设置图片到子物体 Icon
            Transform iconTrans = go.transform.Find("Icon");
            if (iconTrans != null) iconTrans.GetComponent<Image>().sprite = item.icon;

            Image frameImg = go.GetComponent<Image>();
            frameImg.color = witchColor;

            // 绑定事件：悬浮看说明，点击选择
            SkillButtonUI hover = go.GetComponent<SkillButtonUI>() ?? go.AddComponent<SkillButtonUI>();
            // 注意：这里需要稍微修改之前的 SkillButtonUI 兼容 WitchItemData，或者直接在下面处理
            
            go.GetComponent<Button>().onClick.AddListener(() => OnItemClicked(item));
            
            itemButtons.Add(item, frameImg);
        }

        if (itemExplainText != null) itemExplainText.text = "Select a witch item.";
        UpdateVisuals();
        Save();
    }

    private void OnItemClicked(WitchItemData item)
    {
        currentSelection = item;
        ShowDescription(item);
        UpdateVisuals();
        Save();
    }

    public void ShowDescription(WitchItemData item)
    {
        if (itemExplainText != null)
        {
            itemExplainText.text = $"<color=#BB88FF><b>{item.itemName}</b></color>\n{item.description}";
        }
    }

    private void UpdateVisuals()
    {
        foreach (var kvp in itemButtons)
        {
            bool isSelected = (kvp.Key == currentSelection);
            var outline = kvp.Value.GetComponent<Outline>() ?? kvp.Value.gameObject.AddComponent<Outline>();
            outline.enabled = isSelected;
            outline.effectColor = highlightColor;
            outline.effectDistance = new Vector2(4, -4);
            kvp.Value.gameObject.transform.localScale = isSelected ? new Vector3(1.1f, 1.1f, 1f) : Vector3.one;
        }
    }

    private void Save()
    {
        if (currentSelection != null)
        {
            string className = currentSelection.scriptClassName;
            PlayerSettings.Instance.selectedWitchItemName = className;

            // 【核心修复】立即同步给服务器，不要等下次进大厅
            if (NetworkClient.localPlayer != null)
            {
                var pScript = NetworkClient.localPlayer.GetComponent<PlayerScript>();
                if (pScript != null)
                {
                    pScript.CmdUpdateSelectedItem(className);
                    Debug.Log($"[UI] 正在向服务器同步选中的道具: {className}");
                }
            }

            if (LobbyModelPreview.Instance != null)
            {
                LobbyModelPreview.Instance.RefreshItemSelection();
            }
        }
    }
}
```

