using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

public class MyNetworkManager : NetworkManager
{
    // 当服务器检测到客户端断开连接时调用
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        // 1. 先执行基类逻辑（这会销毁玩家物体，从列表中移除连接）
        base.OnServerDisconnect(conn);

        // 2. 获取当前场景名字
        string currentScene = SceneManager.GetActiveScene().name;

        // 3. 只有在“游戏场景”中才执行这个检查
        // 防止在大厅里有人退出导致服务器重载大厅
        if (currentScene == "MyScene") 
        {
            // 4. 检查当前连接的玩家数量
            // numPlayers is a built-in counter in NetworkManager
            Debug.Log($"A player left. Remaining players: {numPlayers}");

            if (numPlayers == 0)
            {
                Debug.Log("All players have left, server returning to lobby...");
                
                // 5. 切换回大厅场景
                // onlineScene 是你在 Inspector 里设置的 Online Scene
                // 或者你可以直接写字符串 "LobbyScene"
                ServerChangeScene(onlineScene); 
            }
        }
    }
}