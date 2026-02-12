using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class WitchTrailRecorder : NetworkBehaviour
{
    [Header("记录设置")]
    public float recordTimeWindow = 15f; 
    public float recordInterval = 0.5f;

    private LinkedList<TrailSnapshot> snapshots = new LinkedList<TrailSnapshot>();
    private float timer = 0f;
    private WitchPlayer witchPlayer;

    public override void OnStartServer()
    {
        base.OnStartServer(); // 记得调用 base
        witchPlayer = GetComponent<WitchPlayer>();
        Debug.Log($"[Recorder] StartServer on {gameObject.name}. Ready to record.");
    }

    [ServerCallback]
    private void Update()
    {
        // 1. 如果没有 witchPlayer 组件，停止
        if (witchPlayer == null) return;

        timer += Time.deltaTime;

        if (timer >= recordInterval)
        {
            RecordSnapshot();
            timer = 0f;
        }
    }

    [Server]
    private void RecordSnapshot()
    {
        // 2. 如果已经死亡，不记录（但在调试阶段，我们打印一下）
        if (witchPlayer.isPermanentDead) 
        {
            // Debug.LogWarning($"[Recorder] {name} is dead, skipping record.");
            return;
        }

        TrailSnapshot snap = new TrailSnapshot
        {
            position = transform.position,
            rotation = transform.rotation,
            propID = witchPlayer.isMorphed ? witchPlayer.morphedPropID : -1
        };

        snapshots.AddLast(snap);

        // 限制队列长度
        int maxSnapshots = Mathf.CeilToInt(recordTimeWindow / recordInterval);
        while (snapshots.Count > maxSnapshots)
        {
            snapshots.RemoveFirst();
        }

        // ★★★ 调试日志：每记录 10 次打印一次，防止刷屏 ★★★
        // if (snapshots.Count % 10 == 0)
        // {
        //     Debug.Log($"[Recorder] {name} recording... Count: {snapshots.Count}. Pos: {transform.position}");
        // }
    }

    [Server]
    public List<TrailSnapshot> GetTrailsInArea(Vector3 center, float radius)
    {
        List<TrailSnapshot> result = new List<TrailSnapshot>();
        float sqrRadius = radius * radius;
        
        // ★★★ 调试日志：显示当前存储了多少个点，以及正在检测的范围 ★★★
        // Debug.Log($"[Recorder] Checking {name} (Total Snapshots: {snapshots.Count}) against center {center} with radius {radius}");

        foreach (var snap in snapshots)
        {
            float distSqr = Vector3.SqrMagnitude(snap.position - center);
            if (distSqr <= sqrRadius)
            {
                result.Add(snap);
            }
        }
        
        if (result.Count == 0 && snapshots.Count > 0)
        {
            // Debug.Log($"[Recorder] {name} has points, but none in range. Closest point dist: {Mathf.Sqrt(GetClosestDistSqr(center))}");
        }

        return result;
    }

    private float GetClosestDistSqr(Vector3 center)
    {
        float min = float.MaxValue;
        foreach (var snap in snapshots)
        {
            float d = Vector3.SqrMagnitude(snap.position - center);
            if (d < min) min = d;
        }
        return min;
    }
}