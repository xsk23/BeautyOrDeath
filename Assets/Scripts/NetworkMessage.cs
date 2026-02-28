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