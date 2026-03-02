using UnityEngine;

public class RandomAnimationPlayer : MonoBehaviour
{
    private Animator animator;
    public string[] stateNames = { "sad_idle", "sad_idle 0", "sad_idle 1" };
    
    void Awake()
    {
        // 【关键修改】使用 GetComponentInChildren 确保能找到子物体上的 Animator
        animator = GetComponentInChildren<Animator>();
    }

    void OnEnable()
    {
        // 增加空检查
        if (animator != null) PlayRandom();
    }

    void Update()
    {
        if (animator == null) return;

        // 检查当前动画层 0 是否播放完毕
        if (animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 0.95f && !animator.IsInTransition(0))
        {
            PlayRandom();
        }
    }

    public void PlayRandom()
    {
        if (animator == null || stateNames == null || stateNames.Length == 0) return;

        int index = Random.Range(0, stateNames.Length);
        animator.CrossFade(stateNames[index], 0.25f);
    }
}