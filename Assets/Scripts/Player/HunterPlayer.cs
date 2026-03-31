using Unity.VisualScripting;
using UnityEngine;
using Mirror;
using System;
using System.Collections.Generic; // 引用 List
using System.Collections;

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
    [Header("开枪偏转设置")]
    private float shootVisualAngle = 20f; // 向右偏转的角度
    private float returnSmoothTime = 0.3f; // 转回来的平滑时间
    private Quaternion originalModelRotation; // 记录模型原始旋转
    private bool hasCapturedRotation = false;
    // 【新增】单独记录当前模型是否处于偏转状态
    private bool isRotatedForShooting = false;
    [Header("连发增强设置")]
    private bool isWaitingForMultiShot = false; // 是否处于连发等待状态
    private float multiShotTimer = 0f;          // 3秒计时器
    // 【新增变量】记录是否已经完成了3秒等待，正在播放收尾动画
    private bool isFinishingSingleShot = false;
    private const float SHOOT_SINGLE_FIRE_TIME = 11f / 32f;    // 第11帧开火
    private const float SHOOT_SINGLE_PAUSE_TIME = 24f / 32f;   // 第24帧暂停并转换
    private const float SHOOT_MULTIPLE_TOTAL_TIME = 15f;       // multiple总帧数
    private float currentBaseShootSpeed = 1.0f;


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
        // 【核心修复】：如果游戏已经结束，绝对不要去动相机，否则会把相机从胜利点抓回来
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
            return;
        if (isFirstPerson)
        {
            Camera.main.transform.SetParent(transform);
            Camera.main.transform.localPosition = new Vector3(0.01f, 1.12f, 0.59f);
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
        
        // 初始锁定鼠标
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 本地玩家也执行一次初始化刷新
        RefreshWeaponVisibility(currentWeaponIndex);
        
        // 更新 UI
        if (sceneScript != null && currentWeaponIndex < hunterWeapon.Length)
        {
            var wb = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
            sceneScript.WeaponText.text = wb != null ? wb.weaponName : "None";
        }

        // 隐藏女巫 UI
        if (SceneScript.Instance != null)
        {
            if (SceneScript.Instance.itemSlot != null) SceneScript.Instance.itemSlot.gameObject.SetActive(false);
            if (SceneScript.Instance.morphSlot != null) SceneScript.Instance.morphSlot.gameObject.SetActive(false);
        }
    }
    public override void OnStartClient()
    {
        base.OnStartClient();
        // 记录出生时的位置，防止 00 第一帧计算出巨大的瞬移距离
        lastPosition = transform.position;
        // 【关键修复点】：远程玩家模型加载时，根据当前的 SyncVar 强制刷新一次武器
        // 这解决了“中途加入”或“初始状态不触发 Hook”的问题
        RefreshWeaponVisibility(currentWeaponIndex);
    }
    public override void OnStartServer()
    {
        base.OnStartServer();

        // moveSpeed = 7f;
        // mouseSensitivity = 2.5f;
        // manaRegenRate = 8f;
    }
    // 当远程玩家连接或 SyncVar 同步时执行
    public void OnWeaponChanged(int oldWeaponIndex, int newWeaponIndex)
    {
        // 核心修复：直接使用最新的 newWeaponIndex 刷新全量状态
        RefreshWeaponVisibility(newWeaponIndex);
    }

    // 抽象出一个统一的显隐控制方法
    private void RefreshWeaponVisibility(int activeIndex)
    {
        if (hunterWeapon == null || hunterWeapon.Length == 0) return;

        for (int i = 0; i < hunterWeapon.Length; i++)
        {
            if (hunterWeapon[i] == null) continue;

            // 只有索引匹配的激活，其余全部隐藏
            bool shouldBeActive = (i == activeIndex);
            hunterWeapon[i].SetActive(shouldBeActive);

            // 如果是当前激活的武器，处理相关的额外逻辑（如动画、特效清空）
            if (shouldBeActive)
            {
                var weaponBase = hunterWeapon[i].GetComponent<WeaponBase>();
                
                // 清理可能残留的粒子
                if (weaponBase != null && weaponBase.muzzleFlash != null)
                {
                    weaponBase.muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
                bool isRifleStyle = (weaponBase.weaponName == "Gun" || weaponBase.weaponName == "NetLauncher");
                hunterAnimator.SetBool("isHoldingGun", isRifleStyle);
                if (isRifleStyle)
                {
                    currentBaseShootSpeed = 1.0f / weaponBase.fireRate;
                    hunterAnimator.SetFloat("ShootSpeed", currentBaseShootSpeed);
                }
                else
                {
                    // 【核心修复】如果是拳头，立即强制 GunLayer 回到 Default
                    // 防止切到拳头时，手臂还维持着持枪姿势
                    hunterAnimator.Play("Default", 1, 0f);
                }
            }
        }
    }
    public override void Update()
    {
        base.Update();
        // 【核心修改】如果游戏结束，不处理切枪、开火和处决逻辑
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            lastAttackInputTime = -1f; // 清空可能的输入缓冲
            return;
        }
        if (isLocalPlayer)
        {
            AnimatorStateInfo stateInfo = hunterAnimator.GetCurrentAnimatorStateInfo(1);
            // --- 【核心修改：收尾状态监测】 ---
            if (isFinishingSingleShot)
            {
                // 如果已经切到了 Idle 或 Default，或者当前已经不在 shoot_ending 状态了
                if (stateInfo.IsName("Holding_Idle") || stateInfo.IsName("Default") || 
                    (!stateInfo.IsName("shoot_ending") && !stateInfo.IsName("Shoot_multiple")))
                {
                    isFinishingSingleShot = false;
                }
            }
            // --- 核心监测：Shoot_Single 运行到第 24 帧时劫持动画 ---
            // 【修改】增加 !isFinishingSingleShot 条件
            if (!isWaitingForMultiShot && !isFinishingSingleShot && stateInfo.IsName("Shoot_Single"))
            {
                // 24/32 = 0.75。只要进度超过 0.75 且没播完，立刻切换
                if (stateInfo.normalizedTime >= 0.75f && stateInfo.normalizedTime < 0.95f)
                {
                    EnterMultiShotWaitMode();
                }
            }

            // --- 连发模式下的逻辑 ---
            if (isWaitingForMultiShot)
            {
                multiShotTimer += Time.deltaTime;

                // 1. 手动开火
                if (Input.GetMouseButtonDown(0))
                {
                    // 只有当动画处于暂停（speed=0）时点击才有效，防止连点导致动作错乱
                    if (hunterAnimator.GetFloat("ShootSpeed") <= 0.01f)
                    {
                        HandleManualMultiShot();
                    }
                }

                // 2. 监测 Shoot_multiple 播完一轮
                if (stateInfo.IsName("Shoot_multiple"))
                {
                    if (stateInfo.normalizedTime >= 0.92f && hunterAnimator.GetFloat("ShootSpeed") > 0)
                    {
                        hunterAnimator.Play("Shoot_multiple", 1, 0f);
                        hunterAnimator.SetFloat("ShootSpeed", 0f);
                    }
                }

                // 3. 3秒超时回到 Single 播完结尾
                if (multiShotTimer >= 3.0f)
                {
                    ExitMultiShotWaitMode(false);
                }
            }
            // 同步动画速度：如果处于锁定中，强制发 0
            float horizontalSpeed = IsInMeleeLockout ? 0f : new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
            CmdUpdateAnimationSpeed(horizontalSpeed);
            // --- 【替换这段代码】 ---
            // 如果玩家开始移动，且当前模型是偏转的，那么仅回正模型，绝不退出连发状态
            if (horizontalSpeed > 0.1f && isRotatedForShooting)
            {
                isRotatedForShooting = false; // 清除偏转标记
                if (hasCapturedRotation)
                {
                    StopCoroutine("RotateBackRoutine");
                    StartCoroutine("RotateBackRoutine"); // 平滑转回正前方
                }
                CmdResetGunRotation(false); // 同步给其他玩家平滑回正
            }
            // ------------------------
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
                        lastAttackInputTime = -1f; 
                        currentWeapon.UpdateCooldown(); // 统一消耗冷却

                        // 【核心修改】区分猎枪和其他武器的开火时机
                        if (currentWeapon.weaponName == "Fist")
                        {
                            meleeLockEndTime = Time.time + fistAttackLockDuration;
                            CmdFireWeapon(Camera.main.transform.position, Camera.main.transform.forward);
                            OnWeaponFired?.Invoke(currentWeaponIndex);
                        }
                        // --- 修改这里：将 NetLauncher 加入 Gun 的逻辑 ---
                        else if (currentWeapon.weaponName == "Gun" || currentWeapon.weaponName == "NetLauncher")
                        {
                            // 【新增】每次按下左键开新的一枪时，重置收尾标记
                            isFinishingSingleShot = false; 
                            // 统统只触发开火动画，真正的逻辑等待第11帧事件
                            CmdTriggerGunAnimation();
                        }
                        // --------------------------------------------
                        else
                        {
                            // 这里的 else 现在通常只走没有特殊定义的武器
                            CmdFireWeapon(Camera.main.transform.position, Camera.main.transform.forward);
                            OnWeaponFired?.Invoke(currentWeaponIndex);
                        }
                    }
                }
            }

            // 处理冷却UI
            HandleCooldownUI();
            // 处决检查
            HandleExecutionCheck(Camera.main.transform.position, Camera.main.transform.forward);
            // 调试绘制（放在武器切换和开火逻辑之后）
            #if UNITY_EDITOR
            if (hunterWeapon != null && currentWeaponIndex < hunterWeapon.Length)
            {
                WeaponBase current = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
                if (current != null && current.weaponName == "Gun")
                {
                    GunWeapon gun = current as GunWeapon;
                    if (gun != null)
                    {
                        Vector3 start = Camera.main.transform.position + Camera.main.transform.forward * 1.2f; // 必须与服务器偏移一致
                        Vector3 end = start + Camera.main.transform.forward * gun.range;
                        Debug.DrawLine(start, end, Color.green, 0f); // 持续到下一帧绘制前
                    }
                }
            }
            #endif
        }
        // 2. 所有人（本地和远程）都根据同步的速度值更新 Animator
        if (hunterAnimator != null)
        {
            // 注意：截图里参数名是小写 "speed"
            hunterAnimator.SetFloat("speed", syncedSpeed, 0.05f, Time.deltaTime);
        }
    }
    [Command]
    private void CmdTriggerGunAnimation()
    {
        // 告诉所有客户端播放开枪动画
        RpcTriggerGunAnimation();
    }

    [ClientRpc]
    private void RpcTriggerGunAnimation()
    {
        if (hunterAnimator != null)
        {
            hunterAnimator.SetTrigger("Shoot");
            // --- 1. 开始转身 ---
            // 我们旋转的是包含 Animator 的模型物体，这样不会干扰摄像机和射击方向
            Transform modelTrans = hunterAnimator.transform;
            
            // 第一次执行时记录原始角度（通常是 0,0,0）
            if (!hasCapturedRotation)
            {
                originalModelRotation = modelTrans.localRotation;
                hasCapturedRotation = true;
            }

            // 瞬间偏转或极快偏转到右侧
            // --- 【核心修改】：判断是否处于静止状态 ---
            // 获取 Animator 中的 "speed" 参数，只有当速度极小（< 0.1f）时，才应用偏转
            // 获取 Animator 中的 "speed" 参数，只有当速度极小（< 0.1f）时，才应用偏转
            if (hunterAnimator.GetFloat("speed") < 0.1f)
            {
                // 瞬间偏转或极快偏转到右侧
                modelTrans.localRotation = originalModelRotation * Quaternion.Euler(0, shootVisualAngle, 0);
                
                // 【新增】：标记当前处于偏转状态
                isRotatedForShooting = true; 
            }
        }
    }
    // ----------------------------------------------------
    // 4. 【关键接口】供 AnimationEventBridge 在第 11 帧调用
    // ----------------------------------------------------
    public void ExecuteAttackEffect()
    {
        // --- 逻辑 A：所有客户端都会执行的视觉还原 ---
        // if (hunterAnimator != null && hasCapturedRotation)
        // {
        //     // 停止之前的协程（防止多次开火冲突）并平滑转回
        //     StopCoroutine("RotateBackRoutine"); 
        //     StartCoroutine("RotateBackRoutine");
        // }
        // 只有按下左键的本地玩家，才有资格在第11帧向服务器发送真实的开枪指令
        // 防止所有客户端上的第11帧都跑去让服务器开火，导致一次开出N枪
        if (isLocalPlayer)
        {
            // 【核心修复】：如果是 3 秒超时后的收尾阶段，无视 Animator 的错误补发事件！
            if (isFinishingSingleShot) return; 

            WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
            
            // 确保第11帧时，玩家手里拿的还是枪（防止动画期间切枪导致 Bug）
            if (currentWeapon != null && (currentWeapon.weaponName == "Gun" || currentWeapon.weaponName == "NetLauncher"))
            {
                Vector3 origin = Camera.main.transform.position;
                Vector3 dir = Camera.main.transform.forward;
                CmdExecuteRealGunFire(origin, dir);
                // 如果当前已经在连发模式中，重置3秒计时
                if (isWaitingForMultiShot)
                {
                    multiShotTimer = 0f;
                }
            }
        }
    }
    // 3. 进入等待模式的具体实现
    private void EnterMultiShotWaitMode()
    {
        isWaitingForMultiShot = true;
        multiShotTimer = 0f;
        // 【新增】：手动清除触发器，防止状态机“记住”了这次点击
        hunterAnimator.ResetTrigger("Shoot");
        // 强制切到连发姿态的起始并暂停
        hunterAnimator.Play("Shoot_multiple", 1, 0f);
        hunterAnimator.SetFloat("ShootSpeed", 0f); 
        
        Debug.Log("[Gun] Entered Multi-shot mode at Frame 24");
    }

    // 4. 手动连发
    private void HandleManualMultiShot()
    {
        multiShotTimer = 0f;
        hunterAnimator.SetFloat("ShootSpeed", currentBaseShootSpeed);
        hunterAnimator.Play("Shoot_multiple", 1, 0f);
    }

    // 5. 退出等待模式（恢复到 Single 播完剩下的部分）
    private void ExitMultiShotWaitMode(bool wasInterrupted)
    {
        isWaitingForMultiShot = false;
        hunterAnimator.SetFloat("ShootSpeed", currentBaseShootSpeed);
        // 【新增】：清理触发器
        hunterAnimator.ResetTrigger("Shoot");
        if (wasInterrupted)
        {
            hunterAnimator.Play("Default", 1, 0f); 
            // 【修改】：加上 && isRotatedForShooting
            if (hasCapturedRotation && isRotatedForShooting)
            {
                isRotatedForShooting = false;
                StopCoroutine("RotateBackRoutine");
                hunterAnimator.transform.localRotation = originalModelRotation;
                CmdResetGunRotation(true);
            }
        }
        else
        {
            // --- 【核心修改】 ---
            // 1. 标记为正在播放结尾
            isFinishingSingleShot = true; 
            
            // 2. 播放专门的收尾动画
            hunterAnimator.Play("shoot_ending", 1, 0f); 
            hunterAnimator.Update(0); // 强制立即切换

            // 3. 处理旋转还原
            if (hasCapturedRotation && isRotatedForShooting)
            {
                isRotatedForShooting = false;
                StopCoroutine("RotateBackRoutine");
                StartCoroutine("RotateBackRoutine");
                CmdResetGunRotation(false);
            }
        }
    }
    // ----------------------------------------------------
    // 【新增】同步模型回正的网络方法
    // ----------------------------------------------------
    [Command]
    private void CmdResetGunRotation(bool snap)
    {
        RpcResetGunRotation(snap);
    }

    [ClientRpc]
    private void RpcResetGunRotation(bool snap)
    {
        // 本地玩家已经在 ExitMultiShotWaitMode 执行过了，直接跳过防止卡顿
        if (isLocalPlayer) return; 

        if (hunterAnimator != null && hasCapturedRotation)
        {
            StopCoroutine("RotateBackRoutine");
            if (snap)
            {
                // 瞬间回正
                hunterAnimator.transform.localRotation = originalModelRotation;
            }
            else
            {
                // 平滑回正
                StartCoroutine("RotateBackRoutine");
            }
        }
    }
    // 平滑转回来的协程
    private IEnumerator RotateBackRoutine()
    {
        Transform modelTrans = hunterAnimator.transform;
        float elapsed = 0f;
        Quaternion startRot = modelTrans.localRotation;

        while (elapsed < returnSmoothTime)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / returnSmoothTime;
            // 使用 Slerp 平滑插值回到原始位置
            modelTrans.localRotation = Quaternion.Slerp(startRot, originalModelRotation, t);
            yield return null;
        }
        modelTrans.localRotation = originalModelRotation;
    }
    [Command]
    private void CmdExecuteRealGunFire(Vector3 origin, Vector3 direction)
    {
        WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        if (currentWeapon != null && (currentWeapon.weaponName == "Gun" || currentWeapon.weaponName == "NetLauncher"))
        {
            // 1. 服务器执行真正的伤害和射线检测
            currentWeapon.OnFire(origin, direction);
            
            // 2. 告诉所有客户端在此刻播放枪口特效和开火音效
            RpcFireEffect(currentWeaponIndex);
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
        WeaponBase currentWeapon = hunterWeapon[weaponIndex].GetComponent<WeaponBase>();
        // 猎枪的声音已在 RpcTriggerGunAnimation 中播放，此处跳过
        OnWeaponFired?.Invoke(weaponIndex);
        // --- 新增：触发近战动画逻辑 ---
        if (hunterAnimator != null)
        { 
            if (currentWeapon != null && currentWeapon.weaponName == "Fist") 
            {
                // --- 【核心修改：精准音效控制】 ---
                // 根据即将播放的动画方向，选择对应的音效条目
                string soundName = nextPunchIsRight ? "Punch_R" : "Punch_L";
                
                // 使用 Play3D 播放，这样其他玩家在附近也能听到
                AudioManager.Instance?.Play3D(soundName, transform.position);
                // ---------------------------------
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
        if (isWaitingForMultiShot)
        {
            // 如果正在等待时换枪，取消状态并恢复速度
            ExitMultiShotWaitMode(true);
        }
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
        bool wasBlindActive = sceneScript != null && sceneScript.blindPanel != null && sceneScript.blindPanel.activeSelf;
        if (!wasBlindActive)
        {
            AudioManager.Instance?.Play2D("致盲耳鸣音");
        }
        // 让猎人中女巫毒雾时，屏幕也产生眩晕扭曲
        if (CameraDrunkEffect.Instance != null)
        {
            // 迷雾的扭曲可以稍微猛一点 (0.1f 强度)
            CameraDrunkEffect.Instance.PlayDrunkEffect(duration, 0.1f);
        }

        //StartCoroutine(BlindRoutine(duration));
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
            // 【关键修复】跳跃前强制把模型子物体的局部旋转归零
            // 防止由于开火导致的偏转还没转回来就直接进入了跳跃动画
            hunterAnimator.transform.localRotation = Quaternion.identity; 
            // 3. 先设置随机索引，再触发 Trigger
            hunterAnimator.SetInteger("JumpIndex", index);
            hunterAnimator.SetTrigger("isJump");
            AudioManager.Instance?.Play2D("jump_sound");
            
            // 调试打印，方便你查看触发了哪一个
            // Debug.Log($"[Jump] Triggered animation index: {index}");
        }
    }
}