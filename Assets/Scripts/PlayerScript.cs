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
    public GameObject floatingInfo;//悬浮信息
    private  Material playerMaterialClone;//玩家材质克隆体

    public GameObject[] weaponArray;//武器数组 

    [SyncVar(hook = nameof(OnWeaponChanged))]
    private int currentWeaponSynced;//当前武器索引:1和2

    private Weapon activeWeapon;//当前武器引用
    private int currentWeaponIndex;//当前武器下标
    private float cooldownTime;//冷却计时器


    private SceneScript sceneScript;//场景脚本引用


    [SyncVar(hook = nameof(OnPlayerNameChanged))]
    public string playerName = "Unknown"; // 给个默认值

    
    [SyncVar(hook = nameof(OnPlayerColorChanged))]
    private Color playerColor;//玩家颜色


    //武器同步变量
    private void OnWeaponChanged(int oldWeapon, int newWeapon)
    {
        // 只有在游戏场景且找到了 sceneScript 才能更新UI，否则只更新逻辑
        bool canUpdateUI = sceneScript != null;
        if(0<oldWeapon && oldWeapon<weaponArray.Length&&weaponArray[oldWeapon]!=null)
        {
            weaponArray[oldWeapon].SetActive(false);
        }
        if(0<newWeapon && newWeapon<weaponArray.Length&&weaponArray[newWeapon]!=null)
        {
            weaponArray[newWeapon].SetActive(true);
            activeWeapon = weaponArray[newWeapon].GetComponent<Weapon>();//获取当前武器引用
            // 【修复】这里之前漏了 canUpdateUI 检查，会导致客户端在大厅报错
            if (canUpdateUI && sceneScript.canvasBulletText != null) 
            {
                sceneScript.canvasBulletText.text = activeWeapon.bulletCount.ToString();
            }
            
        }
        else
        {
            activeWeapon = null;
            if (canUpdateUI && sceneScript.canvasBulletText != null) 
            {
                sceneScript.canvasBulletText.text = "No Weapon";
            }
        }
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


    override public void OnStartLocalPlayer()
    {
        // 1. 尝试查找游戏场景脚本
        sceneScript = FindObjectOfType<SceneScript>();
        if (sceneScript != null) sceneScript.playerScript = this;

        // 2. 如果在大厅，尝试查找大厅脚本 (OnStartClient可能没找到)
        if (!IsInGameScene && lobbyScript == null)
        {
            lobbyScript = FindObjectOfType<LobbyScript>();
        }

        // 摄像机设置 (如果在游戏场景)
        if (IsInGameScene)
        {
            Camera.main.transform.SetParent(transform);
            Camera.main.transform.localPosition = Vector3.zero;
        }

        // UI 设置
        if(floatingInfo != null)
        {
            floatingInfo.transform.localPosition = new Vector3(0, -0.3f, 0.6f);
            floatingInfo.transform.localScale = new Vector3(-0.1f, 0.1f, 0.1f);
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
        // ChangePlayerNameAndColor();
    }

    //方法1:服务器生成子弹对象并同步给所有客户端
    // [Command]//客户端给服务器发送命令
    // private void CmdShoot()//开火命令
    // {
    //     RpcWeaponFire();//调用武器开火客户端远程调用
    // }
    // [ClientRpc]//服务器给客户端发送命令
    // private void RpcWeaponFire()//武器开火客户端远程调用
    // {
    //     var bullet = Instantiate(
    //         activeWeapon.bulletPrefab,
    //         activeWeapon.firePoint.position,
    //         activeWeapon.firePoint.rotation
    //     );
    //     var rb = bullet.GetComponent<Rigidbody>();
    //     rb.velocity = activeWeapon.firePoint.forward * activeWeapon.bulletSpeed;
    //     Destroy(bullet, activeWeapon.bulletLifetime);       
    // }

    //方法2:服务器生成子弹对象并同步给所有客户端
    //给子弹prefab添加NetworkIdentity组件和NetworkRigidbody组件
    //在NetworkManager的Registered Spawnable Prefabs中添加子弹prefab
    [Command]//客户端给服务器发送命令
    private void CmdShoot()//开火命令
    {
        // UnityEngine.Debug.Log("shot1");
        // 增加判空防止报错断线
        // if (activeWeapon == null || activeWeapon.bulletPrefab == null) return;
        // UnityEngine.Debug.Log("shot2");
        var bullet = Instantiate(
            activeWeapon.bulletPrefab,
            activeWeapon.firePoint.position,
            activeWeapon.firePoint.rotation
        );
        var rb = bullet.GetComponent<Rigidbody>();
        rb.velocity = activeWeapon.firePoint.forward * activeWeapon.bulletSpeed;
        Destroy(bullet, activeWeapon.bulletLifetime);       
        NetworkServer.Spawn(bullet);//在服务器生成子弹并同步给所有客户端
    }  


    [Command]//客户端给服务器发送命令
    private void CmdSetupPlayer(string name, Color color)//设置玩家信息命令
    { 
        playerName = name;
        playerColor = color;
        // 【关键修复】这里导致了你断开连接！
        // 因为大厅里 sceneScript 是 null，直接访问会抛出异常，Mirror 就会踢掉这个客户端
        if (sceneScript != null) 
        {
            sceneScript.statusText = $"{playerName} has joined the game!";
        }
    }


    [Command]//客户端给服务器发送命令
    public void CmdSendPlayerMessage()//发送玩家消息命令
    {
        if (sceneScript != null)
        {
            sceneScript.statusText = $"{playerName} says hello! {UnityEngine.Random.Range(1,100)}";
        }
    }

    [Command]   
    public void CmdChangeWeapon(int weaponIndex)//更改武器命令
    {
        if(0<weaponIndex && weaponIndex<weaponArray.Length)
        {
            currentWeaponSynced = weaponIndex;
            // 【核心修复】服务器必须手动调用一次 Hook，否则服务器不知道当前拿的是什么枪
            // 第一个参数 oldValue 传 0 即可，不影响逻辑
            OnWeaponChanged(0, weaponIndex); 
        }
    }
    private void Update()
    {

        // 如果不是本地玩家，直接返回
        if (!isLocalPlayer) return;

        // --- 核心逻辑区分 ---
        // 如果在大厅 (Lobby)
        if (!IsInGameScene) 
        {
            // 在大厅按 C 改名
            // if (Input.GetKeyDown(KeyCode.C)) ChangePlayerNameAndColor();
            return; // 禁止移动和射击
        }
        
        // 如果在游戏 (Game)
        HandleMovement();
        HandleShooting();
    }
    // 封装移动逻辑
    void HandleMovement()
    {
        var moveX = Input.GetAxis("Horizontal") * Time.deltaTime * 110.0f;
        var moveZ = Input.GetAxis("Vertical") * Time.deltaTime * 4.0f;
        transform.Rotate(0, moveX, 0);
        transform.Translate(0, 0, moveZ);
    }

    // 封装射击逻辑
    void HandleShooting()
    {
        if (Input.GetButtonDown("Fire2"))
        {
            int newWeaponIndex = currentWeaponSynced + 1;
            if (newWeaponIndex >= weaponArray.Length) newWeaponIndex = 1;
            CmdChangeWeapon(newWeaponIndex);
        }

        if (Input.GetButtonDown("Fire1"))
        {
            // UnityEngine.Debug.Log("尝试开火");
            if(activeWeapon != null && activeWeapon.bulletCount > 0 && Time.time >= cooldownTime)
            {
                cooldownTime = Time.time + activeWeapon.cooldownTime;
                activeWeapon.bulletCount--;
                if(sceneScript) sceneScript.canvasBulletText.text = activeWeapon.bulletCount.ToString();
                // UnityEngine.Debug.Log("开火条件满足");
                CmdShoot();
            }
        }
    }
    private void Awake()
    {
        sceneScript = FindObjectOfType<SceneScript>();
        //初始化武器状态
        foreach(var weapon in weaponArray)
        {
            if(weapon!=null)                       
            {
                weapon.SetActive(false);
            }
        }
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

        // 2. 只有校验通过才切换场景
        if (total > 0 && total == ready)
        {
            UnityEngine.Debug.Log("All players are ready, starting the game...");
            NetworkManager.singleton.ServerChangeScene("MyScene");
        }
        else
        {
            //英文debug
            UnityEngine.Debug.LogWarning("Not all players are ready!");
    }
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

}
