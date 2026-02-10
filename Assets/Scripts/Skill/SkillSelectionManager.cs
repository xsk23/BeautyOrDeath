using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class SkillSelectionManager : MonoBehaviour
{
    [Header("Skill Data")]
    public List<SkillData> allSkills;
    public GameObject buttonPrefab;

    [Header("Witch UI References")]
    public Transform witchButtonContainer;
    public TextMeshProUGUI witchExplainText;

    [Header("Hunter UI References")]
    public Transform hunterButtonContainer;
    public TextMeshProUGUI hunterExplainText;

    [Header("Frame Colors (Base Border)")]
    public Color hunterFrameColor = new Color(0.3f, 0f, 0f); // 深暗红
    public Color witchFrameColor = new Color(0.2f, 0f, 0.3f);  // 深暗紫

    [Header("Highlight Colors (Selected Outline)")]
    public Color hunterSelectedColor = Color.red;         // 鲜红
    public Color witchSelectedColor = new Color(0.7f, 0f, 1f); // 亮紫

    private List<SkillData> currentWitchSelection = new List<SkillData>();
    private List<SkillData> currentHunterSelection = new List<SkillData>();
    private Dictionary<SkillData, Image> skillFrameImages = new Dictionary<SkillData, Image>();

    private void Start()
    {
        currentWitchSelection = allSkills.Where(s => s.role == PlayerRole.Witch).Take(2).ToList();
        currentHunterSelection = allSkills.Where(s => s.role == PlayerRole.Hunter).Take(2).ToList();

        foreach (var skill in allSkills)
        {
            Transform targetContainer = (skill.role == PlayerRole.Witch) ? witchButtonContainer : hunterButtonContainer;
            if (targetContainer == null) continue;

            GameObject go = Instantiate(buttonPrefab, targetContainer);
            go.GetComponentInChildren<TextMeshProUGUI>().text = skill.skillName;
            
            // --- 核心逻辑修改：设置图标到子物体上 ---
            // 假设你的子物体叫 "Icon"
            Transform iconTrans = go.transform.Find("Icon");
            if (iconTrans != null)
            {
                Image iconImg = iconTrans.GetComponent<Image>();
                iconImg.sprite = skill.icon;
                iconImg.preserveAspect = true;
            }

            // 获取根物体的 Image (作为边框)
            Image frameImg = go.GetComponent<Image>();
            frameImg.color = (skill.role == PlayerRole.Hunter) ? hunterFrameColor : witchFrameColor;
            
            SkillButtonUI hoverScript = go.GetComponent<SkillButtonUI>() ?? go.AddComponent<SkillButtonUI>();
            hoverScript.Setup(skill, this);

            go.GetComponent<Button>().onClick.AddListener(() => OnSkillClicked(skill));
            
            skillFrameImages.Add(skill, frameImg);
        }
        
        UpdateVisuals();
        Save();
    }

    // 统一显示逻辑：自动识别角色并更新对应的 Text
    public void ShowDescription(SkillData skill)
    {
        TextMeshProUGUI targetText = (skill.role == PlayerRole.Witch) ? witchExplainText : hunterExplainText;
        
        if (targetText != null)
        {
            string colorHex = (skill.role == PlayerRole.Hunter) ? "#FF4444" : "#BB88FF";
            targetText.text = $"<color={colorHex}><b>{skill.skillName}</b></color>\n{skill.description}";
        }
    }

    private void OnSkillClicked(SkillData skill)
    {
        var selection = (skill.role == PlayerRole.Witch) ? currentWitchSelection : currentHunterSelection;
        if (selection.Contains(skill)) return;

        ShowDescription(skill);
        selection.RemoveAt(0); 
        selection.Add(skill);  
        
        UpdateVisuals();
        Save();
    }

    private void UpdateVisuals()
    {
        foreach (var kvp in skillFrameImages)
        {
            SkillData skill = kvp.Key;
            Image frameImg = kvp.Value; // 根物体的边框图
            GameObject btnGo = frameImg.gameObject;

            bool isSelected = currentWitchSelection.Contains(skill) || currentHunterSelection.Contains(skill);

            // 1. 处理描边 (Outline)
            var outline = btnGo.GetComponent<Outline>() ?? btnGo.AddComponent<Outline>();
            outline.enabled = isSelected;
            
            // 选中时，描边颜色使用亮色系的红/紫
            if (isSelected)
            {
                outline.effectColor = (skill.role == PlayerRole.Hunter) ? hunterSelectedColor : witchSelectedColor;
                outline.effectDistance = new Vector2(5, -5); // 加厚描边
                btnGo.transform.localScale = new Vector3(1.06f, 1.06f, 1f); // 稍微变大
            }
            else
            {
                btnGo.transform.localScale = Vector3.one;
            }

            // 2. 处理边框颜色 (选中时边框也可以稍微亮一点点，或者保持暗色)
            if (isSelected)
            {
                frameImg.color = (skill.role == PlayerRole.Hunter) ? hunterFrameColor * 1.5f : witchFrameColor * 1.5f;
            }
            else
            {
                frameImg.color = (skill.role == PlayerRole.Hunter) ? hunterFrameColor : witchFrameColor;
            }
        }
    }

    private void Save()
    {
        PlayerSettings.Instance.selectedWitchSkillNames = currentWitchSelection.Select(s => s.scriptClassName).ToList();
        PlayerSettings.Instance.selectedHunterSkillNames = currentHunterSelection.Select(s => s.scriptClassName).ToList();
    }
}