using UnityEngine;
using System.Collections;

public class TrailFireflyEffect : MonoBehaviour
{
    private Color themeColor;
    private float lifeTime;
    private Renderer[] renderers;
    private MaterialPropertyBlock propBlock;
    
    // Shader 属性
    private static readonly int ColorPropID = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropID = Shader.PropertyToID("_BaseColor");

    private float spawnTime;
    private GameObject particleInstance; // 保存生成的粒子实例

    // 【修改】接收传进来的 Prefab
    public void Setup(Color fireflyColor, float duration, GameObject particlePrefab)
    {
        themeColor = fireflyColor;
        lifeTime = duration;
        renderers = GetComponentsInChildren<Renderer>();
        propBlock = new MaterialPropertyBlock();
        spawnTime = Time.time;

        // 【新增】直接使用预制体生成
        if (particlePrefab != null)
        {
            // 在当前黑影的位置生成你调好的粒子
            particleInstance = Instantiate(particlePrefab, transform);
            particleInstance.transform.localPosition = Vector3.up * 1.0f; // 稍微抬高到身体中心

            // 获取粒子组件，覆盖颜色为你算好的女巫专属色
            ParticleSystem ps = particleInstance.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                // 先强行停止，防止因为预制体自带 PlayOnAwake 导致报错
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                
                // 仅覆盖颜色，大小/速度/拖尾全按你在编辑器里调的算
                var main = ps.main;
                main.startColor = themeColor; 

                // 重新播放
                ps.Play();
            }
        }

        // 开启生命周期协程
        StartCoroutine(LifeRoutine());
    }

    private void Update()
    {
        if (renderers == null || renderers.Length == 0) return;

        // 【黑影闪烁逻辑】使用正弦波制造“一闪一闪”的呼吸感 (频率 8.0f 可自己调)
        float pulse = Mathf.Abs(Mathf.Sin((Time.time - spawnTime) * 8.0f)); 
        
        // 基础透明度 0.1，最高闪烁到 0.4
        float currentAlpha = Mathf.Lerp(0.1f, 0.4f, pulse);

        // 如果快要消失了，整体淡出
        float timeLeft = (spawnTime + lifeTime) - Time.time;
        if (timeLeft < 1.0f) 
        {
            currentAlpha *= timeLeft; // 最后一秒渐渐消失
        }

        foreach (var r in renderers)
        {
            r.GetPropertyBlock(propBlock);

            Color shadowColor = themeColor * 0.2f; 
            shadowColor.a = currentAlpha;

            propBlock.SetColor(ColorPropID, shadowColor);
            propBlock.SetColor(BaseColorPropID, shadowColor);
            r.SetPropertyBlock(propBlock);
        }
    }

    private IEnumerator LifeRoutine()
    {
        // 存活指定时间后销毁自己（由于粒子是子物体，会跟着一起销毁）
        yield return new WaitForSeconds(lifeTime);
        Destroy(gameObject);
    }
}