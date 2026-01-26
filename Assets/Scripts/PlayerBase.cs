using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using System;

public enum PlayerRole
{
    Witch,
    Hunter
}

public enum TransformState
{
    Human,
    Transformed
}

/// <summary>
/// 玩家基类，为女巫和猎人提供共同的基础功能，如移动、健康系统、跳跃、技能系统等。
/// 通过虚方法和抽象能力系统，为后续扩展技能、武器、变身等功能铺垫。
/// 使用 CharacterController 实现基础移动，支持双段跳扩展。
/// 技能使用 ScriptableObject 实现，便于在 Inspector 中配置和平衡。
/// </summary>
public class PlayerBase : MonoBehaviour
{
    [Header("角色类型")]
    [SerializeField] protected PlayerRole role = PlayerRole.Witch;

    [Header("组件")]
    [SerializeField] protected CharacterController characterController;
    [SerializeField] protected AudioSource audioSource;

    [Header("移动")]
    [SerializeField] protected float walkSpeed = 5f;
    [SerializeField] protected float runSpeed = 8f;
    [SerializeField] protected float jumpHeight = 2f;
    [SerializeField] protected float gravity = -19.62f; // 较强的重力以匹配快速游戏

    [Header("空中跳跃 (女巫扫帚扩展)")]
    [SerializeField] protected int maxAirJumps = 0; // 猎人0，女巫可设1

    [Header("健康")]
    [SerializeField] protected float maxHealth = 100f;
    protected float currentHealth;

    [Header("战斗")]
    [SerializeField] protected float attackRange = 2f;
    [SerializeField] protected LayerMask enemyLayer = 1 << 6; // 假设敌人Layer=6

    [Header("技能 (在Inspector创建Ability SO资产)")]
    [SerializeField] protected Ability[] abilities;

    // 状态
    [HideInInspector] public bool isAlive = true;
    protected TransformState currentState = TransformState.Human;
    protected GameObject currentForm; // 变身时的物体实例
    protected int airJumpsLeft;
    protected Vector3 velocity;
    protected bool isGrounded;

    // 技能冷却
    protected Dictionary<Ability, float> cooldowns = new Dictionary<Ability, float>();

    // 事件
    public event Action OnDamaged;
    public event Action<float> OnHealthChanged; // 参数: 当前健康比例 (0-1)
    public event Action OnDied;
    public event Action<PlayerBase> OnPlayerKilled; // 参数: 击杀者

    protected virtual void Awake()
    {
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        currentHealth = maxHealth;
        airJumpsLeft = maxAirJumps;
    }

    protected virtual void Update()
    {
        GroundCheck();
        HandleMovementInput();
        HandleAbilityInput();
        UpdateCooldowns();
    }

    /// <summary>
    /// 基础地面检测
    /// </summary>
    protected virtual void GroundCheck()
    {
        isGrounded = characterController.isGrounded;
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
            airJumpsLeft = maxAirJumps; // 重置空中跳跃
        }
    }

    /// <summary>
    /// 处理移动输入 (WASD + Shift奔跑 + Space跳跃)
    /// 变身后可重写以实现树木缓慢移动、脚步声等
    /// </summary>
    protected virtual void HandleMovementInput()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 direction = transform.right * horizontal + transform.forward * vertical;
        float targetSpeed = Input.GetKey(KeyCode.LeftShift) ? runSpeed : walkSpeed;

        Vector3 move = direction.normalized * targetSpeed;

        velocity.x = move.x;
        velocity.z = move.z;

        // 跳跃
        if (Input.GetKeyDown(KeyCode.Space) && (isGrounded || airJumpsLeft > 0))
        {
            Jump();
        }

        // 重力
        velocity.y += gravity * Time.deltaTime;

        characterController.Move(velocity * Time.deltaTime);

        // 脚步声 (变身后重写为咚咚声)
        if (move.magnitude > 0.1f && isGrounded)
        {
            PlayFootstepSound();
        }
    }

    /// <summary>
    /// 跳跃逻辑，支持空中多段跳
    /// </summary>
    protected virtual void Jump()
    {
        velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        if (!isGrounded)
        {
            airJumpsLeft--;
        }
    }

    /// <summary>
    /// 处理技能输入 (1-0键对应abilities数组)
    /// </summary>
    protected virtual void HandleAbilityInput()
    {
        for (int i = 0; i < abilities.Length; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                TryUseAbility(i);
            }
        }
    }

    /// <summary>
    /// 尝试使用技能
    /// </summary>
    public virtual bool TryUseAbility(int index)
    {
        if (index < 0 || index >= abilities.Length) return false;
        var ability = abilities[index];
        return TryUseAbility(ability);
    }

    /// <summary>
    /// 尝试使用指定技能
    /// </summary>
    public virtual bool TryUseAbility(Ability ability)
    {
        if (cooldowns.ContainsKey(ability)) return false;
        UseAbility(ability);
        return true;
    }

    /// <summary>
    /// 使用技能
    /// </summary>
    protected virtual void UseAbility(Ability ability)
    {
        ability.Execute(this);
        if (ability.cooldown > 0)
        {
            cooldowns[ability] = ability.cooldown;
        }
    }

    /// <summary>
    /// 更新冷却
    /// </summary>
    protected virtual void UpdateCooldowns()
    {
        var toRemove = new List<Ability>();
        foreach (var kvp in cooldowns)
        {
            cooldowns[kvp.Key] -= Time.deltaTime;
            if (cooldowns[kvp.Key] <= 0)
            {
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var key in toRemove)
        {
            cooldowns.Remove(key);
        }
    }

    /// <summary>
    /// 基础攻击 (鼠标左键)，猎人重写为射击/网/拳击
    /// 女巫可重写为队友击杀逻辑
    /// </summary>
    protected virtual void Attack()
    {
        // Raycast 或 SphereCast 检测敌人
        if (Physics.Raycast(transform.position + Vector3.up, transform.forward, out RaycastHit hit, attackRange, enemyLayer))
        {
            if (hit.collider.TryGetComponent<PlayerBase>(out var enemy))
            {
                enemy.TakeDamage(20f, this);
            }
        }
    }

    /// <summary>
    /// 受到伤害
    /// </summary>
    public virtual void TakeDamage(float damage, PlayerBase attacker = null)
    {
        if (!isAlive || currentHealth <= 0) return;

        currentHealth = Mathf.Max(0, currentHealth - damage);
        OnHealthChanged?.Invoke(currentHealth / maxHealth);
        OnDamaged?.Invoke();

        if (currentHealth <= 0)
        {
            Die(attacker);
        }
    }

    /// <summary>
    /// 死亡，重写以实现女巫永久死亡debuff、猎人buff等
    /// </summary>
    protected virtual void Die(PlayerBase killer = null)
    {
        isAlive = false;
        OnDied?.Invoke();
        OnPlayerKilled?.Invoke(killer);

        // 禁用控制器，启用ragdoll等
        characterController.enabled = false;
        // Destroy(gameObject, 5f); 或 respawn逻辑
    }

    /// <summary>
    /// 变身核心方法，女巫重写实现物体附身、模型替换、移动改变
    /// 猎人返回false
    /// </summary>
    public virtual bool EnterTransform(GameObject formPrefab, Vector3 position)
    {
        if (role != PlayerRole.Witch) return false;

        currentForm = Instantiate(formPrefab, position, Quaternion.identity);
        transform.SetParent(currentForm.transform);
        transform.localPosition = Vector3.zero;
        characterController.enabled = false;
        currentState = TransformState.Transformed;

        // 重写移动以控制form的Rigidbody
        return true;
    }

    /// <summary>
    /// 退出变身
    /// </summary>
    public virtual void ExitTransform()
    {
        if (currentState != TransformState.Transformed) return;

        transform.SetParent(null);
        characterController.enabled = true;
        if (currentForm != null)
        {
            Destroy(currentForm);
        }
        currentState = TransformState.Human;
    }

    /// <summary>
    /// 播放脚步声，变身后重写为沉重咚咚声 (猎人听声辩位)
    /// </summary>
    protected virtual void PlayFootstepSound()
    {
        if (audioSource != null)
        {
            // audioSource.PlayOneShot(footstepClip);
        }
    }

    // Getter
    public PlayerRole Role => role;
    public float Health => currentHealth;
    public float MaxHealth => maxHealth;
    public TransformState State => currentState;
    public bool IsTransformed => currentState == TransformState.Transformed;
}