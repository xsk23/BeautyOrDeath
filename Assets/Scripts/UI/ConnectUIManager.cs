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
    public GameObject wrongPwdText; // 【新增】拖入你截图中的 WrongText 物体
    public Button closePwdPanelBtn; // 【新增】拖入你刚才做的 CloseButton
    // 记录当前选中的房间信息
    private int selectedRoomId = -1;       // -1 表示未选中
    private bool selectedRoomHasPwd = false;
    // 网络是否已初始化标志
    private bool isNetworkReady = false;
    [Header("Player Limit Settings")]
    public Toggle playerLimitToggle;
    public GameObject playerLimitGroup;
    public Slider playerCountSlider;
    public TMP_Text playerCountText;
    [Header("Feedback")]
    public TMP_Text joinWarningText; // 拖入你截图中的 New Text (JoinText)
    private int selectedRoomCurrentPlayers = 0;
    private int selectedRoomMaxPlayers = 0;
    [Header("Auto Refresh Settings")]
    public bool autoRefresh = true;
    public float refreshInterval = 2f; // 每2秒更新一次
    private Coroutine autoRefreshCoroutine;

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
        // 【新增】绑定关闭密码面板按钮
        if (closePwdPanelBtn != null)
        {
            closePwdPanelBtn.onClick.AddListener(ClosePwdPanel);
        }
        // 【新增】当玩家重新输入密码时，隐藏错误提示
        if (joinPwdInput != null)
        {
            joinPwdInput.onValueChanged.AddListener((val) => {
                if (wrongPwdText) wrongPwdText.SetActive(false);
            });
        }
        // 绑定 Toggle 逻辑：勾选时才显示密码输入框
        passwordToggle.onValueChanged.AddListener((isOn) =>
        {
            passwordInput.gameObject.SetActive(isOn);
            PasswordLabel.gameObject.SetActive(isOn);
            passwordToggleLabel.text = isOn ? "ON" : "OFF";
            if (!isOn) passwordInput.text = ""; // 取消勾选清空密码
        });
        // --- 新增：限制输入长度并设置提示文字 ---
        if (roomNameInput != null)
        {
            roomNameInput.characterLimit = 10;
            // 获取占位符并设置初始提示
            var namePlaceholder = roomNameInput.placeholder.GetComponent<TMP_Text>();
            namePlaceholder.text = "Max Length:10";
        }

        if (passwordInput != null)
        {
            passwordInput.characterLimit = 10;
            var pwdPlaceholder = passwordInput.placeholder.GetComponent<TMP_Text>();
            pwdPlaceholder.text = "Max Length:10";
        }
        // 初始状态
        ControlCreatePanel(false);
        if (inputPwdPanel) inputPwdPanel.SetActive(false);
        if (joinButton) joinButton.interactable = false; // 初始禁用加入按钮
                                                         // 注册网络回调 
        RegisterNetworkHandlers();
        // 1. Toggle 切换时显示/隐藏滑动条组
        playerLimitToggle.onValueChanged.AddListener((isOn) => {
            playerLimitGroup.SetActive(isOn);
        });

        // 2. Slider 变化时更新文字显示
        playerCountSlider.onValueChanged.AddListener((val) => {
            playerCountText.text = val.ToString();
        });

        // 初始化显示
        playerLimitGroup.SetActive(false);
        playerCountText.text = playerCountSlider.value.ToString();
        if (autoRefresh) autoRefreshCoroutine = StartCoroutine(AutoRefreshRoutine());
        // 【新增双保险】
        // 如果回到这个场景时，发现还有没杀掉的房间，且此时还没连上大厅
        var mynet = NetworkManager.singleton as MyNetworkManager;
        if (mynet != null && mynet.pendingRoomIdToCancel > 0 && !NetworkClient.isConnected)
        {
            Debug.Log("[UI] Detected orphan room ID, ensuring connection to Lobby...");
            // 这里的逻辑可以根据你的需求，如果是自动回来的，可以直接触发重连
            // 或者简单地依靠 StartMenu 里的 OnButtonJoin。
        }
        CheckForPendingErrors();
    }
    private void CheckForPendingErrors()
    {
        if (joinWarningText != null && !string.IsNullOrEmpty(MyNetworkManager.PendingErrorMessage))
        {
            // 显示错误信息
            joinWarningText.text = MyNetworkManager.PendingErrorMessage;
            joinWarningText.color = Color.red;
            joinWarningText.gameObject.SetActive(true);

            // 播放一个提示音（可选）
            // AudioManager.Instance?.Play2D("Error_Sound");

            // 重要：消费掉这条消息，防止下次进入该场景又弹出
            MyNetworkManager.PendingErrorMessage = "";
            
            // 如果有 Join 按钮，让它重新可用，以便玩家选择其他房间
            if (joinButton != null) joinButton.interactable = false; 
        }
    }
    IEnumerator AutoRefreshRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(refreshInterval);

            if (NetworkClient.isConnected)
            {
                SendGetListReq(); 
                //触发按钮旋转视觉效果
                if (refreshBtn != null)
                {
                    UIButtonRotate rotateScript = refreshBtn.GetComponent<UIButtonRotate>();
                    if (rotateScript != null)
                    {
                        rotateScript.StartRotate();
                    }
                }
            }
        }
    }
    // 记得在 OnDestroy 时清理协程
    private void OnDestroy()
    {
        if (autoRefreshCoroutine != null) StopCoroutine(autoRefreshCoroutine);
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
            placeholder.text = "<color=red>It's empty!</color>";
            roomNameInput.text = ""; // 清空空格
            // 甚至可以加个小晃动效果（可选）
            return;
        }
        if (rName.Length > 10) rName = rName.Substring(0, 10); // 强制截断
        // ------------------------

        // 2. 【新增】验证密码逻辑
        string pwd = "";
        if (passwordToggle != null && passwordToggle.isOn)
        {
            pwd = passwordInput.text.Trim();
            if (string.IsNullOrWhiteSpace(pwd))
            {
                // 获取密码框的占位符文本组件
                var pwdPlaceholder = passwordInput.placeholder.GetComponent<TMP_Text>();
                pwdPlaceholder.text = "<color=red>It's empty!</color>";
                passwordInput.text = ""; // 清空空格
                
                // 播放一个音效或反馈（可选）
                // AudioManager.Instance?.Play2D("Error_Sound");
                return; // 拦截发送
            }
            if (pwd.Length > 10) pwd = pwd.Substring(0, 10); // 强制截断
        }
        // --- 新增：读取玩家数量 ---
        // 如果勾选了限制，取滑块值；否则默认 1000 人
        int maxPlayers = playerLimitToggle.isOn ? (int)playerCountSlider.value : 1000; // 1000 表示不限制
        AudioManager.Instance?.Play2D("UI点击（木头）");
        
        Debug.Log($"发送创建请求: 房间名='{rName}', 有密码={(!string.IsNullOrEmpty(pwd))}");
        NetworkClient.Send(new CreateRoomReq
        {
            roomName = rName,
            password = pwd,
            maxPlayers = maxPlayers
        });

        if (confirmCreateBtn) confirmCreateBtn.interactable = false;
    }

    // --- 网络回调：创建结果 ---
    void OnCreateRes(CreateRoomRes msg)
    {
        if (confirmCreateBtn) confirmCreateBtn.interactable = true;

        if (msg.success)
        {
            // 【核心修改】直接存入静态变量，无视对象是否存在
            MyNetworkManager.GlobalPendingRoomId = msg.roomId; 
            
            Debug.Log($"[Client] Room Created. Global ID set to: {msg.roomId}");
            ControlCreatePanel(false); 
            
            MyNetworkManager myNet = NetworkManager.singleton as MyNetworkManager;
            if (myNet != null)
            {
                myNet.ClientChangeRoom(msg.serverIp, msg.serverPort);
            }
        }
        else
        {
            Debug.LogError($"Creation failed: {msg.message}");
        }
    }
    // 提供一个公共方法供 StartMenu 调用
    public void CancelMyRoom()
    {
        var myNet = NetworkManager.singleton as MyNetworkManager;
        if (myNet != null && myNet.pendingRoomIdToCancel != -1 && NetworkClient.isConnected) 
        {
            Debug.Log($"[Client] Sending request to kill room {myNet.pendingRoomIdToCancel}");
            NetworkClient.Send(new CancelRoomReq { roomId = myNet.pendingRoomIdToCancel });
            myNet.pendingRoomIdToCancel = -1;
        }
    }
    // 修改这个方法，让它可以被 StartMenu 的取消按钮直接调用
    public void CancelMyRoomManual()
    {
        var myNet = NetworkManager.singleton as MyNetworkManager;
        if (myNet != null && myNet.pendingRoomIdToCancel != -1 && NetworkClient.isConnected)
        {
            Debug.Log($"[Client] Manual cancel sent to Lobby for room: {myNet.pendingRoomIdToCancel}");
            NetworkClient.Send(new CancelRoomReq { roomId = myNet.pendingRoomIdToCancel });
            myNet.pendingRoomIdToCancel = -1;
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

    // 1. 修改 SelectRoom 方法，增加人数参数
    public void SelectRoom(int id, bool hasPwd, int current, int max)
    {
        // 记录数据
        selectedRoomId = id;
        selectedRoomHasPwd = hasPwd;
        selectedRoomCurrentPlayers = current;
        selectedRoomMaxPlayers = max;
        // 选中新房间时，隐藏之前的警告文字
        if (joinWarningText != null) joinWarningText.gameObject.SetActive(false);

        // 激活 Join 按钮
        if (joinButton) joinButton.interactable = true;

        Debug.Log($"已选中房间: {id}, 有密码: {hasPwd}");
    }

    // --- UI 逻辑: 点击 Join 按钮 ---
    void OnClickJoin()
    {
        if (selectedRoomId == -1) return;
        // --- 新增：满人检查逻辑 ---
        // 如果不是无限人数(1000) 且 当前人数已满
        if (selectedRoomMaxPlayers < 1000 && selectedRoomCurrentPlayers >= selectedRoomMaxPlayers)
        {
            // 显示提示文字
            if (joinWarningText != null)
            {
                joinWarningText.text = "Room is Full!";
                joinWarningText.color = Color.red;
                joinWarningText.gameObject.SetActive(true);
            }

            // 按钮震动 (复用你现有的 ShakeUI 协程)
            StartCoroutine(ShakeUI(joinButton.GetComponent<RectTransform>()));
            
            // 播放错误音效
            AudioManager.Instance?.Play2D("错误音效名"); // 如果你有配置的话
            return; // 拦截，不发送加入请求
        }
        // -----------------------
        AudioManager.Instance?.Play2D("UI点击（木头）");

        if (selectedRoomHasPwd)
        {
            // 有密码 -> 弹出密码输入框
            if (inputPwdPanel) inputPwdPanel.SetActive(true);
            if (joinPwdInput) joinPwdInput.text = "";
            if (wrongPwdText) wrongPwdText.SetActive(false); // 【新增】打开面板时默认隐藏错误提示
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
        // if (inputPwdPanel) inputPwdPanel.SetActive(false);
    }

    // --- UI 逻辑: 密码弹窗确认 ---
    void OnConfirmPwd()
    {
        AudioManager.Instance?.Play2D("UI点击（木头）");
        if (joinPwdInput) SendJoinRequest(joinPwdInput.text);
    }
    // 【新增】关闭密码面板的逻辑
    void ClosePwdPanel()
    {
        AudioManager.Instance?.Play2D("UI点击（木头）"); // 播放个点击音效
        if (inputPwdPanel) inputPwdPanel.SetActive(false); // 隐藏面板
        if (wrongPwdText) wrongPwdText.SetActive(false);   // 隐藏错误提示
        if (joinPwdInput) joinPwdInput.text = "";          // 清空输入框，防止下次打开还在
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
            // 【新增】处理密码错误的 UI 反馈
            if (msg.message == "密码错误")
            {
                if (wrongPwdText) wrongPwdText.SetActive(true); // 显示 Wrong! 文字
                
                // 让输入框震动一下
                if (joinPwdInput) 
                {
                    StartCoroutine(ShakeUI(joinPwdInput.GetComponent<RectTransform>()));
                }
                
                // 播放错误音效 (如果有的话)
                // AudioManager.Instance?.Play2D("Error_Sound");
            }
        }
    }
    // 【新增】UI 左右震动的协程
    private IEnumerator ShakeUI(RectTransform target)
    {
        if (target == null) yield break;

        Vector2 originalPos = target.anchoredPosition;
        float duration = 0.3f; // 震动持续时间
        float magnitude = 15f; // 震动幅度（像素）

        float elapsed = 0.0f;

        while (elapsed < duration)
        {
            // 只需要 X 轴（左右）震动
            float offsetX = Random.Range(-1f, 1f) * magnitude;
            
            // 随着时间推移，震动幅度越来越小
            float currentMagnitude = Mathf.Lerp(offsetX, 0, elapsed / duration);
            
            target.anchoredPosition = new Vector2(originalPos.x + currentMagnitude, originalPos.y);

            elapsed += Time.deltaTime;
            yield return null;
        }

        // 震动结束，确保位置完全还原
        target.anchoredPosition = originalPos;
    }
}