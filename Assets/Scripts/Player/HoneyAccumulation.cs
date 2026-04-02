using UnityEngine;
using Mirror;

public class HoneyAccumulation : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnSaturationChanged))]
    public float honeySaturation = 0f;

    public float decayRate = 12f;      // 每秒减少值
    public float stunThreshold = 80f;  // 80点触发定身
    public float stunDuration = 3.5f;

    private WitchPlayer witch;
    private Renderer[] witchRenderers;
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        witch = GetComponent<WitchPlayer>();
        // 获取女巫模型的所有渲染器用于变色
        witchRenderers = GetComponentsInChildren<Renderer>(true);
    }

    [ServerCallback]
    private void Update()
    {
        // 只有不处于被禁锢状态时才缓慢减少
        if (honeySaturation > 0 && !witch.isStunned)
        {
            honeySaturation = Mathf.Max(0, honeySaturation - decayRate * Time.deltaTime);
        }
    }

    [Server]
    public void ServerAddHoney(float amount)
    {
        if (witch.isPermanentDead || witch.isInvulnerable || witch.isInSecondChance) return;

        honeySaturation = Mathf.Min(100f, honeySaturation + amount);

        if (honeySaturation >= stunThreshold && !witch.isStunned)
        {
            StartCoroutine(HoneyStunRoutine());
        }
    }

    private System.Collections.IEnumerator HoneyStunRoutine()
    {
        witch.isStunned = true;
        GameManager.Instance?.ServerPlay3DAt("机械click音陷阱用", witch.transform.position);

        yield return new WaitForSeconds(stunDuration);

        // 如果没有被真实的物理网兜抓到，则解除定身
        if (!witch.isTrappedByNet)
        {
            witch.isStunned = false;
        }

        honeySaturation = 0f;
    }

    // 客户端钩子：当饱和度改变时改变身体颜色
    void OnSaturationChanged(float oldVal, float newVal)
    {
        float t = newVal / stunThreshold;
        // 颜色插值：正常色 -> 亮橙色
        Color targetTint = Color.Lerp(Color.white, new Color(1f, 0.7f, 0f), t);

        foreach (var r in witchRenderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;

            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorID, targetTint);
            r.SetPropertyBlock(mpb);
        }
    }
}