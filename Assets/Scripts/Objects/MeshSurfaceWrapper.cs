using UnityEngine;

public class MeshSurfaceWrapper : MonoBehaviour
{
    public float maxThickness = 0.2f; // 中心厚度
    public LayerMask surfaceLayer;    // 要包裹的层级
    public float raycastDistance = 5f;
    public bool wrapOnStart = true;

    void Start()
    {
        if(wrapOnStart) WrapMesh();
    }

    [ContextMenu("Wrap Now")]
    public void WrapMesh()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        // 必须实例化，否则会修改所有使用该 Mesh 的物体
        Mesh mesh = Instantiate(mf.sharedMesh); 
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;

        // 获取物体的中心点，用于计算边缘衰减（让边缘变薄，不那么突兀）
        Bounds bounds = mf.sharedMesh.bounds;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 worldPos = transform.TransformPoint(vertices[i]);
            
            // 从顶点上方发射射线
            Vector3 rayOrigin = worldPos + transform.up * 2f; 
            if (Physics.Raycast(rayOrigin, -transform.up, out RaycastHit hit, raycastDistance, surfaceLayer))
            {
                // 计算该点到中心的距离百分比 (0为中心，1为边缘)
                float distFromCenter = new Vector2(vertices[i].x / bounds.extents.x, vertices[i].z / bounds.extents.z).magnitude;
                float currentThickness = Mathf.Lerp(maxThickness, 0.01f, distFromCenter);

                // 设置顶点位置：击中点 + 表面法线 * 厚度
                Vector3 targetWorldPos = hit.point + hit.normal * currentThickness;
                vertices[i] = transform.InverseTransformPoint(targetWorldPos);
                
                // 更新顶点法线为表面法线，保证光照正确
                normals[i] = transform.InverseTransformDirection(hit.normal);
                
                // 调试：在场景窗口画出绿线表示击中
                Debug.DrawLine(rayOrigin, hit.point, Color.green, 2f);
            }
            else
            {
                // 调试：红线表示没击中表面
                Debug.DrawRay(rayOrigin, -transform.up * raycastDistance, Color.red, 2f);
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.RecalculateBounds();
        mf.mesh = mesh;

        // 如果有碰撞体，更新它，让女巫能踩在变形后的模型上
        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc != null) mc.sharedMesh = mesh;
    }
}