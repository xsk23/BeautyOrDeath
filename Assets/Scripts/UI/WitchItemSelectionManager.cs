using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

public class WitchItemSelectionManager : MonoBehaviour
{
    [Header("Data")]
    public List<WitchItemData> allItems;
    public GameObject buttonPrefab;

    [Header("UI References")]
    public Transform itemButtonContainer; // 拖入 ItemButtonContainer
    public TextMeshProUGUI itemExplainText; // 拖入 ItemText

    [Header("Visual Settings")]
    public Color witchColor = new Color(0.2f, 0f, 0.3f); // 暗紫
    public Color highlightColor = Color.cyan;          // 道具选中用青色区分

    private WitchItemData currentSelection;
    private Dictionary<WitchItemData, Image> itemButtons = new Dictionary<WitchItemData, Image>();

    private void Start()
    {
        // 1. 默认选择第一个
        if (allItems.Count > 0) currentSelection = allItems[0];

        // 2. 生成按钮
        foreach (var item in allItems)
        {
            GameObject go = Instantiate(buttonPrefab, itemButtonContainer);
            go.GetComponentInChildren<TextMeshProUGUI>().text = ""; // 隐藏文字，只看图

            // 设置图片到子物体 Icon
            Transform iconTrans = go.transform.Find("Icon");
            if (iconTrans != null) iconTrans.GetComponent<Image>().sprite = item.icon;

            Image frameImg = go.GetComponent<Image>();
            frameImg.color = witchColor;

            // 绑定事件：悬浮看说明，点击选择
            SkillButtonUI hover = go.GetComponent<SkillButtonUI>() ?? go.AddComponent<SkillButtonUI>();
            // 注意：这里需要稍微修改之前的 SkillButtonUI 兼容 WitchItemData，或者直接在下面处理
            
            go.GetComponent<Button>().onClick.AddListener(() => OnItemClicked(item));
            
            itemButtons.Add(item, frameImg);
        }

        if (itemExplainText != null) itemExplainText.text = "Select a witch item.";
        UpdateVisuals();
        Save();
    }

    private void OnItemClicked(WitchItemData item)
    {
        currentSelection = item;
        ShowDescription(item);
        UpdateVisuals();
        Save();
    }

    public void ShowDescription(WitchItemData item)
    {
        if (itemExplainText != null)
        {
            itemExplainText.text = $"<color=#BB88FF><b>{item.itemName}</b></color>\n{item.description}";
        }
    }

    private void UpdateVisuals()
    {
        foreach (var kvp in itemButtons)
        {
            bool isSelected = (kvp.Key == currentSelection);
            var outline = kvp.Value.GetComponent<Outline>() ?? kvp.Value.gameObject.AddComponent<Outline>();
            outline.enabled = isSelected;
            outline.effectColor = highlightColor;
            outline.effectDistance = new Vector2(4, -4);
            kvp.Value.gameObject.transform.localScale = isSelected ? new Vector3(1.1f, 1.1f, 1f) : Vector3.one;
        }
    }

    private void Save()
    {
        if (currentSelection != null)
            PlayerSettings.Instance.selectedWitchItemName = currentSelection.scriptClassName;
    }
}