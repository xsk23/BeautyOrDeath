    using UnityEngine;
using Mirror;
using System.Collections;


public class WitchSkill_Chaos : SkillBase
{
    public float radius = 15f;
    public float duration = 5f;

    protected override void OnCast()
    {
        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used skill: Chaos! Disturbing nearby trees.</color>");
        // 找到周围的普通树
        Collider[] hits = Physics.OverlapSphere(ownerPlayer.transform.position, radius);
        foreach (var hit in hits)
        {
            PropTarget prop = hit.GetComponentInParent<PropTarget>();
            if (prop != null && !prop.isAncientTree && prop.isStaticTree)
            {
                // 开启协程让它们乱动
                StartCoroutine(ChaosRoutine(prop.transform));
            }
        }
    }

    [Server]
    IEnumerator ChaosRoutine(Transform treeTrans)
    {
        float timer = 0f;
        Vector3 originalPos = treeTrans.position;
        
        while (timer < duration)
        {
            timer += Time.deltaTime;
            // 简单的位移噪点
            Vector3 offset = new Vector3(Mathf.Sin(Time.time * 5 + treeTrans.GetInstanceID()), 0, Mathf.Cos(Time.time * 5)) * 0.5f;
            treeTrans.position = originalPos + offset;
            yield return null;
        }
        treeTrans.position = originalPos;
    }
}