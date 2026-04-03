using UnityEngine;
using Mirror;
using System.Collections;

public class HoneyAccumulation : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnSaturationChanged))]
    public float honeySaturation = 0f;

    [SyncVar] public bool hasVisibleDecal = false; // 标记身上是否有贴花

    public float decayRate = 12f;
    public float stunThreshold = 80f;
    public float stunDuration = 3.5f;

    private WitchPlayer witch;
    private Renderer[] witchRenderers;
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        witch = GetComponent<WitchPlayer>();
        witchRenderers = GetComponentsInChildren<Renderer>(true);
    }

    [ServerCallback]
    private void Update()
    {
        if (honeySaturation > 0 && !witch.isStunned)
        {
            honeySaturation = Mathf.Max(0, honeySaturation - decayRate * Time.deltaTime);
        }
    }

    [Server]
    public void ServerAddHoney(float amount, float decalDuration)
    {
        if (witch.isPermanentDead || witch.isInvulnerable || witch.isInSecondChance) return;

        honeySaturation = Mathf.Min(100f, honeySaturation + amount);

        // 如果身上没有贴花，则允许子弹生成贴花，并开启倒计时重置标记
        if (!hasVisibleDecal)
        {
            hasVisibleDecal = true;
            StartCoroutine(ResetDecalFlag(decalDuration));
        }

        if (honeySaturation >= stunThreshold && !witch.isStunned)
        {
            StartCoroutine(HoneyStunRoutine());
        }
    }

    // =================================================================
    // 【核心新增】由服务器通知所有客户端：将刚生成的贴花绑定到女巫身上
    // =================================================================
    [ClientRpc]
    public void RpcAttachDecal(NetworkIdentity decalIdentity)
    {
        if (decalIdentity == null || decalIdentity.gameObject == null) return;

        Transform decalTransform = decalIdentity.transform;

        // 1. 建立父子关系
        decalTransform.SetParent(this.transform);

        // 2. 强行修正相对位置、旋转和缩放 (向下投影覆盖女巫)
        decalTransform.localPosition = new Vector3(0, 1.0f, 0);
        decalTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        decalTransform.localScale = Vector3.one;

        // 3. 【关键防御】如果预制体带了位置同步脚本，在成为子物体后必须禁用它
        // 否则 Mirror 的网络同步会把客户端的贴花坐标不断扯回旧的世界坐标系
        MonoBehaviour[] scripts = decalIdentity.GetComponents<MonoBehaviour>();
        foreach (var s in scripts)
        {
            if (s.GetType().Name.Contains("NetworkTransform"))
            {
                s.enabled = false;
            }
        }
    }

    private IEnumerator ResetDecalFlag(float delay)
    {
        yield return new WaitForSeconds(delay);
        hasVisibleDecal = false;
    }

    private IEnumerator HoneyStunRoutine()
    {
        witch.isStunned = true;
        GameManager.Instance?.ServerPlay3DAt("机械click音陷阱用", witch.transform.position);
        yield return new WaitForSeconds(stunDuration);
        if (!witch.isTrappedByNet) witch.isStunned = false;
        honeySaturation = 0f;
    }

    void OnSaturationChanged(float oldVal, float newVal)
    {
        float t = newVal / stunThreshold;
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