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