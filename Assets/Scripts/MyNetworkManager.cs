using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
using System.Collections;
using kcp2k;
// 头部增加
using System.Net.Sockets;
using System.Text;

public class MyNetworkManager : NetworkManager
{
    // 定义静态变量，静态变量在场景切换时绝对不会丢失
    // 新增一个静态变量临时存储解析出来的房间名
    // 1. 定义一个静态变量，这是真正“不死”的数据源
    public static int GlobalPendingRoomId = -1;
    // 1. 在类顶部附近增加一个标记
    public static bool PendingKillOnConnect = false; 
    public static string InitialRoomName = "New Room";
    private static string _targetAddr;
    private static ushort _targetPort;
    private static bool _shouldReconnect = false;
    public static bool IsTransitioningToRoom = false; 
    // 【新增】专门用于标记“主动断开大厅”的瞬间
    public static bool isDisconnectingFromLobby = false;
    // 【新增】保存大厅的 IP 和端口，以便失败时准确重连大厅
    public static string lobbyAddress = "localhost";
    private static ushort lobbyPort = 7770;
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
    private UdpClient statusSender; // 【新增】用于向大厅发消息
    // 增加一个静态引用来持有协程，以便取消它
    private static Coroutine _activeFinalConnectRoutine;
    [Header("Debug Room Controls")]
    [Tooltip("在加载界面点击取消时，这里会记录待销毁的房间ID")]
    public int pendingRoomIdToCancel = -1; // 【去掉 static】现在 Inspector 可见了
    // 用于记录返回大厅时的错误提示
    public static string PendingErrorMessage = "";
    // ---------------------------------------------------------
    // 服务器启动时生成 GameManager
    // ---------------------------------------------------------
    private void Update()
    {
        // 2. 每帧同步，让你在 Inspector 实时看到值
        pendingRoomIdToCancel = GlobalPendingRoomId;
    }
    public override void Awake()
    {
        base.Awake();
        statusSender = new UdpClient(); // 【新增】初始化发送器
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
            // --- 新增：解析最大人数参数 ---
            if (args[i] == "-maxPlayers" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int max))
                {
                    this.maxConnections = max; // 设置 NetworkManager 的最大连接数
                    Debug.Log($"[ServerStartup] Max Connections set to: {max}");
                }
            }
        }
    }
    // 【新增】核心汇报逻辑：向本地 7770 端口丢一个包含当前人数的数据包
    private void ReportStatusToLobby()
    {
        if (Application.isBatchMode && IsRoomSubProcess())
        {
            if (Transport.active is kcp2k.KcpTransport kcp)
            {
                // 发送格式："自己的端口:当前连接的玩家数"
                string msg = $"{kcp.Port}:{numPlayers}";
                byte[] data = Encoding.UTF8.GetBytes(msg);
                
                // 使用 UDP 直接扔给大厅，不需要建立长连接
                statusSender.SendAsync(data, data.Length, "127.0.0.1", 7770);
            }
        }
    }
    public void ClientChangeRoom(string ip, ushort port)
    {
        Debug.Log($"[Client] 准备跳转至房间: {ip}:{port}");
        // 【核心修复】动态记录当前连着的大厅信息，防止回不去
        lobbyAddress = this.networkAddress;
        // 1. 开启跳转标志位
        IsTransitioningToRoom = true;
        isDisconnectingFromLobby = true; // 【新增】告诉系统：接下来的第一次断线是我们自己要求的
        PendingErrorMessage = ""; // 清空之前的错误
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
        // --- 核心修复逻辑 ---
        // 如果设置了“待杀标记” 且 我们确实有待销毁的房间ID
        if (PendingKillOnConnect && GlobalPendingRoomId > 0)
        {
            // 只有在菜单/大厅界面连上服务器，才认为连的是大厅主服务器
            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == "StartMenu" || currentScene == "ConnectRoom")
            {
                Debug.Log($"[Client] 重连大厅成功，正在请求强制销毁残留房间: {GlobalPendingRoomId}");
                NetworkClient.Send(new CancelRoomReq { roomId = GlobalPendingRoomId });
                
                // 发完重置状态
                GlobalPendingRoomId = -1;
                PendingKillOnConnect = false; 
            }
        }
    }
    public override void OnClientError(TransportError error, string reason)
    {
        base.OnClientError(error, reason);
        Debug.LogWarning($"[Client] 网络连接错误: {error} - 原因: {reason}");

        if (IsTransitioningToRoom)
        {
            // 如果是在跳转中报错，通常是目标端口不可达（房间已关）
            PendingErrorMessage = "Room is no longer available.";
            HandleConnectionFailure();
        }
    }
    public static void SendCancelPendingRoom()
    {
        if (GlobalPendingRoomId > 0 && NetworkClient.isConnected)
        {
            Debug.Log($"[Client] 主动销毁待定房间: {GlobalPendingRoomId}");
            NetworkClient.Send(new CancelRoomReq { roomId = GlobalPendingRoomId });
            GlobalPendingRoomId = -1;
        }
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
                _activeFinalConnectRoutine = UnityMainThreadDispatcher.Instance().StartCoroutine(FinalConnectRoutine());
            });
        }
    }
    // 【新增】提供给 UI 调用的中断方法
    public static void AbortTransition()
    {
        IsTransitioningToRoom = false;
        _shouldReconnect = false;

        if (_activeFinalConnectRoutine != null)
        {
            // 这里的关键是：_activeFinalConnectRoutine 是在 Dispatcher 上运行的
            // 我们需要通过 Dispatcher 停止它，或者简单地让协程内部判断标志位
            _activeFinalConnectRoutine = null; 
            Debug.Log("[Client] Room Join Routine Aborted.");
        }
        
        // 确保 Mirror 彻底停止当前任何残留的尝试
        if (singleton != null)
        {
            singleton.StopClient();
        }
    }
    private IEnumerator FinalConnectRoutine()
    {
        Debug.Log($"[Client] FinalConnectRoutine started on Dispatcher. Target: {_targetAddr}:{_targetPort}");
        float timer = 0;
        while (timer < 5.0f)
        {
            // 【新增】每帧检查：如果玩家中途取消了（AbortTransition 把这个设为 false 了）
            // 则立即跳出协程，不执行最后的 StartClient()
            if (!IsTransitioningToRoom) 
            {
                Debug.Log("[Client] FinalConnectRoutine Interrupted! No StartClient will be called.");
                yield break; 
            }

            timer += Time.deltaTime;
            yield return null;
        }

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
        _activeFinalConnectRoutine = null;
        // 3. 【新增：监控循环】
        float connectTimeout = 3f; // 如果 3 秒内还没连上，说明房间可能刚好关了
        float startTime = Time.time;

        while (IsTransitioningToRoom && !NetworkClient.isConnected)
        {
            // 检查是否超时
            if (Time.time - startTime > connectTimeout)
            {
                Debug.LogWarning("[Client] Connection Timeout: Room process might be closed.");
                PendingErrorMessage = "Room is no longer available.";
                
                // 强制停止 Mirror 的连接尝试
                nm.StopClient(); 
                
                // 触发回退 UI 的逻辑
                HandleConnectionFailure();
                yield break;
            }

            // 检查 NetworkClient 是否已经因为底层错误报错（例如端口拒绝）
            // 如果底层已经报错，NetworkClient.active 会变 false
            if (!NetworkClient.active && !NetworkClient.isConnected)
            {
                Debug.LogWarning("[Client] NetworkClient became inactive. Room gone.");
                PendingErrorMessage = "Failed to connect: Room closed.";
                HandleConnectionFailure();
                yield break;
            }

            yield return null;
        }
    }
    // 辅助方法：统一处理失败回退
    private void HandleConnectionFailure()
    {
        Debug.Log("[Client] 检测到连接失败，启动安全回退程序...");
        
        IsTransitioningToRoom = false;
        _shouldReconnect = false;
        isDisconnectingFromLobby = false;

        // 停止跳转协程，防止它在后台继续跑
        if (_activeFinalConnectRoutine != null)
        {
            UnityMainThreadDispatcher.Instance().StopCoroutine(_activeFinalConnectRoutine);
            _activeFinalConnectRoutine = null;
        }

        // 立即停止客户端
        if (singleton != null)
        {
            singleton.StopClient();
        }

        // 关键：将重连逻辑交给 Dispatcher，它是一个不随 NetworkManager 销毁而消失的物体
        UnityMainThreadDispatcher.Instance().Enqueue(() =>
        {
            // 开启一个新的重连流程
            UnityMainThreadDispatcher.Instance().StartCoroutine(GlobalSafeReconnectRoutine());
        });
    }
    // 使用静态/全局安全的协程，不依赖于 MyNetworkManager 的实例
    private IEnumerator GlobalSafeReconnectRoutine()
    {
        // 1. 等待底层彻底释放（解决 "Already a player" 错误）
        while (NetworkClient.active || NetworkClient.isConnected)
        {
            yield return null;
        }

        // 2. 强行回归 ConnectRoom 场景
        // 如果房主关了房，Mirror 可能已经切了一半场景，必须强行拉回 UI 场景
        if (SceneManager.GetActiveScene().name != "ConnectRoom")
        {
            AsyncOperation op = SceneManager.LoadSceneAsync("ConnectRoom");
            while (!op.isDone) yield return null;
        }

        // 3. 此时场景已重载，旧的 UI 引用已消失，我们需要重新寻找新场景的组件
        yield return new WaitForSeconds(0.2f); // 给 UI 一点初始化时间

        // 获取当前活跃的单例引用（不要用 this）
        MyNetworkManager currentNM = NetworkManager.singleton as MyNetworkManager;
        if (currentNM == null) yield break;

        // 4. 恢复大厅配置
        currentNM.networkAddress = lobbyAddress;
        if (Transport.active is KcpTransport kcp)
        {
            kcp.Port = lobbyPort;
        }

        // 5. 寻找 StartMenu 并触发重连
        StartMenu startMenu = FindObjectOfType<StartMenu>();
        if (startMenu != null)
        {
            if (startMenu.loadingPanel != null) startMenu.loadingPanel.SetActive(false);
            Debug.Log("[Client] UI 场景已恢复，正在自动重连大厅...");
            startMenu.OnButtonJoin();
        }
        else
        {
            currentNM.StartClient();
        }

        // 6. 显示错误提示
        yield return new WaitForSeconds(0.2f);
        ConnectUIManager connectUI = FindObjectOfType<ConnectUIManager>();
        if (connectUI != null && connectUI.joinWarningText != null)
        {
            connectUI.joinWarningText.text = string.IsNullOrEmpty(PendingErrorMessage) ? "Room was closed." : PendingErrorMessage;
            connectUI.joinWarningText.color = Color.red;
            connectUI.joinWarningText.gameObject.SetActive(true);
            if (connectUI.joinButton != null) connectUI.joinButton.interactable = false;
            PendingErrorMessage = ""; // 消费掉错误
        }
    }
    // 处理无缝重连大厅与 UI 重置的协程
    private IEnumerator AutoReconnectLobbyRoutine()
    {
        // 1. 等待 Mirror 底层彻底释放网络资源 (非常重要，否则会报 Client Active 错)
        while (NetworkClient.active || NetworkClient.isConnected)
        {
            yield return null;
        }

        // 2. 确保配置恢复为大厅的 IP 和端口
        this.networkAddress = lobbyAddress;
        if (Transport.active is kcp2k.KcpTransport kcp)
        {
            kcp.Port = lobbyPort;
        }
        // 如果此时场景不是 ConnectRoom（可能已经切到了半截 LobbyRoom），强行拉回来
        if (SceneManager.GetActiveScene().name != "ConnectRoom")
        {
            SceneManager.LoadScene("ConnectRoom");
            // 等待一帧让新场景脚本 Awake
            yield return null; 
        }
        // 3. 寻找 StartMenu，关闭加载遮罩并调用其重连逻辑
        StartMenu startMenu = FindObjectOfType<StartMenu>();
        if (startMenu != null)
        {
            // 强制隐藏由于跳房失败残留的加载界面面板
            if (startMenu.loadingPanel != null) startMenu.loadingPanel.SetActive(false);
            
            Debug.Log("[Client] 网络已释放，正在通过 StartMenu.OnButtonJoin() 重新连接大厅...");
            startMenu.OnButtonJoin(); // 【满足要求】：直接触发重新加入逻辑
        }
        else
        {
            Debug.LogWarning("[Client] 找不到 StartMenu，改为直接调用 StartClient()");
            StartClient();
        }

        // 4. 等待一帧让 UI 更新，然后直接把报错文字弹出来
        yield return null;
        ConnectUIManager connectUI = FindObjectOfType<ConnectUIManager>();
        if (connectUI != null && connectUI.joinWarningText != null && !string.IsNullOrEmpty(PendingErrorMessage))
        {
            connectUI.joinWarningText.text = PendingErrorMessage;
            connectUI.joinWarningText.color = Color.red;
            connectUI.joinWarningText.gameObject.SetActive(true);
            
            // 禁用加入按钮，强制玩家必须重新点击某个房间
            if (connectUI.joinButton != null) connectUI.joinButton.interactable = false;

            // 消耗掉错误记录，防止下次重复弹出
            PendingErrorMessage = "";
        }
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
            // ServerChangeScene("LobbyRoom");
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
        // 如果玩家成功加载并进入了游戏房间的 LobbyRoom 场景
        if (SceneManager.GetActiveScene().name == "LobbyRoom")
        {
            // 说明房间已经正常使用了，不需要杀掉，重置 ID
            GlobalPendingRoomId = -1;
            PendingKillOnConnect = false;
            Debug.Log("[Client] 成功进入游戏房间，取消待销毁记录。");
        }
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
        // 跳转中由于玩家退出导致的断开
        if (IsTransitioningToRoom && !isDisconnectingFromLobby)
        {
            Debug.LogWarning("[Client] 在进入房间的过程中房主关闭了房间。");
            PendingErrorMessage = "Room closed by host.";
            HandleConnectionFailure();
            return;
        }
        // 1. 如果是我们【主动】要求断开大厅，消耗掉标记并放行
        if (isDisconnectingFromLobby)
        {
            isDisconnectingFromLobby = false; 
            base.OnClientDisconnect();
            Debug.Log("[Client] 已从大厅主动断开，准备连接目标房间...");
            return; 
        }
        // 3. 处理游戏中异常掉线（被踢、服务器崩溃、网络超时等）
        if (string.IsNullOrEmpty(PendingErrorMessage))
        {
            PendingErrorMessage = "Lost connection to server.";
        }
        base.OnClientDisconnect();

        // 游戏中异常掉线
        if (SceneManager.GetActiveScene().name != "StartMenu")
        {
            SceneManager.LoadScene("StartMenu");
        }
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
        // 【新增】每次加人后，告诉大厅
        ReportStatusToLobby();
    }


    // ---------------------------------------------------------
    // 玩家断线回调 (保持你原有的逻辑)
    // ---------------------------------------------------------
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // 1. 先执行基类逻辑（这会销毁玩家物体，从列表中移除连接）
        base.OnServerDisconnect(conn);
        // 【新增】每次减人后，告诉大厅
        ReportStatusToLobby();
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