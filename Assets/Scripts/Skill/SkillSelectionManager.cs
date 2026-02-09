using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class SkillSelectionManager : MonoBehaviour
{
    public List<SkillData> allSkills;
    public Transform contentFolder; // SkillButtonContainer
    public GameObject buttonPrefab;
    [Header("Description UI")]
    public TextMeshProUGUI skillExplainText; // 【新增】拖入你的 SkillText 物体
    public Color hunterColor = new Color(0.3f, 0f, 0f); // 暗红
    public Color witchColor = new Color(0.2f, 0f, 0.3f);  // 暗紫

    private List<SkillData> currentWitchSelection = new List<SkillData>();
    private List<SkillData> currentHunterSelection = new List<SkillData>();
    private Dictionary<SkillData, Image> skillButtons = new Dictionary<SkillData, Image>();

    private void Start()
    {
        // 默认选择初始化
        currentWitchSelection = allSkills.Where(s => s.role == PlayerRole.Witch).Take(2).ToList();
        currentHunterSelection = allSkills.Where(s => s.role == PlayerRole.Hunter).Take(2).ToList();

        foreach (var skill in allSkills)
        {
            GameObject go = Instantiate(buttonPrefab, contentFolder);
            go.GetComponentInChildren<TextMeshProUGUI>().text = skill.skillName;
            
            Image btnImg = go.GetComponent<Image>();
            btnImg.color = (skill.role == PlayerRole.Hunter) ? hunterColor : witchColor;
            
            // --- 【关键修改】 ---
            // 1. 获取新脚本并初始化
            SkillButtonUI hoverScript = go.GetComponent<SkillButtonUI>() ?? go.AddComponent<SkillButtonUI>();
            hoverScript.Setup(skill, this);

            // 2. 保持原有的点击逻辑
            go.GetComponent<Button>().onClick.AddListener(() => OnSkillClicked(skill));
            
            skillButtons.Add(skill, btnImg);
        }
        
        if (skillExplainText != null) skillExplainText.text = "Hover over a skill to see details.";
        
        UpdateVisuals();
        Save();
    }
    // 统一显示逻辑：无论是 Hover 还是 Click 都会调用这里
    public void ShowDescription(SkillData skill)
    {
        if (skillExplainText != null)
        {
            string colorHex = (skill.role == PlayerRole.Hunter) ? "#FF4444" : "#BB88FF";
            // 统一格式化：[技能名] - [角色描述] 换行 [详细介绍]
            skillExplainText.text = $"<color={colorHex}><b>{skill.skillName}</b></color> ({skill.role})\n{skill.description}";
        }
    }

    public void ClearDescription()
    {
        if (skillExplainText != null)
        {
            // 鼠标移开时，可以清空，或者显示提示文字
            skillExplainText.text = "Hover over a skill to see details.";
        }
    }

    private void OnSkillClicked(SkillData skill)
    {
        Debug.Log($"Skill button clicked: {skill.skillName}");
        var selection = (skill.role == PlayerRole.Witch) ? currentWitchSelection : currentHunterSelection;
        
        // 如果点击的是已经选中的，不操作
        if (selection.Contains(skill)) 
        {
            Debug.Log($"{skill.skillName} is already in the selection list");
            return;
        }

        selection.RemoveAt(0); // 移除第一个
        selection.Add(skill);  // 添加新的
        
        UpdateVisuals();
        Save();
    }

    private void UpdateVisuals()
    {
        foreach (var kvp in skillButtons)
        {
            SkillData skill = kvp.Key;
            Image img = kvp.Value;
            GameObject btnGo = img.gameObject;

            // 判断是否被选中
            bool isSelected = currentWitchSelection.Contains(skill) || currentHunterSelection.Contains(skill);

            // 1. 描边处理 (Outline)
            var outline = btnGo.GetComponent<Outline>() ?? btnGo.AddComponent<Outline>();
            outline.enabled = isSelected;
            outline.effectColor = Color.yellow;
            // 【关键】增加描边粗细 (默认是 1, -1，这里改为 4, -4 或更大)
            outline.effectDistance = new Vector2(4, -4);

            // 2. 缩放处理 (Scale)
            // 选中时放大到 1.15 倍，未选中恢复 1.0 倍
            btnGo.transform.localScale = isSelected ? new Vector3(1.1f, 1.1f, 1f) : Vector3.one;

            // 3. 颜色微调 (亮度)
            // 选中时让原有的暗色调稍微亮一点
            if (isSelected)
            {
                // 如果是暗红，变亮红；如果是暗紫，变亮紫
                img.color = (skill.role == PlayerRole.Hunter) ? new Color(0.6f, 0f, 0f) : new Color(0.5f, 0f, 0.6f);
            }
            else
            {
                // 恢复暗色
                img.color = (skill.role == PlayerRole.Hunter) ? hunterColor : witchColor;
            }
        }
    }

    private void Save()
    {
        // 存入持久化的脚本类名，方便后续加载
        PlayerSettings.Instance.selectedWitchSkillNames = currentWitchSelection.Select(s => s.scriptClassName).ToList();
        PlayerSettings.Instance.selectedHunterSkillNames = currentHunterSelection.Select(s => s.scriptClassName).ToList();
        Debug.Log("Saved to PlayerSettings. Current Witch Skills: " + string.Join(", ", PlayerSettings.Instance.selectedWitchSkillNames));
    }
}