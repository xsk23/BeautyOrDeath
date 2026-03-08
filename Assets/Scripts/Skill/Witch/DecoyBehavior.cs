using UnityEngine;
using Mirror;

[RequireComponent(typeof(CharacterController))]
public class DecoyBehavior : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float lifeTime = 10f; 
    public float moveSpeed = 5f; 
    public float gravity = -9.81f; 

    [Header("Sync Settings")]
    [SyncVar(hook = nameof(OnPropIDChanged))]
    public int propID = -1;
    [Header("Visual References")]
    public GameObject humanVisualRoot; 
    private CharacterController cc;
    private Vector3 moveDir;
    private float verticalVelocity; 
    private float jitterTimer = 0f; 
    public Animator animator;

    [SyncVar]
    private float syncedSpeed; 

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    public override void OnStartServer()
    {
        moveDir = transform.forward;
        Destroy(gameObject, lifeTime);
    }

    [ServerCallback]
    private void Update()
    {
        if (cc == null) return;

        jitterTimer += Time.deltaTime;
        if (jitterTimer > 1.0f) 
        {
            float jitter = Random.Range(-45f, 45f);
            Quaternion turn = Quaternion.AngleAxis(jitter, Vector3.up);
            moveDir = turn * moveDir;
            jitterTimer = 0;
        }

        if (cc.isGrounded && verticalVelocity < 0)
        {   
            verticalVelocity = -2f; 
        }
        else
        {   
            verticalVelocity += gravity * Time.deltaTime;
        }

        Vector3 finalMove = moveDir.normalized * moveSpeed;
        finalMove.y = verticalVelocity;

        cc.Move(finalMove * Time.deltaTime);
        syncedSpeed = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;

        Vector3 faceDir = new Vector3(moveDir.x, 0, moveDir.z);
        if (faceDir != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(faceDir), Time.deltaTime * 5f);
        }
    }

    private void LateUpdate()
    {
        UpdateAnimator();
    }

    private void UpdateAnimator()
    {
        if (animator == null && propID == -1 && humanVisualRoot != null)
        {
            animator = humanVisualRoot.GetComponentInChildren<Animator>();
        }

        if (animator != null)
        {
            animator.SetFloat("speed", syncedSpeed);
        }
    }

    void OnPropIDChanged(int oldID, int newID)
    {
        if (isServer) return; 
        ApplyVisuals(newID);
    }

    [Server]
    public void ServerSetup(int initialPropID)
    {
        this.propID = initialPropID;
        ApplyVisuals(initialPropID);
    }

    private void ApplyVisuals(int newID)
    {
        animator = null; 
        foreach (Transform child in transform) {
            if (child.gameObject != humanVisualRoot && child.name != "FX")
                Destroy(child.gameObject);
        }

        if (newID == -1)
        {
            // 防呆设计：检查是不是误把根物体拖给了 humanVisualRoot
            if (humanVisualRoot != null)
            {
                if (humanVisualRoot == this.gameObject)
                    Debug.LogError("[Decoy] 错误：不能把分身自身的根物体拖入 Human Visual Root！");
                else
                    humanVisualRoot.SetActive(true);

                animator = humanVisualRoot.GetComponentInChildren<Animator>(); 
                UpdateColliderDimensions(humanVisualRoot);
            }
        }
        else
        {
            if (humanVisualRoot != null && humanVisualRoot != this.gameObject) 
                humanVisualRoot.SetActive(false);

            if (PropDatabase.Instance != null && PropDatabase.Instance.GetPropPrefab(newID, out GameObject prefab))
            {
                GameObject visual = Instantiate(prefab, transform);
                visual.SetActive(true); 
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                
                // 【核心修复 B】: 像女巫变身一样，剔除模型上自带的多余脚本（防止和分身逻辑打架）
                Component[] allComps = visual.GetComponentsInChildren<Component>();
                foreach (var comp in allComps)
                {
                    if (comp is MonoBehaviour script && !(comp is Animator))
                    {
                        script.enabled = false;
                    }
                }

                animator = visual.GetComponent<Animator>();                 
                foreach(var c in visual.GetComponentsInChildren<Collider>()) c.enabled = false;

                var pt = GetComponent<PropTarget>();
                if (pt != null) pt.ManualInit(newID, visual);

                UpdateColliderDimensions(visual);
            }
        }
    }

    private void UpdateColliderDimensions(GameObject visualModel)
    {
        if (cc == null) cc = GetComponent<CharacterController>();

        Renderer[] rs = visualModel.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;

        float minY = float.MaxValue;
        float maxY = float.MinValue;
        float maxSide = 0f;

        bool foundRenderer = false;
        foreach (var r in rs)
        {
            if (r is ParticleSystemRenderer) continue; 
            
            Bounds b = r.bounds;
            Vector3 localMin = transform.InverseTransformPoint(b.min);
            Vector3 localMax = transform.InverseTransformPoint(b.max);

            minY = Mathf.Min(minY, localMin.y);
            maxY = Mathf.Max(maxY, localMax.y);
            
            float sideX = Mathf.Max(Mathf.Abs(localMin.x), Mathf.Abs(localMax.x));
            float sideZ = Mathf.Max(Mathf.Abs(localMin.z), Mathf.Abs(localMax.z));
            maxSide = Mathf.Max(maxSide, sideX, sideZ);
            
            foundRenderer = true;
        }

        if (!foundRenderer) return;

        float height = maxY - minY;
        float centerY = (minY + maxY) / 2f;

        cc.enabled = false; 
        
        cc.height = height;
        cc.center = new Vector3(0, centerY, 0);
        cc.radius = Mathf.Clamp(maxSide, 0.2f, 0.5f); 
        cc.stepOffset = Mathf.Min(0.3f, height * 0.3f);

        cc.enabled = true;
        
        transform.position += Vector3.up * 0.5f; 

        // 【核心修复 C】: 只有当 GameObject 已经激活，并且 CC 也启用的情况下，调用 Move 才不会报错！
        if (gameObject.activeInHierarchy && cc.enabled)
        {
            cc.Move(Vector3.down * 0.5f);
        }
        
        Debug.Log($"[Decoy] Adjusting CC: Height={height}, CenterY={centerY}, Morphed={propID != -1}");
    }
}