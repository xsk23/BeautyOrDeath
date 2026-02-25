using UnityEngine;
using Mirror;
using System.Collections;

public class WitchSkill_Chaos : SkillBase
{
    public float radius = 15f;
    public float duration = 5f;
    public float pushForce = 15f; // 【新增】撞击猎人的力度

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
        Quaternion originalRot = treeTrans.rotation;
        
        // 分配一个随机种子，让每棵树扭动的频率和路径不同
        float randomSeed = Random.Range(0f, 100f);

        while (timer < duration)
        {
            timer += Time.deltaTime;
            
            // 1. 更加剧烈和多方位的空间位移
            float timeParam = Time.time * 20f + randomSeed; // 加快晃动频率
            float offsetX = Mathf.Sin(timeParam) * 1.5f + Mathf.PerlinNoise(timeParam, 0) * 2f - 1f;
            float offsetZ = Mathf.Cos(timeParam * 1.2f) * 1.5f + Mathf.PerlinNoise(0, timeParam) * 2f - 1f;
            float offsetY = Mathf.Abs(Mathf.Sin(timeParam * 2f)) * 0.8f; // 轻微向上跳跃

            Vector3 offset = new Vector3(offsetX, offsetY, offsetZ);
            treeTrans.position = originalPos + offset;

            // 2. 剧烈旋转（模拟左右前后摇摆，增加视觉冲击）
            float angleX = Mathf.Sin(timeParam * 0.8f) * 25f;
            float angleZ = Mathf.Cos(timeParam * 0.9f) * 25f;
            float angleY = Mathf.Sin(timeParam * 0.5f) * 45f;
            treeTrans.rotation = originalRot * Quaternion.Euler(angleX, angleY, angleZ);

            // 3. 【新增】将附近的猎人撞得动来动去
            Collider[] colliders = Physics.OverlapSphere(treeTrans.position, 3.5f); // 碰撞检测范围稍大
            foreach(var col in colliders)
            {
                HunterPlayer hunter = col.GetComponent<HunterPlayer>() ?? col.GetComponentInParent<HunterPlayer>();
                if (hunter != null)
                {
                    CharacterController cc = hunter.GetComponent<CharacterController>();
                    if (cc != null)
                    {
                        // 计算弹开方向（从树的中心往外推）
                        Vector3 pushDir = hunter.transform.position - treeTrans.position;
                        pushDir.y = 0; // 只在水平面上施加撞击力，以免把猎人拍到地下
                        
                        // 稍微加点噪音让推力更不可预测
                        pushDir += new Vector3(Random.Range(-0.5f, 0.5f), 0, Random.Range(-0.5f, 0.5f));
                        pushDir.Normalize();

                        // 强行移动 CC 模拟撞击效果
                        cc.Move(pushDir * pushForce * Time.deltaTime);
                    }
                }
            }

            yield return null;
        }
        
        // 结束时恢复原样
        treeTrans.position = originalPos;
        treeTrans.rotation = originalRot;
    }
}