# Code Repository Contents

## PlayerSkillManager.cs

```cs
using UnityEngine;
using Mirror;



public class PlayerSkillManager : NetworkBehaviour
{
    [Header("Skills Configuration")]
    public SkillBase[] skills; // 在 Inspector 中把具体的技能组件拖进去

    private GamePlayer player;

    public override void OnStartLocalPlayer()
    {
        player = GetComponent<GamePlayer>();
        
        // 初始化 UI
        if (SceneScript.Instance != null && SceneScript.Instance.skillSlots != null)
        {
            for (int i = 0; i < skills.Length; i++)
            {
                if (i < SceneScript.Instance.skillSlots.Length && skills[i] != null)
                {   
                    //Name of its icon
                    Debug.Log($"<color=yellow>[SkillManager] Initializing skill slot {i} with skill: {skills[i].icon.name}</color>");
                    skills[i].Init(player);
                    SceneScript.Instance.skillSlots[i].Setup(skills[i].icon, skills[i].triggerKey.ToString());
                    SceneScript.Instance.skillSlots[i].gameObject.SetActive(true);
                }
            }
        }
    }

    public override void OnStartServer()
    {
        // 服务器端初始化拥有者引用
        player = GetComponent<GamePlayer>();
        foreach (var skill in skills)
        {
            if(skill != null) skill.Init(player);
        }
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        // 1. 处理输入
        if (Cursor.lockState == CursorLockMode.Locked && !player.isChatting && !player.isStunned)
        {
            foreach (var skill in skills)
            {
                if (skill != null && Input.GetKeyDown(skill.triggerKey))
                {
                    skill.TryCast();
                }
            }
        }

        // 2. 更新 UI 冷却遮罩
        if (SceneScript.Instance != null && SceneScript.Instance.skillSlots != null)
        {
            for (int i = 0; i < skills.Length; i++)
            {
                if (i < SceneScript.Instance.skillSlots.Length && skills[i] != null)
                {
                    SceneScript.Instance.skillSlots[i].UpdateCooldown(skills[i].CooldownRatio);
                }
            }
        }
    }
}
```

## SkillBase.cs

```cs
using UnityEngine;
using Mirror;

public abstract class SkillBase : NetworkBehaviour
{
    [Header("Skill Settings")]
    public string skillName;
    public Sprite icon;
    public float cooldownTime = 5f;
    public KeyCode triggerKey;
    
    [SyncVar]
    private double lastUseTime;

    protected GamePlayer ownerPlayer;

    public float CooldownRatio
    {
        get
        {
            float duration = (float)(NetworkTime.time - lastUseTime);
            if (duration >= cooldownTime) return 0f;
            return 1f - (duration / cooldownTime);
        }
    }

    public bool IsReady => (NetworkTime.time - lastUseTime) >= cooldownTime;

    public void Init(GamePlayer player)
    {
        ownerPlayer = player;
        lastUseTime = -cooldownTime; // 初始就绪
    }

    // 客户端尝试释放技能
    public void TryCast()
    {
        if (IsReady)
        {
            CmdCast();
        }
    }

    [Command]
    private void CmdCast()
    {
        if (!IsReady) return;
        
        // 记录时间 (NetworkTime 用于同步)
        lastUseTime = NetworkTime.time;

        // 执行具体逻辑
        OnCast();
    }

    // 子类实现具体的技能逻辑 (服务器端执行)
    protected abstract void OnCast();
}
```

## SkillSlotUI.cs

```cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillSlotUI : MonoBehaviour
{
    [Header("UI References")]
    public Image skillIcon;        // 技能图标
    public Image cooldownOverlay;  // 冷却遮罩 (Fill Type = Radial 360)
    public TextMeshProUGUI keyText; // 按键提示 (Q, E, R)

    public void Setup(Sprite icon, string key)
    {
        if (skillIcon != null && icon != null) skillIcon.sprite = icon;
        if (keyText != null) keyText.text = key;
        if (cooldownOverlay != null) cooldownOverlay.fillAmount = 0;
    }

    public void UpdateCooldown(float ratio)
    {
        if (cooldownOverlay != null)
        {
            cooldownOverlay.fillAmount = ratio;
        }
    }
}
```

## Hunter\DogSkillBehavior.cs

```cs
using UnityEngine;
using Mirror;
using Controller; // 引用你的 CreatureMover 所在的命名空间

public class DogSkillBehavior : NetworkBehaviour
{
    [Header("设置")]
    public float runSpeed = 8f;      // 奔跑速度
    public float lifeTime = 5f;      // 存在时间（跑多远后消失）
    public float detectRadius = 4f;  // 检测半径
    public LayerMask targetLayer;    // 目标层级 (设置为 Player 层)

    [Header("引用")]
    private CreatureMover mover;
    private bool hasBarking = false; // 是否已经叫过

    private void Awake()
    {
        mover = GetComponent<CreatureMover>();
        // 覆盖 CreatureMover 的速度设置，确保它跑得够快
        if (mover != null)
        {
            mover.m_RunSpeed = runSpeed;
            // 如果你的 CreatureMover 里的 WalkSpeed 也是 public 的，最好也改一下
            mover.m_WalkSpeed = runSpeed; 
        }
    }

    public override void OnStartServer()
    {
        // 时间到自动销毁
        Destroy(gameObject, lifeTime);
    }

    [ServerCallback]
    private void Update()
    {
        // 1. 强制向前跑
        // CreatureMover 的 SetInput 需要 (input, target, isRun, isJump)
        // input 传入 (0, 1) 代表向前，target 传入前方的一个点
        if (mover != null)
        {
            Vector3 forwardPoint = transform.position + transform.forward * 5f;
            // 这里的 Input (0,1) 会被 CreatureMover 转换为向前的移动
            // true 表示跑步状态 (isRun)
            mover.SetInput(new Vector2(0, 1), forwardPoint, true, false);
        }

        // 2. 检测女巫
        if (!hasBarking)
        {
            DetectWitch();
        }
    }

    [Server]
    void DetectWitch()
    {
        // 检测周围的碰撞体
        Collider[] hits = Physics.OverlapSphere(transform.position, detectRadius, targetLayer);
        foreach (var hit in hits)
        {
            // 尝试获取 WitchPlayer 组件
            // 兼容碰撞体在子物体或者父物体的情况
            WitchPlayer witch = hit.GetComponent<WitchPlayer>() ?? hit.GetComponentInParent<WitchPlayer>();

            if (witch != null && !witch.isPermanentDead)
            {
                // 找到了！
                hasBarking = true;
                
                // 1. 发出声音/特效
                RpcBarkEffect(witch.transform.position);

                // 2. 可选：显形女巫 (如果你想让狗能破隐身)
                if (witch.isMorphed)
                {
                    // 这里可以直接给个提示，或者稍微减速女巫
                    Debug.Log($"[Dog] Found witch: {witch.playerName}");
                }

                // 3. 找到后狗消失，或者停下来？
                // 方案A：直接销毁（类似子弹命中）
                // NetworkServer.Destroy(gameObject); 
                
                // 方案B：停在原地叫唤，不销毁，等时间到
                if (mover != null) mover.m_RunSpeed = 0; 
                this.enabled = false; // 停止 Update 里的移动逻辑
                
                break; // 只对第一个找到的生效
            }
        }
    }

    [ClientRpc]
    void RpcBarkEffect(Vector3 witchPos)
    {
        // 在这里播放音效
        // AudioSource.PlayClipAtPoint(barkSound, transform.position);
        
        // 可以在女巫头上显示一个感叹号 UI
        Debug.Log("Wong! Wong! Found a witch!");
    }
    
    // 画出检测范围，方便调试
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectRadius);
    }
}
```

## Hunter\HunterSkill_Dog.cs

```cs
using UnityEngine;
using Mirror;
using System.Diagnostics;

public class HunterSkill_Dog : SkillBase
{
    [Header("技能设置")]
    public GameObject dogPrefab; // 拖入刚才做好的 HunterDog
    public float spawnDistance = 1.5f; // 生成在猎人前方多少米

    protected override void OnCast()
    {
        if (dogPrefab == null) return;

        UnityEngine.Debug.Log($"<color=green>[Hunter] {ownerPlayer.playerName} used skill: Summon Dog!</color>");

        // 1. 计算生成位置：猎人面前一点点，防止卡在猎人身体里
        Vector3 spawnPos = ownerPlayer.transform.position + ownerPlayer.transform.forward * spawnDistance;

        // 2. 计算朝向：非常重要！
        // 猎人的 transform.rotation 是包含 Y 轴旋转的，直接用这个就可以
        // 这样猎人看向哪里，狗就面朝哪里
        Quaternion spawnRot = ownerPlayer.transform.rotation;

        // 3. 生成实例
        GameObject dog = Instantiate(dogPrefab, spawnPos, spawnRot);
        
        // 4. 网络生成
        NetworkServer.Spawn(dog);
    }
}
```

## Hunter\HunterSkill_Shockwave.cs

```cs
using UnityEngine;
using Mirror;
using System.Diagnostics;

public class HunterSkill_Shockwave : SkillBase
{
    public float radius = 8f;
    public GameObject vfxPrefab; // 震地特效

    protected override void OnCast()
    {
        RpcPlayVFX();

        Collider[] hits = Physics.OverlapSphere(ownerPlayer.transform.position, radius);
        Debug.Log($"<color=green>[Hunter] {ownerPlayer.playerName} used skill: Shockwave! Affected {hits.Length} targets.</color>");
        foreach (var hit in hits)
        {
            // 找到女巫
            WitchPlayer witch = hit.GetComponent<WitchPlayer>();
            if (witch == null) witch = hit.GetComponentInParent<WitchPlayer>();

            if (witch != null && !witch.isPermanentDead)
            {
                // 1. 强制显形
                if (witch.isMorphed)
                {
                    // 调用女巫现有的 Revert 命令逻辑
                    // 由于这是服务器端，我们不能调用 Cmd，需要把 CmdRevert 的逻辑拆分出一个 ServerRevert
                    // 或者我们这里简单暴力点，直接修改变量并调用 ApplyRevert
                    witch.isMorphed = false;
                    witch.morphedPropID = -1;
                    // 通过 Rpc 通知女巫客户端 (WitchPlayer 需要对应修改 OnMorphedPropIDChanged 钩子来处理逻辑)
                    // 目前代码里 OnMorphedPropIDChanged 已经处理了 ApplyRevert
                }

                // 2. 减速 (需要给 GamePlayer 加个 StatusEffect 系统，这里简化直接改速度，3秒后改回)
                StartCoroutine(SlowDownWitch(witch));
                
                // 3. 提示猎人
                TargetHitFeedback(ownerPlayer.connectionToClient);
            }
        }
    }

    [TargetRpc]
    void TargetHitFeedback(NetworkConnection conn)
    {
        // UI 显示 "Hit!"
    }

    [ClientRpc]
    void RpcPlayVFX()
    {
        if (vfxPrefab) Instantiate(vfxPrefab, ownerPlayer.transform.position, Quaternion.identity);
    }

    [Server]
    System.Collections.IEnumerator SlowDownWitch(WitchPlayer witch)
    {
        float originalSpeed = witch.moveSpeed;
        witch.moveSpeed = 2f; // 极慢
        yield return new WaitForSeconds(3f);
        witch.moveSpeed = originalSpeed;
    }
}
```

## Hunter\HunterSkill_Trap.cs

```cs
using UnityEngine;
using Mirror;
using System.Diagnostics;

public class HunterSkill_Trap : SkillBase
{
    public GameObject trapPrefab;

    protected override void OnCast()
    {
        UnityEngine.Debug.Log($"<color=green>[Hunter] {ownerPlayer.playerName} used skill: Place Trap!</color>");
        Vector3 pos = ownerPlayer.transform.position + ownerPlayer.transform.forward * 1.5f;
        // 贴地
        if (Physics.Raycast(pos + Vector3.up, Vector3.down, out RaycastHit hit, 5f))
        {
            pos = hit.point;
        }

        GameObject trap = Instantiate(trapPrefab, pos, Quaternion.identity);
        NetworkServer.Spawn(trap);
    }
}
```

## Hunter\TrapBehavior.cs

```cs
using UnityEngine;
using Mirror;

public class TrapBehavior : NetworkBehaviour
{
    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        WitchPlayer witch = other.GetComponent<WitchPlayer>() ?? other.GetComponentInParent<WitchPlayer>();
        if (witch != null)
        {
            // 触发效果：定身
            witch.ServerGetTrapped(); // 复用网枪的逻辑
            
            // 显形
            if (witch.isMorphed)
            {
                witch.isMorphed = false;
                witch.morphedPropID = -1;
            }

            // 销毁陷阱
            NetworkServer.Destroy(gameObject);
        }
    }
    
    // 只有猎人能看到 (通过 TeamVision 类似的逻辑，或者简单的 Layer 设置)
    // 这里简单处理：对所有人隐形 (除了 Debug)
    public override void OnStartClient()
    {
         GetComponentInChildren<Renderer>().enabled = false; // 完全隐形
    }
}
```

## Witch\CursedTreeTrigger.cs

```cs
using UnityEngine;
using Mirror;

public class CursedTreeTrigger : NetworkBehaviour
{
    [SyncVar] public uint casterNetId;

    // 当这棵树受到伤害时调用 (需要修改 WeaponBase 或 GunWeapon 来检测这个组件)
    [Server]
    public void OnHitByHunter(HunterPlayer hunter)
    {
        // 触发惩罚：致盲 hunter
        hunter.TargetBlindEffect(hunter.connectionToClient, 3f);
        
        // 触发后移除诅咒 (是一次性的)
        Destroy(this);
    }
}
```

## Witch\DecoyBehavior.cs

```cs
using UnityEngine;
using Mirror;

[RequireComponent(typeof(CharacterController))]
public class DecoyBehavior : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float lifeTime = 10f; // 分身存活时间
    public float moveSpeed = 5f; // 移动速度（最好和女巫走路速度一致）
    public float gravity = -9.81f; // 重力

    [Header("Sync Settings")]
    [SyncVar(hook = nameof(OnPropIDChanged))]
    public int propID = -1;

    // 内部变量
    private CharacterController cc;
    private Vector3 moveDir;
    private float verticalVelocity; // 垂直速度（处理重力）
    private float jitterTimer = 0f; // 随机转向计时器

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    public override void OnStartServer()
    {
        // 服务器端初始化
        // 初始方向：就是生成的朝向
        moveDir = transform.forward;
        
        // 销毁计时
        Destroy(gameObject, lifeTime);
    }

    [ServerCallback]
    private void Update()
    {
        if (cc == null) return;

        // 1. 处理随机转向 (模拟玩家的不规则移动)
        jitterTimer += Time.deltaTime;
        if (jitterTimer > 1.0f) // 每秒可能微调一次方向
        {
            // 随机偏转 -45 到 45 度，模拟玩家转弯
            float jitter = Random.Range(-45f, 45f);
            Quaternion turn = Quaternion.AngleAxis(jitter, Vector3.up);
            moveDir = turn * moveDir;
            jitterTimer = 0;
        }

        // 2. 处理重力
        if (cc.isGrounded && verticalVelocity < 0)
        {
            verticalVelocity = -2f; // 贴地力
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        // 3. 最终移动向量
        // 水平速度
        Vector3 finalMove = moveDir.normalized * moveSpeed;
        // 叠加垂直速度
        finalMove.y = verticalVelocity;

        // 4. 执行移动 (利用 CharacterController 的碰撞处理)
        cc.Move(finalMove * Time.deltaTime);

        // 5. 让模型朝向移动方向
        // 只取水平方向，防止分身朝向地面或天空
        Vector3 faceDir = new Vector3(moveDir.x, 0, moveDir.z);
        if (faceDir != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(faceDir), Time.deltaTime * 5f);
        }
    }

    // --- 视觉同步逻辑 (保持不变) ---
    void OnPropIDChanged(int oldID, int newID)
    {
        // 先清空旧模型 (如果有)
        foreach (Transform child in transform) {
            Destroy(child.gameObject);
        }

        if (PropDatabase.Instance != null && PropDatabase.Instance.GetPropPrefab(newID, out GameObject prefab))
        {
             GameObject visual = Instantiate(prefab, transform);
             visual.transform.localPosition = Vector3.zero;
             visual.transform.localRotation = Quaternion.identity;
             
             // 初始化 PropTarget 用于被猎人选中
             // 这里的 propID 和 visual 传进去，确保猎人射线打中分身能高亮
             var pt = GetComponent<PropTarget>();
             if (pt != null)
             {
                 pt.ManualInit(newID, visual);
             }
        }
    }
}
```

## Witch\WitchSkill_Chaos.cs

```cs
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
```

## Witch\WitchSkill_Curse.cs

```cs
using UnityEngine;
using Mirror;

public class WitchSkill_Curse : SkillBase
{
    public float range = 10f;
    public LayerMask treeLayer; // 确保树在这个 Layer

    protected override void OnCast()
    {
        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used skill: Curse! Attempting to curse a tree.</color>");
        // 射线检测
        Ray ray = new Ray(ownerPlayer.transform.position + Vector3.up, ownerPlayer.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, range, treeLayer))
        {
            PropTarget prop = hit.collider.GetComponentInParent<PropTarget>();
            // 只能诅咒普通树，不能诅咒古树
            if (prop != null && !prop.isAncientTree)
            {
                // 动态添加组件
                if (prop.gameObject.GetComponent<CursedTreeTrigger>() == null)
                {
                    var curse = prop.gameObject.AddComponent<CursedTreeTrigger>();
                    curse.casterNetId = ownerPlayer.netId;
                    // 不需要 NetworkServer.Spawn，因为这是添加组件，但要注意 Mirror 对于动态组件的支持有限
                    // 更好的做法是生成一个不可见的 Hitbox Prefab 罩住树
                    // 简易做法：利用 Rpc 通知客户端显示特效
                    RpcCurseEffect(prop.transform.position);
                }
            }
        }
    }

    [ClientRpc]
    void RpcCurseEffect(Vector3 pos)
    {
        // 播放一点紫色的粒子特效，提示女巫诅咒成功
    }
}
```

## Witch\WitchSkill_Decoy.cs

```cs
using UnityEngine;
using Mirror;


public class WitchSkill_Decoy : SkillBase
{
    public GameObject decoyPrefab; 

    protected override void OnCast()
    {
        Debug.Log($"<color=purple>[Witch] {ownerPlayer.playerName} used skill: Decoy! Summoning a decoy.</color>");
        WitchPlayer witch = ownerPlayer as WitchPlayer;
        if (witch == null) return;

        // 如果没变身，就复制人类 (或者禁止使用)
        // 这里假设复制当前的 morphedPropID
        int idToCopy = witch.isMorphed ? witch.morphedPropID : -1; // -1 表示没变身

        // 在玩家位置生成
        GameObject decoy = Instantiate(decoyPrefab, witch.transform.position, witch.transform.rotation);
        DecoyBehavior db = decoy.GetComponent<DecoyBehavior>();
        db.propID = idToCopy;

        NetworkServer.Spawn(decoy);
    }
}
```

