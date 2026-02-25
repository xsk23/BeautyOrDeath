using Unity.VisualScripting;
using UnityEngine;
using Mirror;
using System;

public class HunterPlayer : GamePlayer
{
    [Header("Execution Settings")]
    public float executionRange = 3.0f; // 处决距离
    public float executionDamage = 40f; // 处决伤害
    public float executionRecoveryTime = 2.0f; // 猎人硬直时间
    // 用于冷却UI的辅助变量
    private bool wasCoolingDown = false;
    //定义事件
    public event Action<int> OnWeaponFired;
    // 猎人专用武器数组
    public GameObject[] hunterWeapon;
    // 当前武器索引（同步变量，变化时调用 OnWeaponChanged）
    [SyncVar(hook = nameof(OnWeaponChanged))]
    public int currentWeaponIndex = 0;
    [Header("Animation")]
    [SerializeField] private Animator hunterAnimator; // 在 Inspector 中拖入猎人的 Animator
    // 【新增 1】定义记录上一帧位置的变量
    private Vector3 lastPosition;
    private bool nextPunchIsRight = false; // 记录左右交替的状态
    [Header("Input Buffering")]
    public float attackBufferTime = 0.2f; // 缓冲窗口大小：冷却结束前 0.2s 内的点击有效
    private float lastAttackInputTime = -1f; // 上次尝试点击攻击的时间戳
    [Header("Fist Melee Settings")]
    public float fistAttackLockDuration = 1f; 
    private float meleeLockEndTime = 0f; // 记录锁定结束的具体时间点
    // 定义一个快捷属性判断是否处于锁定状态
    private bool IsInMeleeLockout => Time.time < meleeLockEndTime;
    // 【新增】重写父类的起跳许可，出拳硬直期间禁止起跳
    protected override bool CanJump()
    {
        // 必须满足父类的条件（没被禁锢），且当前没有处于出拳硬直状态
        return base.CanJump() && !IsInMeleeLockout;
    }
    // 【新增】在初始化时赋值给父类的字段
    private void Awake()
    {
        goalText = "Hunt Down The Witch Until the Time Runs Out!";
    }
    // 1. 重写移动逻辑，在硬直期间强制输入为 0
    protected override void HandleMovementOverride(Vector2 inputOverride)
    {
        if (IsInMeleeLockout)
        {
            inputOverride = Vector2.zero;
            velocity.x = 0;
            velocity.z = 0;
            if (controller.isGrounded) velocity.y = -2f;
        }
        base.HandleMovementOverride(inputOverride);
    }
    public override void UpdateCameraView()
    {
        if (isFirstPerson)
        {
            Camera.main.transform.SetParent(transform);
            Camera.main.transform.localPosition = new Vector3(0, 1.31f, 0.304f);
            Camera.main.transform.localRotation = Quaternion.identity;  
        }
        else
        {
            Camera.main.transform.SetParent(transform);
            Camera.main.transform.localPosition = new Vector3(0, 3.09f, -3.74f);
            Camera.main.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        }
    }

    // 重写基类的抽象方法
    protected override void Attack()
    {
        // 这里是服务器端运行的代码
        //改成英文debug
        // Debug.Log($"<color=green>【猎人】{playerName} 释放了技能：开枪射击！</color>");
        Debug.Log($"<color=green>[Hunter] {playerName} used skill: Shoot Gun!</color>");
        // 在这里写具体的射线检测逻辑...
        // if (Physics.Raycast(...)) { ... }
    }
    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        ChangeWeapon(currentWeaponIndex);
    // 确保本地猎人看到的是隐藏的女巫相关 UI
        if (SceneScript.Instance != null)
        {
            // 1. 隐藏女巫的 F 键道具槽（你原本已有的逻辑）
            if (SceneScript.Instance.itemSlot != null)
            {
                SceneScript.Instance.itemSlot.gameObject.SetActive(false);
            }

            // 2. 【新增】隐藏女巫的变身技能槽
            if (SceneScript.Instance.morphSlot != null)
            {
                SceneScript.Instance.morphSlot.gameObject.SetActive(false);
            }
        }
    }
    public override void OnStartClient()
    {
        base.OnStartClient();
        // 记录出生时的位置，防止 00 第一帧计算出巨大的瞬移距离
        lastPosition = transform.position;
    }
    public override void OnStartServer()
    {
        base.OnStartServer();

        // moveSpeed = 7f;
        // mouseSensitivity = 2.5f;
        // manaRegenRate = 8f;
    }
    public void OnWeaponChanged(int oldWeaponIndex, int newWeaponIndex)
    {
        if (oldWeaponIndex >= 0 && oldWeaponIndex < hunterWeapon.Length)
        {
            hunterWeapon[oldWeaponIndex].SetActive(false);
        }
        if (newWeaponIndex >= 0 && newWeaponIndex < hunterWeapon.Length)
        {
            hunterWeapon[newWeaponIndex].SetActive(true);
            // 【新增】防止切枪时如果粒子正在播放卡在半空中，强制停止
            var weaponBase = hunterWeapon[newWeaponIndex].GetComponent<WeaponBase>();
            if (weaponBase != null && weaponBase.muzzleFlash != null)
            {
                weaponBase.muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
    public override void Update()
    {
        base.Update();
        if (isLocalPlayer)
        {
            // 同步动画速度：如果处于锁定中，强制发 0
            float horizontalSpeed = IsInMeleeLockout ? 0f : new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
            CmdUpdateAnimationSpeed(horizontalSpeed);
            // 切换武器
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                ChangeWeapon(0);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                ChangeWeapon(1);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                ChangeWeapon(2);
            }
            if (Input.GetAxis("Mouse ScrollWheel") > 0f)
            {
                int nextIndex = (currentWeaponIndex + 1) % hunterWeapon.Length;
                ChangeWeapon(nextIndex);

            }
            else if (Input.GetAxis("Mouse ScrollWheel") < 0f)
            {
                int nextIndex = (currentWeaponIndex - 1 + hunterWeapon.Length) % hunterWeapon.Length;
                ChangeWeapon(nextIndex);
            }
            // 开火
            // 1. 记录玩家的点击意图
            if (Input.GetMouseButtonDown(0))
            {
                lastAttackInputTime = Time.time;
            }

            // 2. 检测是否有“存着”的指令需要触发
            if (Time.time - lastAttackInputTime <= attackBufferTime)
            {
                WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();

                if (currentWeapon != null && currentWeapon.CanFire())
                {
                    // --- 【新增】：判断是否处于地面（结合原生判断与射线容错，防止下坡误判） ---
                    bool isOnGround = controller.isGrounded || Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, (controller.height * 0.5f) + 0.3f, groundLayer);
                    // 如果武器是拳头且在空中，则拦截攻击
                    if (currentWeapon.weaponName == "Fist" && !isOnGround)
                    {
                        // 仅消耗掉输入缓冲，不执行射击指令（禁止空中出拳）
                        lastAttackInputTime = -1f;
                    }
                    else
                    {
                        // 冷却刚好，立即执行！
                        lastAttackInputTime = -1f; // 消耗掉这个缓冲，防止重复触发
                        // --- 【核心修改】 ---
                        if (currentWeapon.weaponName == "Fist")
                        {
                            // 每次攻击都刷新“结束时间”，确保连招期间全程原地不动
                            meleeLockEndTime = Time.time + fistAttackLockDuration;
                        }
                        // ---------------------
                        currentWeapon.UpdateCooldown();
                        CmdFireWeapon(Camera.main.transform.position, Camera.main.transform.forward);
                        OnWeaponFired?.Invoke(currentWeaponIndex);
                    }
                }
            }

            // 处理冷却UI
            HandleCooldownUI();
            // 处决检查
            HandleExecutionCheck(Camera.main.transform.position, Camera.main.transform.forward);
        }
        // 2. 所有人（本地和远程）都根据同步的速度值更新 Animator
        if (hunterAnimator != null)
        {
            // 注意：截图里参数名是小写 "speed"
            hunterAnimator.SetFloat("speed", syncedSpeed, 0.05f, Time.deltaTime);
        }
    }

    [Command]
    void CmdChangeWeapon(int weaponIndex)
    {
        if (weaponIndex >= 0 && weaponIndex < hunterWeapon.Length)
        {
            currentWeaponIndex = weaponIndex;
        }
    }
    [Command]
    void CmdFireWeapon(Vector3 origin, Vector3 direction)
    {
        WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        if (currentWeapon != null && currentWeapon.CanFire())
        {
            // 服务器更新冷却
            currentWeapon.UpdateCooldown();
            // 多态分发具体开火逻辑
            currentWeapon.OnFire(origin, direction);
            // 3. 告诉所有客户端同步特效
            RpcFireEffect(currentWeaponIndex);
        }
    }
    [ClientRpc]
    void RpcFireEffect(int weaponIndex)
    {
        // // ★ 关键细节：如果是本地玩家，刚才在 Update 里已经播过了，就别播第二次了
        // if (isLocalPlayer) return;
        // 触发事件
        OnWeaponFired?.Invoke(weaponIndex);
        // --- 新增：触发近战动画逻辑 ---
        if (hunterAnimator != null)
        {
            WeaponBase currentWeapon = hunterWeapon[weaponIndex].GetComponent<WeaponBase>();
            
            if (currentWeapon != null && currentWeapon.weaponName == "Fist") 
            {
                // 1. 设置布尔值，决定这次走左边还是右边的动画分支
                hunterAnimator.SetBool("isPunchRight", nextPunchIsRight);

                // 2. 触发攻击 Trigger0
                hunterAnimator.SetTrigger("Punch");

                // 3. 切换状态：下次攻击换另一只手
                nextPunchIsRight = !nextPunchIsRight;
                
                UnityEngine.Debug.Log($"[Animation] Punching side: {(nextPunchIsRight ? "Left" : "Right")}");
            }
        }
    }

    private void HandleCooldownUI()
    {
        if (sceneScript == null || hunterWeapon.Length == 0) return;

        // 获取当前武器脚本
        WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();

        if (currentWeapon != null)
        {
            // 利用我们在 WeaponBase 做的修改获取冷却比例
            float ratio = currentWeapon.CooldownRatio;

            if (ratio > 0)
            {
                // 正在冷却中：显示 UI
                // ratio 从 1 变到 0，代表类似“倒计时”的效果
                // 颜色设为半透明青色 (Color.cyan) 或者 灰色 (Color.gray)
                sceneScript.UpdateRevertUI(ratio, true);
                wasCoolingDown = true;
            }
            else
            {
                // 冷却结束：隐藏 UI
                if (wasCoolingDown)
                {
                    // 只有刚结束的那一帧调用一次隐藏，避免每帧都调用
                    sceneScript.UpdateRevertUI(0, false);
                    wasCoolingDown = false;
                }
            }
        }
    }

    private void ChangeWeapon(int weaponIndex)
    {
        CmdChangeWeapon(weaponIndex);
        if (sceneScript == null) return;

        string weaponName = "None";
        if (weaponIndex >= 0 && weaponIndex < hunterWeapon.Length)
        {
            WeaponBase weaponBase = hunterWeapon[weaponIndex].GetComponent<WeaponBase>();
            if (weaponBase != null)
            {
                weaponName = weaponBase.weaponName;
            }
        }
        sceneScript.WeaponText.text = weaponName;
    }
    private void HandleExecutionCheck(Vector3 origin, Vector3 direction)
    {
        if (sceneScript == null) return;
        WitchPlayer targetWitch = null;
        Vector3 startPos = origin + direction * 0.6f;
        if (Physics.Raycast(startPos, direction, out RaycastHit hit, executionRange))
        {
            GamePlayer target = hit.collider.GetComponent<GamePlayer>();
            if (target is WitchPlayer witch)
            {
                if (witch.currentHealth > 0 && witch.isTrappedByNet)
                {
                    targetWitch = witch;
                }
            }
        }
        // UI 显示与输入处理
        if (targetWitch != null)
        {
            sceneScript.ExecutionText.gameObject.SetActive(true);

            if (Input.GetKeyDown(KeyCode.F))
            {
                // 发送处决命令
                CmdExecuteWitch(targetWitch.netId);
                // 此时本地立刻隐藏文字
                sceneScript.ExecutionText.gameObject.SetActive(false);
            }
        }
        else
        {
            sceneScript.ExecutionText.gameObject.SetActive(false);
        }
    }

    [Command]
    private void CmdExecuteWitch(uint targetNetId)
    {
        // 1. 校验：不能在硬直期间再次处决
        if (isStunned) return;

        // 2. 获取目标对象
        if (NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity identity))
        {
            WitchPlayer witch = identity.GetComponent<WitchPlayer>();

            if (witch != null && witch.isTrappedByNet)
            {
                float dist = Vector3.Distance(transform.position, witch.transform.position);
                // 允许一点点网络延迟导致的距离误差 (比如 range + 1.0f)
                if (dist <= executionRange + 1.5f)
                {
                    // A. 女巫扣血并释放
                    witch.ServerGetExecuted(executionDamage);

                    // B. 猎人进入硬直
                    isStunned = true;

                    // C. 开启协程或计时器，2秒后恢复
                    StartCoroutine(RecoverFromExecution());

                    Debug.Log($"{playerName} Executed {witch.playerName}!");
                }
            }
        }
    }

    // 服务器端恢复协程
    [Server]
    private System.Collections.IEnumerator RecoverFromExecution()
    {
        yield return new WaitForSeconds(executionRecoveryTime);
        isStunned = false;
    }
    // 致盲效果的 TargetRpc 方法
    [TargetRpc]
    public void TargetBlindEffect(NetworkConnection target, float duration)
    {
        StartCoroutine(BlindRoutine(duration));
        Debug.Log($"[Hunter] {playerName} is Blinded for {duration} seconds.");
    }

    private System.Collections.IEnumerator BlindRoutine(float duration)
    {
        // 假设 SceneScript 里有个全黑的 Image 叫 BlindPanel
        if (sceneScript != null && sceneScript.blindPanel != null)
        {
            sceneScript.blindPanel.SetActive(true);
            yield return new WaitForSeconds(duration);
            sceneScript.blindPanel.SetActive(false);
        }
    }
    // ----------------------------------------------------
    // 跳跃动画触发
    // ----------------------------------------------------

    // 重写基类的跳跃钩子
    protected override void OnJumpTriggered()
    {
        // 增加严格的落地判断：只有 CharacterController 认为在地面上，才发送跳跃指令
        if (isLocalPlayer)
        {
            CmdTriggerJumpAnimation();
        }
    }

    [Command]
    void CmdTriggerJumpAnimation()
    {
        // 1. 在服务器端生成随机数 (0 或 1)
        // 使用 Random.Range(0, 2) 会得到 0 或 1
        int randomIndex = UnityEngine.Random.Range(0, 2);

        // 2. 将随机索引传给 Rpc
        RpcOnJump(randomIndex);
    }

    [ClientRpc]
    void RpcOnJump(int index)
    {
        if (hunterAnimator != null)
        {
            // 3. 先设置随机索引，再触发 Trigger
            hunterAnimator.SetInteger("JumpIndex", index);
            hunterAnimator.SetTrigger("isJump");
            
            // 调试打印，方便你查看触发了哪一个
            // Debug.Log($"[Jump] Triggered animation index: {index}");
        }
    }
}