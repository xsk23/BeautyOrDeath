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
    // 记录当前选中的房间信息
    private int selectedRoomId = -1;       // -1 表示未选中
    private bool selectedRoomHasPwd = false;
    // 网络是否已初始化标志
    private bool isNetworkReady = false;

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

        // 绑定 Toggle 逻辑：勾选时才显示密码输入框
        passwordToggle.onValueChanged.AddListener((isOn) =>
        {
            passwordInput.gameObject.SetActive(isOn);
            PasswordLabel.gameObject.SetActive(isOn);
            passwordToggleLabel.text = isOn ? "ON" : "OFF";
            if (!isOn) passwordInput.text = ""; // 取消勾选清空密码
        });

        // 初始状态
        ControlCreatePanel(false);
        if (inputPwdPanel) inputPwdPanel.SetActive(false);
        if (joinButton) joinButton.interactable = false; // 初始禁用加入按钮
                                                         // 注册网络回调 
        RegisterNetworkHandlers();
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

        string pwd = (passwordToggle && passwordToggle.isOn) ? passwordInput.text : "";
        string rName = (roomNameInput) ? roomNameInput.text : "New Room";
        Debug.Log($"发送创建请求: 房间名='{rName}', 有密码={(!string.IsNullOrEmpty(pwd))}");
        NetworkClient.Send(new CreateRoomReq
        {
            roomName = rName,
            password = pwd,
            maxPlayers = 10
        });

        // 禁用按钮防止重复点击
        if (confirmCreateBtn) confirmCreateBtn.interactable = false;
    }

    // --- 网络回调：创建结果 ---
    void OnCreateRes(CreateRoomRes msg)
    {
        if (confirmCreateBtn) confirmCreateBtn.interactable = true;

        if (msg.success)
        {
            Debug.Log("创建成功！正在刷新列表...");
            ControlCreatePanel(false); // 关闭弹窗
            MyNetworkManager netManager = NetworkManager.singleton as MyNetworkManager;
            if (netManager != null)
            {
                netManager.ClientChangeRoom(msg.serverIp, msg.serverPort);
            }
        }
        else
        {
            Debug.LogError($"创建失败: {msg.message}");
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

    // --- 供 RoomItemUI 调用：处理选中逻辑 ---
    public void SelectRoom(int id, bool hasPwd)
    {
        // 记录数据
        selectedRoomId = id;
        selectedRoomHasPwd = hasPwd;

        // 激活 Join 按钮
        if (joinButton) joinButton.interactable = true;

        Debug.Log($"已选中房间: {id}, 有密码: {hasPwd}");
    }

    // --- UI 逻辑: 点击 Join 按钮 ---
    void OnClickJoin()
    {
        if (selectedRoomId == -1) return;

        if (selectedRoomHasPwd)
        {
            // 有密码 -> 弹出密码输入框
            if (inputPwdPanel) inputPwdPanel.SetActive(true);
            if (joinPwdInput) joinPwdInput.text = "";
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
        if (inputPwdPanel) inputPwdPanel.SetActive(false);
    }

    // --- UI 逻辑: 密码弹窗确认 ---
    void OnConfirmPwd()
    {
        if (joinPwdInput) SendJoinRequest(joinPwdInput.text);
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
        }
    }

}