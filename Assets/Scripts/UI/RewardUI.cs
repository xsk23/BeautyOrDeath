using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;

public class RewardUI : MonoBehaviour
{
    public static RewardUI Instance;
    public GameObject panel;
    public Button[] optionButtons;
    public TextMeshProUGUI[] optionTexts;

    private RewardOption[] currentOptions;

    void Awake() { Instance = this; panel.SetActive(false); }

    public void Show(RewardOption[] options)
    {
        currentOptions = options;
        panel.SetActive(true);
        
        // 解锁鼠标
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        for (int i = 0; i < 3; i++)
        {
            int index = i;
            optionTexts[i].text = $"<b>{options[i].title}</b>\n{options[i].description}";
            optionButtons[i].onClick.RemoveAllListeners();
            optionButtons[i].onClick.AddListener(() => OnClickOption(index));
        }
    }

    void OnClickOption(int index)
    {
        // 告知服务器我们的选择
        var localWitch = NetworkClient.localPlayer.GetComponent<WitchPlayer>();
        // 发送点击的索引 (0, 1, 或 2)
        localWitch.CmdSelectReward(index); 

        panel.SetActive(false);
        
        // 恢复鼠标锁定
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}