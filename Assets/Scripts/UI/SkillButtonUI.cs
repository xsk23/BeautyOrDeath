using UnityEngine;
using UnityEngine.EventSystems;

public class SkillButtonUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private SkillData skillData;
    private SkillSelectionManager manager;

    // 由 Manager 在生成按钮时调用，初始化数据
    public void Setup(SkillData data, SkillSelectionManager selectionManager)
    {
        skillData = data;
        manager = selectionManager;
    }

    // 鼠标进入时触发
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (manager != null && skillData != null)
        {
            manager.ShowDescription(skillData);
        }
    }

    // 鼠标离开时触发
    public void OnPointerExit(PointerEventData eventData)
    {
        // if (manager != null)
        // {
        //     manager.ClearDescription();
        // }
    }
}