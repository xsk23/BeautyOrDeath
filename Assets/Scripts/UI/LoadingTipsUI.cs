using UnityEngine;
using TMPro;

public class LoadingTipsUI : MonoBehaviour
{
    public TextMeshProUGUI tipText;
    private string[] tips = {
        "Witches can possess ancient trees to move them!",
        "Hunters use dogs to track witch footprints.",
        "Don't forget to press F to use your items!",
        "Magic brooms allow you to double jump!",
        "Working together as a witch team makes control easier."
    };

    private int lastIndex = -1; // 记录上一次显示的索引

    void Start()
    {
        ShowRandomTip();
    }

    void Update()
    {
        // 检测鼠标左键点击 (0 是左键)
        // 这也兼容手机端的单指点击
        if (Input.GetMouseButtonDown(0))
        {
            ShowRandomTip();
        }
    }

    // 将逻辑封装成方法，方便多处调用
    public void ShowRandomTip()
    {
        if (tipText == null || tips.Length == 0) return;

        int newIndex = lastIndex;

        // 如果贴士数量大于1，则通过循环确保抽到跟上次不一样的贴士
        if (tips.Length > 1)
        {
            while (newIndex == lastIndex)
            {
                newIndex = Random.Range(0, tips.Length);
            }
        }
        else
        {
            newIndex = 0;
        }

        lastIndex = newIndex;
        tipText.text = tips[newIndex];
    }
}