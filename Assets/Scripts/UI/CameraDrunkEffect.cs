using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Camera))]
[ExecuteInEditMode] // 允许在编辑模式下运行，实时预览
public class CameraDrunkEffect : MonoBehaviour
{
    public static CameraDrunkEffect Instance;

    [Header("眩晕材质 (拖入使用了 DrunkRipple Shader 的 Material)")]
    public Material effectMaterial;

    [Header("Editor 预览测试 (仅在不播放技能时有效)")]
    [Range(0f, 0.5f)] 
    public float previewIntensity = 0f;
    
    private float currentIntensity = 0f;
    private Coroutine activeRoutine;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// 触发屏幕眩晕效果 (游戏运行时调用)
    /// </summary>
    public void PlayDrunkEffect(float duration, float maxIntensity = 0.08f)
    {
        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(EffectRoutine(duration, maxIntensity));
    }

    private IEnumerator EffectRoutine(float duration, float maxIntensity)
    {
        float timer = 0;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            // 效果随时间逐渐减弱
            currentIntensity = Mathf.Lerp(maxIntensity, 0f, timer / duration);
            yield return null;
        }
        currentIntensity = 0f;
        activeRoutine = null;
    }

    // 屏幕后处理魔法函数
    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (effectMaterial != null)
        {
            // 如果技能正在播放(currentIntensity > 0)，就用技能的强度
            // 否则，使用你在 Inspector 里拖动的预览强度(previewIntensity)
            float finalIntensity = (currentIntensity > 0.001f) ? currentIntensity : previewIntensity;

            if (finalIntensity > 0.001f)
            {
                // 将最终强度传递给 Shader 的 _DistortionStrength 属性
                effectMaterial.SetFloat("_DistortionStrength", finalIntensity);
                Graphics.Blit(src, dest, effectMaterial);
                return;
            }
        }
        
        // 没扭曲时原画输出
        Graphics.Blit(src, dest);
    }
}