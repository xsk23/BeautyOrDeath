using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class LobbySkillManager : MonoBehaviour
{
    public static LobbySkillManager Instance;

    [Header("Main Selection Buttons")]
    public Button witchSkill1Btn;
    public Button witchSkill2Btn;
    public Button witchItemBtn;
    public Button hunterSkill1Btn;
    public Button hunterSkill2Btn;

    [Header("Popup Panels")]
    public GameObject witchSkillPanel;
    public GameObject witchItemPanel;
    public GameObject hunterSkillPanel;
    public GameObject uiBlocker; // 拖入全屏透明遮罩

    [Header("Panel Explain Texts")]
    public TextMeshProUGUI witchSkillExplainText;
    public TextMeshProUGUI witchItemExplainText;
    public TextMeshProUGUI hunterSkillExplainText;

    [Header("Choice Button Prefab")]
    public GameObject choiceButtonPrefab;

    [Header("Databases")]
    public List<SkillData> allSkills;
    public List<WitchItemData> allItems;

    [Header("Colors")]
    public Color highlightColor = Color.yellow; // 选中的黄色高亮
    public Sprite defaultEmptyIcon; // 【新增】当没选技能时显示的默认图标（可选）
    private int currentSelectingSlot = -1;

    private void Awake() => Instance = this;

    private void Start()
    {
        CloseAllPanels();
        // --- 【核心修改：设置默认道具】 ---
        InitializeDefaultSettings();
        // 绑定主按钮
        witchSkill1Btn.onClick.AddListener(() => OpenSelectionPanel(0));
        witchSkill2Btn.onClick.AddListener(() => OpenSelectionPanel(1));
        witchItemBtn.onClick.AddListener(() => OpenSelectionPanel(2));
        hunterSkill1Btn.onClick.AddListener(() => OpenSelectionPanel(3));
        hunterSkill2Btn.onClick.AddListener(() => OpenSelectionPanel(4));

        // 绑定全屏遮罩：点击框外关闭
        if (uiBlocker != null)
        {
            uiBlocker.GetComponent<Button>().onClick.AddListener(CloseAllPanels);
        }

        RefreshMainButtonUI();
    }
    private void Update()
    {
        // 如果有任何一个子面板（技能或道具）打开，按 Esc 全部关闭
        if (IsAnyPanelOpen() && Input.GetKeyDown(KeyCode.Escape))
        {
            CloseAllPanels();
        }
    }

    // 【新增方法】
    private void InitializeDefaultSettings()
    {
        if (PlayerSettings.Instance == null) return;

        // 1. 如果女巫道具为空，默认选第一个
        if (string.IsNullOrEmpty(PlayerSettings.Instance.selectedWitchItemName) && allItems.Count > 0)
        {
            PlayerSettings.Instance.selectedWitchItemName = allItems[0].scriptClassName;
            Debug.Log($"[Lobby] 为女巫自动选择了默认道具: {allItems[0].itemName}");
        }
        
        // 2. (可选) 如果你希望技能也有默认值，可以在这里类似处理
        // 但你在 PlayerSettings 里已经预设了 "WitchSkill_Mist" 等，所以通常不需要
    }
    private void OpenSelectionPanel(int slotIndex)
    {
        // 如果点的是当前已经打开的槽位，则关闭它（开关逻辑）
        if (currentSelectingSlot == slotIndex && IsAnyPanelOpen())
        {
            CloseAllPanels();
            return;
        }
        // 第一步：关闭所有已打开的面板，确保互斥
        CloseAllPanels();
        currentSelectingSlot = slotIndex;
        uiBlocker.SetActive(true); // 开启背景检测

        if (slotIndex <= 1)
        {
            witchSkillPanel.SetActive(true);
            PopulatePanel(witchSkillPanel.transform.Find("SkillButtonContainer"), PlayerRole.Witch, witchSkillExplainText);
        }
        else if (slotIndex == 2)
        {
            witchItemPanel.SetActive(true);
            PopulateItemPanel(witchItemPanel.transform.Find("SkillButtonContainer"), witchItemExplainText);
        }
        else
        {
            hunterSkillPanel.SetActive(true);
            PopulatePanel(hunterSkillPanel.transform.Find("SkillButtonContainer"), PlayerRole.Hunter, hunterSkillExplainText);
        }
    }
    private bool IsAnyPanelOpen()
    {
        return witchSkillPanel.activeSelf || witchItemPanel.activeSelf || hunterSkillPanel.activeSelf;
    }
    private void PopulatePanel(Transform container, PlayerRole role, TextMeshProUGUI targetText)
    {
        foreach (Transform child in container) Destroy(child.gameObject);
        if(targetText) targetText.text = "Select your power...";

        var settings = PlayerSettings.Instance;
        // 确定该职业目前选了什么，用于高亮和禁用
        List<string> currentlyEquipped = (role == PlayerRole.Witch) ? settings.selectedWitchSkillNames : settings.selectedHunterSkillNames;

        foreach (var skill in allSkills)
        {
            if (skill.role != role) continue;

            GameObject go = Instantiate(choiceButtonPrefab, container);
            go.transform.Find("Icon").GetComponent<Image>().sprite = skill.icon;
            
            Button btn = go.GetComponent<Button>();
            Outline outline = go.GetComponent<Outline>();

            // --- 核心逻辑：高亮与交互状态 ---
            bool isAlreadySelected = currentlyEquipped.Contains(skill.scriptClassName);
            
            if (isAlreadySelected)
            {
                btn.interactable = false; // 已选中的不能再点
                if (outline != null)
                {
                    outline.effectColor = highlightColor;
                    outline.effectDistance = new Vector2(4, 4); // 展现黄色外框
                }
            }

            btn.onClick.AddListener(() => OnChoiceSelected(skill.scriptClassName));
            
            SkillChoiceHover hover = go.AddComponent<SkillChoiceHover>();
            hover.targetText = targetText; 
            hover.description = $"<color=#FFD700><b>{skill.skillName}</b></color>\n{skill.description}";
        }
    }

    private void PopulateItemPanel(Transform container, TextMeshProUGUI targetText)
    {
        foreach (Transform child in container) Destroy(child.gameObject);
        if(targetText) targetText.text = "Select an item...";

        string equippedItem = PlayerSettings.Instance.selectedWitchItemName;

        foreach (var item in allItems)
        {
            GameObject go = Instantiate(choiceButtonPrefab, container);
            go.transform.Find("Icon").GetComponent<Image>().sprite = item.icon;
            
            Button btn = go.GetComponent<Button>();
            Outline outline = go.GetComponent<Outline>();

            if (item.scriptClassName == equippedItem)
            {
                btn.interactable = false;
                if (outline != null)
                {
                    outline.effectColor = highlightColor;
                    outline.effectDistance = new Vector2(4, 4);
                }
            }

            btn.onClick.AddListener(() => OnChoiceSelected(item.scriptClassName));

            SkillChoiceHover hover = go.AddComponent<SkillChoiceHover>();
            hover.targetText = targetText;
            hover.description = $"<color=#BB88FF><b>{item.itemName}</b></color>\n{item.description}";
        }
    }

    private void OnChoiceSelected(string className)
    {
        var settings = PlayerSettings.Instance;

        switch (currentSelectingSlot)
        {
            case 0: settings.selectedWitchSkillNames[0] = className; break;
            case 1: settings.selectedWitchSkillNames[1] = className; break;
            case 2: settings.selectedWitchItemName = className; break;
            case 3: settings.selectedHunterSkillNames[0] = className; break;
            case 4: settings.selectedHunterSkillNames[1] = className; break;
        }

        CloseAllPanels();
        RefreshMainButtonUI();
    }

    public void RefreshMainButtonUI()
    {
        if (PlayerSettings.Instance == null) return;

        var settings = PlayerSettings.Instance;
        // 更新所有主按钮的显示
        UpdateBtnVisual(witchSkill1Btn, settings.selectedWitchSkillNames[0]);
        UpdateBtnVisual(witchSkill2Btn, settings.selectedWitchSkillNames[1]);
        UpdateBtnVisual(witchItemBtn, settings.selectedWitchItemName);
        UpdateBtnVisual(hunterSkill1Btn, settings.selectedHunterSkillNames[0]);
        UpdateBtnVisual(hunterSkill2Btn, settings.selectedHunterSkillNames[1]);
    }
    private Sprite GetIconByClassName(string className)
    {
        if (string.IsNullOrEmpty(className)) return null;

        // 从技能数据库找
        var skill = allSkills.Find(s => s.scriptClassName == className);
        if (skill != null) return skill.icon;

        // 从道具数据库找
        var item = allItems.Find(i => i.scriptClassName == className);
        if (item != null) return item.icon;

        return null;
    }
    private void UpdateBtnVisual(Button btn, string className)
    {
        if (btn == null) return;

        // 尝试获取子物体中的 Icon Image 和 Text
        Transform iconTrans = btn.transform.Find("Icon");
        Image iconImage = iconTrans != null ? iconTrans.GetComponent<Image>() : null;
        TextMeshProUGUI textComp = btn.GetComponentInChildren<TextMeshProUGUI>();

        Sprite skillIcon = GetIconByClassName(className);

        if (skillIcon != null)
        {
            // 如果找到了图标：显示图标，隐藏文字
            if (iconImage != null)
            {
                iconImage.sprite = skillIcon;
                iconImage.enabled = true;
            }
            if (textComp != null) textComp.enabled = false;
        }
        else
        {
            // 如果没选或没找到图标：隐藏图标，显示文字（显示类名或 None）
            if (iconImage != null) iconImage.enabled = false;
            if (textComp != null)
            {
                textComp.enabled = true;
                textComp.text = "None"; 
            }
        }
    }
    private void UpdateBtnText(Button btn, string className)
    {
        if (btn == null) return;
        var textComp = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (textComp != null)
        {
            textComp.text = GetDisplayName(className);
        }
    }

    private string GetDisplayName(string className)
    {
        if (string.IsNullOrEmpty(className)) return "None";
        var skill = allSkills.Find(s => s.scriptClassName == className);
        if (skill != null) return skill.skillName;
        var item = allItems.Find(i => i.scriptClassName == className);
        if (item != null) return item.itemName;
        return className;
    }

    public void CloseAllPanels()
    {
        witchSkillPanel.SetActive(false);
        witchItemPanel.SetActive(false);
        hunterSkillPanel.SetActive(false);
        if (uiBlocker != null) uiBlocker.SetActive(false);
    }
}