using UnityEngine;

public class WorldBoundaryManager : MonoBehaviour
{
    public static WorldBoundaryManager Instance { get; private set; }

    [Header("设置")]
    public bool isActive = true;
    public float radiusOffset = 0.5f; // 考虑到角色半径的缓冲距离

    private SphereCollider sphereCollider;

    public Vector3 Center => transform.position;
    public float Radius => (sphereCollider != null) ? (sphereCollider.radius * transform.lossyScale.x) : 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        sphereCollider = GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            Debug.LogError("WorldBoundaryManager: 找不到 SphereCollider！");
        }
    }

    // 提供给所有物体使用的静态约束方法
    public Vector3 GetConstrainedPosition(Vector3 currentPos, float characterRadius = 0.5f)
    {
        if (!isActive) return currentPos;

        Vector3 center = Center;
        float radius = Radius - characterRadius - radiusOffset;
        
        float dist = Vector3.Distance(currentPos, center);

        if (dist > radius)
        {
            Vector3 fromCenterToPos = (currentPos - center).normalized;
            return center + fromCenterToPos * radius;
        }

        return currentPos;
    }

    // 用于 AI 逻辑：判断一个点是否在球体内
    public bool IsWithinBoundary(Vector3 targetPos)
    {
        return Vector3.Distance(targetPos, Center) < (Radius - 1f);
    }
}