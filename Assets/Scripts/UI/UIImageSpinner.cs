using System.Collections;
using UnityEngine;

public class UIImageSpinner : MonoBehaviour
{
    [Header("旋转设置")]
    public float duration = 1.0f;     // 转一圈所需时间
    public bool autoStart = true;     // 是否脚本启动时就开始转
    public bool isLooping = true;     // 是否循环不停
    public bool clockwise = true;     // 是否顺时针

    private RectTransform rectTransform;
    private Coroutine spinCoroutine;

    private void Awake()
    {
        // UI物体必须使用 RectTransform
        rectTransform = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        if (autoStart)
        {
            StartSpinning();
        }
    }

    // --- 外部调用接口 ---

    // 开始旋转
    public void StartSpinning()
    {
        if (spinCoroutine == null)
        {
            spinCoroutine = StartCoroutine(SpinRoutine());
        }
    }

    // 停止旋转
    public void StopSpinning()
    {
        if (spinCoroutine != null)
        {
            StopCoroutine(spinCoroutine);
            spinCoroutine = null;
        }
    }

    // --- 内部逻辑 ---

    private IEnumerator SpinRoutine()
    {
        float direction = clockwise ? -360f : 360f;
        
        while (true)
        {
            float elapsed = 0f;
            // 每次循环前重置角度，防止数值无限叠加导致精度问题
            rectTransform.localRotation = Quaternion.identity;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float percent = elapsed / duration;
                
                // 计算当前旋转
                rectTransform.localRotation = Quaternion.Euler(0, 0, percent * direction);
                yield return null;
            }

            if (!isLooping) break; // 如果不循环，转完一圈跳出
        }

        rectTransform.localRotation = Quaternion.Euler(0, 0, 0);
        spinCoroutine = null;
    }
}