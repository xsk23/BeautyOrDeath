using UnityEngine;
using System.Collections;

public class CameraDrunkEffect : MonoBehaviour
{
    public static CameraDrunkEffect Instance;

    [Header("URP 眩晕材质 (拖入 Mat_DrunkEffect)")]
    public Material effectMaterial;

    [Header("Editor 预览测试")]
    [Range(0f, 0.5f)] 
    public float previewIntensity = 0f;
    
    private float currentIntensity = 0f;
    private Coroutine activeRoutine;

    private static readonly int StrengthProp = Shader.PropertyToID("_DistortionStrength");

    private void Awake()
    {
        Instance = this;
        // 每次这个脚本醒来（进入游戏对局），强制清零
        ResetEffect();
    }

    // 【新增】无论是因为死亡、切场景还是关游戏，只要脚本被禁用，必须清零！
    private void OnDisable()
    {
        ResetEffect();
    }

    private void OnApplicationQuit()
    {
        ResetEffect();
    }

    // 统一的清零方法
    private void ResetEffect()
    {
        currentIntensity = 0f;
        previewIntensity = 0f;
        if (effectMaterial != null) 
        {
            effectMaterial.SetFloat(StrengthProp, 0f);
        }
    }

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
            currentIntensity = Mathf.Lerp(maxIntensity, 0f, timer / duration);
            yield return null;
        }
        currentIntensity = 0f;
        activeRoutine = null;
    }

    private void Update()
    {
        if (effectMaterial == null) return;

        float finalIntensity = (currentIntensity > 0.001f) ? currentIntensity : previewIntensity;
        effectMaterial.SetFloat(StrengthProp, finalIntensity);
    }
}