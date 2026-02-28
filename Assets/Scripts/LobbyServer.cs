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
        Process p = SpawnGameProcess(port);

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
    Process SpawnGameProcess(int port)
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
            info.Arguments = $"-batchmode -nographics -port {port}";
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