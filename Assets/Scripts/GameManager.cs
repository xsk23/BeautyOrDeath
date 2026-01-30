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
    public bool FriendlyFire => friendlyFireInternal; // 提供一个只读访问接口
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
                EndGame(); 
            }
        }
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
        MyNetworkManager netManager = NetworkManager.singleton as MyNetworkManager;
        if (netManager == null) return;
        int id = conn.connectionId;
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
        GameObject prefabToUse = (role == PlayerRole.Witch) ? netManager.witchPrefab : netManager.hunterPrefab;
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



        // 4. 生成实例
        GameObject characterInstance = Instantiate(prefabToUse, spawnPos, spawnRot);

        // 5. 初始化数据
        LobbyScript lobby = FindObjectOfType<LobbyScript>();
        GamePlayer playerScript = characterInstance.GetComponent<GamePlayer>();
        if (playerScript != null)
        {
            playerScript.playerName = pName;
            playerScript.playerRole = role;

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

    // 【修改】将分配逻辑改为预分配，存入字典
    // [Server]
    // public void PreAssignRoles()//纯随机分配
    // {
    //     pendingRoles.Clear();
    //     pendingNames.Clear(); // 【新增】清空名字字典

    //     Debug.Log($"[PreAssignRoles] Starting... Total Connections: {NetworkServer.connections.Count}");

    //     foreach (var conn in NetworkServer.connections.Values)
    //     {
    //         if (conn?.identity == null) 
    //         {
    //             Debug.LogWarning($"[PreAssignRoles] Skip connection {conn.connectionId} (No Identity)");
    //             continue;
    //         }

    //         // 1. 保存角色 (原有逻辑)
    //         PlayerRole newRole = (UnityEngine.Random.value < 0.5f) ? PlayerRole.Witch : PlayerRole.Hunter;
    //         pendingRoles[conn.connectionId] = newRole;

    //         // 2. 获取名字
    //         var playerScript = conn.identity.GetComponent<PlayerScript>();
    //         string pName = (playerScript != null) ? playerScript.playerName : "Unknown";
    //         pendingNames[conn.connectionId] = pName;

    //         Debug.Log($"[PreAssignRoles] ID: {conn.connectionId} | Name: {pName} | Role: {newRole}");
    //     }
    // }

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
    }

    // 【新增】当游戏场景真正加载完成后被调用
    [Server]
    public void OnGameSceneReady()
    {
        Debug.Log("[Server] Game Scene Ready. Initializing spawner...");
        
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
    }

    public void ResetGame()
    {
        // 重置游戏逻辑
        SetGameState(GameState.Lobby);
        gameTimer = 300f;
        Debug.Log("Game has been reset to Lobby state.");
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
            Debug.LogWarning("[Server] LobbyScript not found, using default timer 300s");
        }

        // 2. 寻找 Spawner
        if (animalSpawner == null) {
            animalSpawner = FindObjectOfType<ServerAnimalSpawner>();
        }

        // 3. 改变游戏状态
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
