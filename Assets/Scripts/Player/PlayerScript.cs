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

    // public GameObject[] weaponArray;//武器数组 

    // [SyncVar(hook = nameof(OnWeaponChanged))]
    // private int currentWeaponSynced;//当前武器索引:1和2

    // private Weapon activeWeapon;//当前武器引用
    // private int currentWeaponIndex;//当前武器下标
    // private float cooldownTime;//冷却计时器


    // private SceneScript sceneScript;//场景脚本引用


    [SyncVar(hook = nameof(OnPlayerNameChanged))]
    public string playerName = "Unknown"; // 给个默认值

    
    [SyncVar(hook = nameof(OnPlayerColorChanged))]
    private Color playerColor;//玩家颜色

    [SyncVar(hook = nameof(OnRoleChanged))]
    public PlayerRole role; // 角色类型

    // private PlayerBase playerBase; // 引用角色组件

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
        // 1. 尝试查找游戏场景脚本
        // sceneScript = FindObjectOfType<SceneScript>();
        // if (sceneScript != null) sceneScript.playerScript = this;

        // 2. 如果在大厅，尝试查找大厅脚本 (OnStartClient可能没找到)
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
}
