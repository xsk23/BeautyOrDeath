using UnityEngine;
using UnityEngine.UI;
using TMPro; // 如果你用TextMeshPro

public class PlayerRowUI : MonoBehaviour
{
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI pingText; // 【新增】拖入显示 Ping 的 TMP 文本
    public Button actionButton;       // 对应 Prefab 里的按钮
    public TextMeshProUGUI actionButtonText;     // 对应按钮里面的文字 (用于显示 Ready / Cancel)

    [Header("Inline Edit")]
    public Button btnEdit;                      // 小修改按鈕 (✎)
    public TMP_InputField nameInputField;       // 與 nameText 重疊的輸入框
    public GameObject nameContainer;            // 可選：包住 nameText + btnEdit 的容器

    private PlayerScript boundPlayer;      // 記住這行對應哪個玩家
    private void Awake()
    {
        if (btnEdit != null)
        {
            btnEdit.onClick.AddListener(StartEditingName);
            btnEdit.gameObject.SetActive(false);   // 一開始隱藏
        }

        if (nameInputField != null)
        {
            nameInputField.gameObject.SetActive(false);
            nameInputField.onEndEdit.AddListener(OnNameInputEndEdit);
            // 可選：按 Escape 取消
            // nameInputField.onDeselect.AddListener(...);
        }
    }

    // 更新这一行的显示内容
    public void UpdateInfo(string playerName, bool isReady, bool isLocalPlayer,int ping) // 【修改】增加 ping 参数
    {
        // 名字显示
        nameText.text = playerName + (isLocalPlayer ? " (You)" : "");
        // nameText.color = isLocalPlayer ? Color.green : Color.white;

        // 状态显示
        statusText.text = isReady ? "<color=green>READY</color>" : "<color=red>WAITING</color>";   
        // 【新增】显示延迟逻辑
        if (pingText != null)
        {
            pingText.text = ping + "ms";
            // 根据延迟改变颜色
            if (ping < 80) pingText.color = Color.green;
            else if (ping < 150) pingText.color = Color.yellow;
            else pingText.color = Color.red;
        }        
        // 如果这行是本地玩家，我们需要更新按钮上的文字
        if (isLocalPlayer && actionButtonText != null)
        {
            actionButtonText.text = isReady ? "Cancel" : "Ready";
        }
        // 只對本地玩家顯示編輯按鈕
        if (btnEdit != null)
        {
            btnEdit.gameObject.SetActive(isLocalPlayer);
        }

        // 確保編輯中狀態被重置（斷線重連等情況）
        if (nameInputField != null && nameInputField.gameObject.activeSelf)
        {
            StopEditing();
        }
    }
    // 讓 LobbyScript 呼叫，綁定對應的 PlayerScript
    public void BindToPlayer(PlayerScript player)
    {
        boundPlayer = player;
    }

    private void StartEditingName()
    {
        if (boundPlayer == null || nameText == null || nameInputField == null) return;

        // 1. 把當前名字填入輸入框
        nameInputField.text = boundPlayer.playerName;

        // 2. 隱藏文字，顯示輸入框
        nameText.gameObject.SetActive(false);
        btnEdit.gameObject.SetActive(false);           // 編輯中隱藏按鈕
        nameInputField.gameObject.SetActive(true);

        // 3. 自動聚焦 + 全選
        nameInputField.ActivateInputField();
        nameInputField.Select();
    }

    private void OnNameInputEndEdit(string newName)
    {
        StopEditing();

        if (boundPlayer == null) return;

        newName = newName.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            // 可選擇不允許空名字，或保持原名
            return;
        }

        if (newName.Length > 16) newName = newName.Substring(0, 16);

        boundPlayer.CmdChangePlayerName(newName);
    }

    private void StopEditing()
    {
        if (nameText != null) nameText.gameObject.SetActive(true);
        if (btnEdit != null && boundPlayer != null && boundPlayer.isLocalPlayer)
        {
            btnEdit.gameObject.SetActive(true);
        }
        if (nameInputField != null) nameInputField.gameObject.SetActive(false);
    }

    // 可選：按 Escape 取消編輯
    private void Update()
    {
        if (nameInputField != null && nameInputField.gameObject.activeSelf)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                StopEditing();
            }
        }
    }
}