using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Mirror;

/**
 * 【开发者指南】增加一项新游戏设置（Setting）的完整步骤：
 * 
 * 1. LobbyScript.cs (定义真值): 
 *    - 增加 [SyncVar(hook = nameof(OnSettingChanged))] 变量 (例如: syncedWitchJumpForce)。
 *    - 确保它使用通用的 OnSettingChanged 钩子，以便数值改变时通知 UI 刷新。
 * 
 * 2. PlayerScript.cs (建立通信隧道):
 *    - 在 CmdUpdateLobbySettings 的 switch 语句中增加一个新 case (例如: case 8)。
 *    - 将传入的 floatVal/boolVal/intVal 赋给 LobbyScript 中的对应变量。
 * 
 * 3. LobbySettingsManager.cs -> BuildSettingsUI() (生成 UI):
 *    - 在对应类别下调用 CreateSlider/CreateToggle/CreateDropdown。
 *    - key 必须唯一，回调 lambda 中调用 localPlayer?.CmdUpdateLobbySettings，传入刚才定义的 case 编号。
 * 
 * 4. LobbySettingsManager.cs -> UpdateVisuals() (同步视觉效果):
 *    - 调用辅助方法 UpdateSliderVisual("你的Key", lobby.synced变量)。
 *    - 若是 Toggle，则手动编写 TryGetValue 逻辑并调用 SetIsOnWithoutNotify。
 * 
 * 5. GameManager.cs -> 数据固化 (跨场景保护):
 *    - 增加一个 private 内部变量 (例如: witchJumpForceInternal)。
 *    - 在 StartGame() 方法中，从 LobbyScript 抓取该值存入内部变量。
 *    - 在 StartGame() 的 else 分支中，为该变量设置一个默认值。
 * 
 * 6. GameManager.cs -> 逻辑应用 (实际生效):
 *    - 在 SpawnPlayerForConnection() 中，根据 role 判断，将固化的内部变量赋给刚生成的 playerScript。
 */

public class LobbySettingsManager : MonoBehaviour
{
    public static LobbySettingsManager Instance;

    [Header("UI Toggle")]
    public GameObject settingPanel;
    public Button settingBtn;
    public TextMeshProUGUI settingBtnText;

    [Header("Prefabs")]
    public GameObject sliderPrefab;   
    public GameObject togglePrefab;   
    public GameObject dropdownPrefab; 
    public GameObject headerPrefab;
    public Transform container;       

    // 用于记录已经生成的 UI 元素，避免 Destroy
    private Dictionary<string, GameObject> spawnedSettings = new Dictionary<string, GameObject>();

    private void Awake()
    {
        Instance = this;
        settingPanel.SetActive(false);
        settingBtn.onClick.AddListener(TogglePanel);
    }

    private void Start()
    {
        BuildSettingsUI();
    }

    public void TogglePanel()
    {
        bool isActive = !settingPanel.activeSelf;
        settingPanel.SetActive(isActive);
        settingBtnText.text = isActive ? "Close" : "Setting";
    }

    private void BuildSettingsUI()
    {
        foreach (Transform child in container) Destroy(child.gameObject);
        spawnedSettings.Clear();
        LobbyScript lobby = FindObjectOfType<LobbyScript>();
        if (lobby == null) return;
        PlayerScript localPlayer = NetworkClient.localPlayer?.GetComponent<PlayerScript>();
        //将CmdUpdateLobbySettings的type顺序编号与UI生成顺序对应起来，方便维护
        // --- 类别：核心规则 ---
        CreateHeader("--- BASIC RULES ---");
        // 游戏时间：整数 (true)
        CreateSlider("GameTime", "Game Time (sec)", 60, 600, lobby.syncedGameTimer, true, (v) => localPlayer?.CmdUpdateLobbySettings(0, v, false, 0));
        // 动物数量：整数 (true)
        CreateSlider("Animals", "Animal Count", 0, 50, lobby.syncedAnimalsNumber, true, (v) => localPlayer?.CmdUpdateLobbySettings(1, v, false, 0));
        CreateToggle("FriendlyFire", "Friendly Fire", lobby.syncedFriendlyFire, (v) => localPlayer?.CmdUpdateLobbySettings(2, 0, v, 0));

        // --- 类别：阵营平衡 ---
        CreateHeader("--- BALANCE ---");
        // 血量：整数 (true)
        CreateSlider("WitchHP", "Witch Max HP", 50, 200, lobby.syncedWitchHP, true, (v) => localPlayer?.CmdUpdateLobbySettings(3, v, false, 0));
        CreateSlider("WitchMana", "Witch Max Mana", 50, 200, lobby.syncedWitchMana, true, (v) => localPlayer?.CmdUpdateLobbySettings(4, v, false, 0));
        // 速度：小数 (false)
        CreateSlider("HunterSpeed", "Hunter Speed", 4, 12, lobby.syncedHunterSpeed, false, (v) => localPlayer?.CmdUpdateLobbySettings(5, v, false, 0));
        // 挣脱：整数 (true)
        CreateSlider("TrapDiff", "Trap Escape Clicks", 1, 10, lobby.syncedTrapDifficulty, true, (v) => localPlayer?.CmdUpdateLobbySettings(6, v, false, 0));
        // 恢复率：小数 (false)
        CreateSlider("ManaRate", "Mana Regen Rate", 1, 20, lobby.syncedManaRegen, false, (v) => localPlayer?.CmdUpdateLobbySettings(7, v, false, 0));
        // 猎人比例：小数 (false) 【这是你刚才报错的地方】
        CreateSlider("HunterRatio", "Hunter Ratio (%)", 0.1f, 0.9f, lobby.syncedHunterRatio, false, (v) => localPlayer?.CmdUpdateLobbySettings(8, v, false, 0));    
        CreateSlider("AncientRatio", "Ancient Tree Ratio (x)", 1.0f, 3.0f, lobby.syncedAncientRatio, false, (v) => localPlayer?.CmdUpdateLobbySettings(9, v, false, 0));
    }
    private void CreateHeader(string title)
    {
        // 检查 Prefab 是否分配
        if (headerPrefab == null)
        {
            Debug.LogError("LobbySettingsManager: headerPrefab 尚未在 Inspector 中分配！");
            return;
        }

        GameObject go = Instantiate(headerPrefab, container);
        
        // 使用 GetComponentInChildren 兼容子物体带有文字的情况
        TextMeshProUGUI textComp = go.GetComponentInChildren<TextMeshProUGUI>();

        if (textComp != null)
        {
            textComp.text = title;
        }
        else
        {
            Debug.LogError($"LobbySettingsManager: 在生成的 {go.name} 及其子物体中找不到 TextMeshProUGUI 组件！");
        }
    }

    // 增加 isWhole 参数
    private void CreateSlider(string key, string label, float min, float max, float current, bool isWhole, System.Action<float> onCmd)
    {
        GameObject go = Instantiate(sliderPrefab, container);
        go.name = key;
        go.transform.Find("Text (TMP)").GetComponent<TextMeshProUGUI>().text = label;
        
        Slider s = go.GetComponentInChildren<Slider>();
        TextMeshProUGUI valText = go.transform.Find("SliderGroup/SliderValue").GetComponent<TextMeshProUGUI>();

        s.minValue = min;
        s.maxValue = max;
        
        // 【关键修改】：不再写死 true，而是使用传进来的变量
        s.wholeNumbers = isWhole; 
        
        s.value = current;

        // 【视觉优化】：如果是小数，显示两位精度；如果是整数，显示为 0 精度
        valText.text = isWhole ? current.ToString("F0") : current.ToString("F2");

        s.onValueChanged.AddListener((v) => {
            valText.text = isWhole ? v.ToString("F0") : v.ToString("F2");
            onCmd?.Invoke(v);
        });

        spawnedSettings.Add(key, go);
    }

    // Toggle 和 Dropdown 的 Create 方法保持类似，给 go.name 赋值即可
    private void CreateToggle(string key, string label, bool current, System.Action<bool> onCmd)
    {
        GameObject go = Instantiate(togglePrefab, container);
        go.name = key;
        
        // 设置左侧标题文字
        go.transform.Find("Text (TMP)").GetComponent<TextMeshProUGUI>().text = label;
        
        Toggle t = go.GetComponentInChildren<Toggle>();
        // 根据你的截图层级：ToggleGroup -> Toggle -> ToggleText
        TextMeshProUGUI statusText = go.transform.Find("ToggleGroup/Toggle/ToggleText").GetComponent<TextMeshProUGUI>();

        // 初始化状态
        t.isOn = current;
        statusText.text = current ? "On" : "Off"; // 或者 "Enabled" : "Disabled"

        t.onValueChanged.AddListener((v) => {
            // 本地即时切换文字
            statusText.text = v ? "On" : "Off";
            onCmd?.Invoke(v);
        });

        spawnedSettings.Add(key, go);
    }

    private void CreateDropdown(string key, string label, List<string> options, int current, System.Action<int> onCmd)
    {
        GameObject go = Instantiate(dropdownPrefab, container);
        go.name = key;
        go.transform.Find("Text (TMP)").GetComponent<TextMeshProUGUI>().text = label;
        TMP_Dropdown d = go.GetComponentInChildren<TMP_Dropdown>();
        d.ClearOptions();
        d.AddOptions(options);
        d.value = current;
        d.onValueChanged.AddListener((v) => onCmd?.Invoke(v));
        spawnedSettings.Add(key, go);
    }

    // 【关键修改】供 Hook 调用：只更新值，不重建 UI
    public void UpdateVisuals()
    {
        LobbyScript lobby = FindObjectOfType<LobbyScript>();
        if (lobby == null) return;

        // 更新 Slider: GameTime
        if (spawnedSettings.TryGetValue("GameTime", out GameObject sliderGo))
        {
            Slider s = sliderGo.GetComponentInChildren<Slider>();
            // 重点：如果用户正在拖拽这个 Slider，不要用服务器数据覆盖它，否则会“弹回”
            if (Input.GetMouseButton(0) == false) 
            {
                s.SetValueWithoutNotify(lobby.syncedGameTimer);
                sliderGo.transform.Find("SliderGroup/SliderValue").GetComponent<TextMeshProUGUI>().text = lobby.syncedGameTimer.ToString();
            }
        }

        // 更新 Toggle: FriendlyFire
        if (spawnedSettings.TryGetValue("FriendlyFire", out GameObject toggleGo))
        {
            Toggle t = toggleGo.GetComponentInChildren<Toggle>();
            t.SetIsOnWithoutNotify(lobby.syncedFriendlyFire);
        }
        // 更新 Slider: Animals
        if (spawnedSettings.TryGetValue("Animals", out GameObject animalSliderGo))
        {
            Slider s = animalSliderGo.GetComponentInChildren<Slider>();
            // 重点：如果用户正在拖拽这个 Slider，不要用服务器数据覆盖它，否则会“弹回”
            if (Input.GetMouseButton(0) == false) 
            {
                s.SetValueWithoutNotify(lobby.syncedAnimalsNumber);
                animalSliderGo.transform.Find("SliderGroup/SliderValue").GetComponent<TextMeshProUGUI>().text = lobby.syncedAnimalsNumber.ToString();
            }
        }
        // 更新其他设置
        // 参考以下模板补全所有新参数：
        UpdateSliderVisual("WitchHP", lobby.syncedWitchHP);
        UpdateSliderVisual("WitchMana", lobby.syncedWitchMana);
        UpdateSliderVisual("HunterSpeed", lobby.syncedHunterSpeed);
        UpdateSliderVisual("TrapDiff", lobby.syncedTrapDifficulty);
        UpdateSliderVisual("ManaRate", lobby.syncedManaRegen);
        UpdateSliderVisual("HunterRatio", lobby.syncedHunterRatio);
        UpdateSliderVisual("AncientRatio", lobby.syncedAncientRatio);
    }
    // 辅助方法减少重复代码
    private void UpdateSliderVisual(string key, float value)
    {
        if (spawnedSettings.TryGetValue(key, out GameObject go))
        {
            Slider s = go.GetComponentInChildren<Slider>();
            if (Input.GetMouseButton(0) == false) 
            {
                s.SetValueWithoutNotify(value);
                // 根据滑块自身的 wholeNumbers 属性决定显示格式
                string format = s.wholeNumbers ? "F0" : "F2";
                go.transform.Find("SliderGroup/SliderValue").GetComponent<TextMeshProUGUI>().text = value.ToString(format);
            }
        }
    }
}