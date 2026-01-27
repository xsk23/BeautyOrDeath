using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;

public class GameManager : SingletonAutoMono<GameManager>
{
    public enum GameState
    {
        Lobby,
        InGame,
        Paused,
        GameOver
    }

    private GameState currentState = GameState.Lobby;

    public GameState CurrentState
    {
        get { return currentState; }
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
        
        // --- Debug Start ---
        if (pendingRoles.ContainsKey(id))
        {
            Debug.Log($"[Spawn] Found Role for ID {id}: {pendingRoles[id]}");
        }
        else
        {
            Debug.LogError($"[Spawn] NO Role found for ID {id}! Assigning Default (Hunter). Dumping keys:");
            foreach(var key in pendingRoles.Keys) Debug.LogError($"Key: {key}");
        }
        // --- Debug End ---
        // 1. 获取数据
        PlayerRole role = pendingRoles.ContainsKey(conn.connectionId) ? pendingRoles[conn.connectionId] : PlayerRole.Hunter;
        string pName = pendingNames.ContainsKey(conn.connectionId) ? pendingNames[conn.connectionId] : $"Player {conn.connectionId}";

        // 2. 获取 Prefab
        GameObject prefabToUse = (role == PlayerRole.Witch) ? netManager.witchPrefab : netManager.hunterPrefab;
        if (prefabToUse == null) return;

        // 3. 计算位置
        Transform startTrans = NetworkManager.singleton.GetStartPosition();
        Vector3 spawnPos = startTrans != null ? startTrans.position : Vector3.zero;
        Quaternion spawnRot = startTrans != null ? startTrans.rotation : Quaternion.identity;

        // 4. 生成实例
        GameObject characterInstance = Instantiate(prefabToUse, spawnPos, spawnRot);

        // 5. 初始化数据
        GamePlayer playerScript = characterInstance.GetComponent<GamePlayer>();
        if (playerScript != null)
        {
            playerScript.playerName = pName;
            playerScript.playerRole = role; // 重要：设置角色类型
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
    // // 【核心逻辑】替换玩家对象
    // [Server]
    // public void SpawnGamePlayers()
    // {
    //     // 1. 【新增】获取 MyNetworkManager 实例引用
    //     // 因为 NetworkManager.singleton 是基类类型，需要强转为你的子类 MyNetworkManager
    //     MyNetworkManager netManager = NetworkManager.singleton as MyNetworkManager;

    //     if (netManager == null)
    //     {
    //         Debug.LogError("MyNetworkManager not found! Check your NetworkManager setup.");
    //         return;
    //     }


    //     // 获取特定的生成点 (可选：你可以设置多个 SpawnPoint)
    //     // Transform startPos = NetworkManager.singleton.GetStartPosition();

    //     foreach (NetworkConnectionToClient conn in NetworkServer.connections.Values)
    //     {
    //         if (conn == null) continue;

    //         // 1. 查找该玩家预选的角色和名字
    //         // 如果找不到（比如中途加入的），默认给 Hunter
    //         PlayerRole role = pendingRoles.ContainsKey(conn.connectionId) ? pendingRoles[conn.connectionId] : PlayerRole.Hunter;
    //         string pName = pendingNames.ContainsKey(conn.connectionId) ? pendingNames[conn.connectionId] : $"Player {conn.connectionId}";

    //         // 2. 【修改】从 MyNetworkManager 获取 Prefab
    //         GameObject prefabToUse = (role == PlayerRole.Witch) ? netManager.witchPrefab : netManager.hunterPrefab;
    //         if (prefabToUse == null) 
    //         {
    //             Debug.LogError("Prefab missing in GameManager!");
    //             continue;
    //         }

    //         // 3. 计算生成位置
    //         // 简单起见，这里用随机位置防止重叠，或者使用 NetworkManager 的 StartPosition
    //         Transform startTrans = NetworkManager.singleton.GetStartPosition();
    //         Vector3 spawnPos = startTrans != null ? startTrans.position : Vector3.zero;
    //         Quaternion spawnRot = startTrans != null ? startTrans.rotation : Quaternion.identity;

    //         // 4. 生成实例
    //         GameObject characterInstance = Instantiate(prefabToUse, spawnPos, spawnRot);

    //         // 5. 初始化数据 (设置名字、血量等)
    //         // 假设你的 WitchPrefab 和 HunterPrefab 都继承自新的 GamePlayer 基类
    //         GamePlayer playerScript = characterInstance.GetComponent<GamePlayer>();
    //         if (playerScript != null)
    //         {
    //             playerScript.playerName = pName;
    //             // playerScript.role = role; // 如果基类里还有 role 字段的话
    //         }

    //         // 6. 替换
    //         // 原来的写法会导致警告
    //         // NetworkServer.ReplacePlayerForConnection(conn, characterInstance);

    //         // 【修改为】：
    //         NetworkServer.ReplacePlayerForConnection(conn, characterInstance, ReplacePlayerOptions.Destroy);
            
    //         Debug.Log($"[Server] Spawning {role} ({pName}) for ConnId: {conn.connectionId}");
    //     }
    // }

    // 【修改】将分配逻辑改为预分配，存入字典
    [Server]
    public void PreAssignRoles()
    {
        pendingRoles.Clear();
        pendingNames.Clear(); // 【新增】清空名字字典

        Debug.Log($"[PreAssignRoles] Starting... Total Connections: {NetworkServer.connections.Count}");

        foreach (var conn in NetworkServer.connections.Values)
        {
            if (conn?.identity == null) 
            {
                Debug.LogWarning($"[PreAssignRoles] Skip connection {conn.connectionId} (No Identity)");
                continue;
            }

            // 1. 保存角色 (原有逻辑)
            PlayerRole newRole = (UnityEngine.Random.value < 0.5f) ? PlayerRole.Witch : PlayerRole.Hunter;
            pendingRoles[conn.connectionId] = newRole;

            // 2. 获取名字
            var playerScript = conn.identity.GetComponent<PlayerScript>();
            string pName = (playerScript != null) ? playerScript.playerName : "Unknown";
            pendingNames[conn.connectionId] = pName;

            Debug.Log($"[PreAssignRoles] ID: {conn.connectionId} | Name: {pName} | Role: {newRole}");
        }
    }

    public void ResetGame()
    {
        // 重置游戏逻辑
        SetGameState(GameState.Lobby);
        Debug.Log("Game has been reset to Lobby state.");
    }
    public void StartGame()
    {                                                                           
        SetGameState(GameState.InGame);
        Debug.Log("Game has started.");
        
        if (NetworkServer.active)
        {
            PreAssignRoles(); // 先把角色定下来，存到 GameManager 里
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
