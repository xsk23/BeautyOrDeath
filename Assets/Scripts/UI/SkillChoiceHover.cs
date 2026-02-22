using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

public class SkillChoiceHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public TextMeshProUGUI targetText; // 指向所属面板的 ExplainText
    public string description;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (targetText != null)
        {
            targetText.text = description;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (targetText != null)
        {
            targetText.text = "Select your power...";
        }
    }
}