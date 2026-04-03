using Unity.VisualScripting;
using UnityEngine;
using Mirror;
using System;
using System.Collections.Generic;
using System.Collections;

public class HunterPlayer : GamePlayer
{
    [Header("Execution Settings")]
    public float executionRange = 3.0f;
    public float executionDamage = 40f;
    public float executionRecoveryTime = 2.0f;

    private bool wasCoolingDown = false;
    public event Action<int> OnWeaponFired;

    [Header("Weapons")]
    public GameObject[] hunterWeapon;
    [SyncVar(hook = nameof(OnWeaponChanged))]
    public int currentWeaponIndex = 0;

    [Header("Animation")]
    [SerializeField] private Animator hunterAnimator;
    private Vector3 lastPosition;
    private bool nextPunchIsRight = false;

    [Header("Input Buffering")]
    public float attackBufferTime = 0.2f;
    private float lastAttackInputTime = -1f;

    [Header("Fist Melee Settings")]
    public float fistAttackLockDuration = 1f;
    private float meleeLockEndTime = 0f;
    private bool IsInMeleeLockout => Time.time < meleeLockEndTime;

    [Header("Visual Effects")]
    private float shootVisualAngle = 20f;
    private float returnSmoothTime = 0.3f;
    private Quaternion originalModelRotation;
    private bool hasCapturedRotation = false;
    private bool isRotatedForShooting = false;

    [Header("Standard Gun Multi-Shot Logic")]
    private bool isWaitingForMultiShot = false;
    private float multiShotTimer = 0f;
    private bool isFinishingSingleShot = false;
    private float currentBaseShootSpeed = 1.0f;
    [Header("FPS Weapon Aiming")]
    [Tooltip("调整此偏移量直到第一人称下枪口朝前 (通常 Y 或 Z 选一个设为 90 或 180)")]
    public Vector3 fpWeaponRotationOffset = new Vector3(-92.22f, 0, -180); 
    // 【新增】移动持枪时的目标局部坐标
    public Vector3 weaponMoveHoldPos = new Vector3(-0.116f, -0.031f, 0.142f);
    // 【新增】用于存储每把武器初始的局部旋转（相对于手部的坐标）
    private Quaternion[] originalLocalRotations;
    // 【新增】用于存储每把武器初始的局部坐标
    private Vector3[] originalLocalPositions;
    private bool rotationsCaptured = false;
    // 用于平滑位置切换的权重
    private float weaponOffsetWeight = 0f;
    [Header("Network Sync - Aiming")]
    [SyncVar] 
    private float syncedPitch; // 同步的上下仰角

    [Command(channel = 1)] // 使用不可靠信道提高频率
    private void CmdSyncPitch(float pitch)
    {
        syncedPitch = pitch;
    }
    protected override bool CanJump()
    {
        return base.CanJump() && !IsInMeleeLockout;
    }
    // 【核心逻辑：记录初始旋转】
    private void CaptureWeaponRotations()
    {
        if (rotationsCaptured || hunterWeapon == null) return;
        
        originalLocalRotations = new Quaternion[hunterWeapon.Length];
        originalLocalPositions = new Vector3[hunterWeapon.Length]; // 初始化位置数组

        for (int i = 0; i < hunterWeapon.Length; i++)
        {
            if (hunterWeapon[i] != null)
            {
                originalLocalRotations[i] = hunterWeapon[i].transform.localRotation;
                originalLocalPositions[i] = hunterWeapon[i].transform.localPosition; // 记录初始位置
            }
        }
        rotationsCaptured = true;
    }

    protected override void HandleMovementOverride(Vector2 inputOverride)
    {
        if (IsInMeleeLockout)
        {
            inputOverride = Vector2.zero;
            velocity.x = 0; velocity.z = 0;
            if (controller.isGrounded) velocity.y = -2f;
        }
        base.HandleMovementOverride(inputOverride);
    }

    public override void UpdateCameraView()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver) return;

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

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        RefreshWeaponVisibility(currentWeaponIndex);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        lastPosition = transform.position;
        // 客户端启动时记录一次初始旋转
        CaptureWeaponRotations();
        RefreshWeaponVisibility(currentWeaponIndex);
    }

    public void OnWeaponChanged(int oldIndex, int newIndex) => RefreshWeaponVisibility(newIndex);

    private void RefreshWeaponVisibility(int activeIndex)
    {
        if (hunterWeapon == null || hunterWeapon.Length == 0) return;
        for (int i = 0; i < hunterWeapon.Length; i++)
        {
            if (hunterWeapon[i] == null) continue;
            bool shouldBeActive = (i == activeIndex);
            hunterWeapon[i].SetActive(shouldBeActive);
            if (shouldBeActive)
            {
                var wb = hunterWeapon[i].GetComponent<WeaponBase>();
                bool isRifleStyle = (wb.weaponName == "Gun" || wb.weaponName == "HoneyGun");
                hunterAnimator.SetBool("isHoldingGun", isRifleStyle);
                if (isRifleStyle)
                {
                    currentBaseShootSpeed = 1.0f / wb.fireRate;
                    hunterAnimator.SetFloat("ShootSpeed", currentBaseShootSpeed);
                }
                else hunterAnimator.Play("Default", 1, 0f);
            }
        }
    }
    // ==========================================
    // 新增：用于多段射击动画强制跳转的网络同步
    // ==========================================
    [Command]
    private void CmdSyncGunAnimationState(string stateName, float shootSpeed)
    {
        RpcSyncGunAnimationState(stateName, shootSpeed);
    }

    [ClientRpc]
    private void RpcSyncGunAnimationState(string stateName, float shootSpeed)
    {
        // 如果是本地玩家自己，直接跳过（因为本地为了手感无延迟，已经提前执行过 Play 了，重播会导致抽搐）
        if (isLocalPlayer) return; 
        
        if (hunterAnimator == null) return;

        hunterAnimator.ResetTrigger("Shoot");
        hunterAnimator.SetFloat("ShootSpeed", shootSpeed);
        hunterAnimator.Play(stateName, 1, 0f);
    }
    public new void Update()
    {
        base.Update();
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver) return;

        if (isLocalPlayer)
        {
            // --- 新增：同步仰角给服务器 ---
            if (Mathf.Abs(syncedPitch - xRotation) > 0.1f) 
            {
                CmdSyncPitch(xRotation);
            }
            // 1. 处理猎枪（Gun）特有的 hijacks 逻辑
            HandleStandardGunAnimations();

            // 2. 动画速度同步
            float horizontalSpeed = IsInMeleeLockout ? 0f : new Vector3(controller.velocity.x, 0, controller.velocity.z).magnitude;
            CmdUpdateAnimationSpeed(horizontalSpeed);

            // 如果玩家移动，自动回正模型
            if (horizontalSpeed > 0.1f && isRotatedForShooting) StopShootingVisuals(false);

            // 3. 切换武器输入
            HandleWeaponSwitchInput();

            // 4. --- 攻击输入核心逻辑 ---
            WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();

            if (currentWeapon != null)
            {
                // 分支 A: 蜂蜜枪 (全自动连发)
                if (currentWeapon.weaponName == "HoneyGun")
                {
                    lastAttackInputTime = -1f; // 蜂蜜枪不使用缓冲，确保清空信号

                    if (Input.GetMouseButton(0)) // 按住左键
                    {
                        if (currentWeapon.CanFire())
                        {
                            CmdFireWeapon(Camera.main.transform.position, Camera.main.transform.forward);
                            // 开启侧身动作
                            if (!isRotatedForShooting) CmdTriggerGunAnimation();
                        }
                    }
                    else if (Input.GetMouseButtonUp(0)) // 松开按键
                    {
                        if (isRotatedForShooting) StopShootingVisuals(false);
                        // 【关键修改】松开鼠标时，向服务器请求触发结束动画信号
                        CmdTriggerHoneyGunEnd(); 
                    }
                }
                // 分支 B: 猎枪 & 拳头 (半自动 + 缓冲)
                else
                {
                    if (Input.GetMouseButtonDown(0)) lastAttackInputTime = Time.time;

                    // 检查缓冲时间
                    if (lastAttackInputTime > 0 && (Time.time - lastAttackInputTime <= attackBufferTime))
                    {
                        if (currentWeapon.CanFire())
                        {
                            bool isOnGround = controller.isGrounded || Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, 1.3f, groundLayer);

                            if (currentWeapon.weaponName == "Fist" && !isOnGround)
                            {
                                lastAttackInputTime = -1f; // 空中禁止出拳
                            }
                            else
                            {
                                // 【关键修复】执行指令前立即清空缓冲，防止无限射击
                                lastAttackInputTime = -1f;

                                if (currentWeapon.weaponName == "Gun")
                                {
                                    isFinishingSingleShot = false;
                                    CmdTriggerGunAnimation();
                                }
                                else // 拳头逻辑
                                {
                                    meleeLockEndTime = Time.time + fistAttackLockDuration;
                                    CmdFireWeapon(Camera.main.transform.position, Camera.main.transform.forward);
                                }
                            }
                        }
                    }
                    else if (lastAttackInputTime > 0 && (Time.time - lastAttackInputTime > attackBufferTime))
                    {
                        lastAttackInputTime = -1f; // 缓冲超时清理
                    }
                }
            }

            HandleCooldownUI();
            HandleExecutionCheck(Camera.main.transform.position, Camera.main.transform.forward);
        }

        // 全局同步 Animator speed
        if (hunterAnimator != null) hunterAnimator.SetFloat("speed", syncedSpeed, 0.05f, Time.deltaTime);
    }
    [Command]
    private void CmdTriggerHoneyGunEnd()
    {
        RpcTriggerHoneyGunEnd();
    }

    [ClientRpc]
    private void RpcTriggerHoneyGunEnd()
    {
        if (hunterAnimator != null)
        {
            // 1. 触发结束信号
            hunterAnimator.SetTrigger("HoneyGunEnd");

            // 2. 【核心修复】强制清理可能残留的 Shoot 触发器，防止回位后自动跳到 Shoot_Single
            hunterAnimator.ResetTrigger("Shoot");
        }
    }
    private void LateUpdate()
    {
        // 1. 基础检查
        if (hunterWeapon == null || currentWeaponIndex < 0 || currentWeaponIndex >= hunterWeapon.Length) return;
        GameObject weaponObj = hunterWeapon[currentWeaponIndex];
        if (weaponObj == null || !rotationsCaptured) return;

        WeaponBase wb = weaponObj.GetComponent<WeaponBase>();
        bool isLongGun = wb != null && (wb.weaponName == "Gun" || wb.weaponName == "HoneyGun");
        if (!isLongGun) return;

        // 2. 判定行为（这部分本地和远程都可以判定，因为 syncedSpeed 是同步的，状态机通常也是同步的）
        AnimatorStateInfo stateInfo = hunterAnimator.GetCurrentAnimatorStateInfo(1);
        bool isShooting = stateInfo.IsName("Shoot_Single") || stateInfo.IsName("Shoot_multiple") || stateInfo.IsName("shoot_ending");
        bool isMoving = syncedSpeed > 0.1f;
        
        // 只有 移动 + 开火 时才位移坐标
        bool shouldOffset = isShooting && isMoving;
        weaponOffsetWeight = Mathf.Lerp(weaponOffsetWeight, shouldOffset ? 1f : 0f, Time.deltaTime * 10f);

        // 3. 应用位置位移 (所有客户端都会为该猎人执行 Lerp)
        weaponObj.transform.localPosition = Vector3.Lerp(
            originalLocalPositions[currentWeaponIndex], 
            weaponMoveHoldPos, 
            weaponOffsetWeight
        );

        // 4. 应用旋转覆盖
        if (isShooting && (GameManager.Instance == null || GameManager.Instance.CurrentState != GameManager.GameState.GameOver))
        {
            if (isLocalPlayer)
            {
                // 本地玩家：依然使用真实 Camera 旋转，保证绝对精准
                if (Camera.main != null)
                {
                    weaponObj.transform.rotation = Camera.main.transform.rotation * Quaternion.Euler(fpWeaponRotationOffset);
                }
            }
            else
            {
                // 远程玩家：利用同步的俯仰角和自身的偏航角合成旋转
                // 我们需要模拟猎人头部的旋转方向
                Quaternion lookRotation = Quaternion.Euler(syncedPitch, transform.eulerAngles.y, 0);
                weaponObj.transform.rotation = lookRotation * Quaternion.Euler(fpWeaponRotationOffset);
            }
        }
        else
        {
            // 没在开火时，将旋转权还给骨骼动画
            weaponObj.transform.localRotation = originalLocalRotations[currentWeaponIndex];
        }
    }
    private void HandleStandardGunAnimations()
    {
        AnimatorStateInfo stateInfo = hunterAnimator.GetCurrentAnimatorStateInfo(1);
        if (isFinishingSingleShot)
        {
            if (stateInfo.IsName("Holding_Idle") || stateInfo.IsName("Default") || (!stateInfo.IsName("shoot_ending") && !stateInfo.IsName("Shoot_multiple")))
                isFinishingSingleShot = false;
        }
        if (!isWaitingForMultiShot && !isFinishingSingleShot && stateInfo.IsName("Shoot_Single"))
        {
            if (stateInfo.normalizedTime >= 0.75f && stateInfo.normalizedTime < 0.95f) EnterMultiShotWaitMode();
        }
        if (isWaitingForMultiShot)
        {
            multiShotTimer += Time.deltaTime;
            if (Input.GetMouseButtonDown(0) && hunterAnimator.GetFloat("ShootSpeed") <= 0.01f) HandleManualMultiShot();
            if (stateInfo.IsName("Shoot_multiple") && stateInfo.normalizedTime >= 0.92f && hunterAnimator.GetFloat("ShootSpeed") > 0)
            {
                hunterAnimator.Play("Shoot_multiple", 1, 0f);
                hunterAnimator.SetFloat("ShootSpeed", 0f);
                // 【新增】打完一发后再次进入等待状态，需要同步给别人
                CmdSyncGunAnimationState("Shoot_multiple", 0f); 
            }
            if (multiShotTimer >= 3.0f) ExitMultiShotWaitMode(false);
        }
    }

    private void StopShootingVisuals(bool snap)
    {
        isRotatedForShooting = false;
        if (hasCapturedRotation)
        {
            StopCoroutine("RotateBackRoutine");
            if (snap) hunterAnimator.transform.localRotation = originalModelRotation;
            else StartCoroutine("RotateBackRoutine");
        }
        CmdResetGunRotation(snap);
    }

    private void HandleWeaponSwitchInput()
    {
        int nextIndex = -1;
        if (Input.GetKeyDown(KeyCode.Alpha1)) nextIndex = 0;
        else if (Input.GetKeyDown(KeyCode.Alpha2)) nextIndex = 1;
        else if (Input.GetKeyDown(KeyCode.Alpha3)) nextIndex = 2;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll > 0f) nextIndex = (currentWeaponIndex + 1) % hunterWeapon.Length;
        else if (scroll < 0f) nextIndex = (currentWeaponIndex - 1 + hunterWeapon.Length) % hunterWeapon.Length;

        if (nextIndex != -1 && nextIndex != currentWeaponIndex) ChangeWeapon(nextIndex);
    }

    private void ChangeWeapon(int index)
    {
        // 1. 复位当前武器旋转
        ResetCurrentWeaponRotation();
        // 1. 清理射击相关状态
        if (isWaitingForMultiShot) ExitMultiShotWaitMode(true);
        if (isRotatedForShooting) StopShootingVisuals(true);

        lastAttackInputTime = -1f;
        isFinishingSingleShot = false;

        // 2. 强制复位状态机层 1，防止动作锁死
        hunterAnimator.ResetTrigger("Shoot");
        hunterAnimator.Play("Default", 1, 0f);

        // 3. 执行网络同步切换
        CmdChangeWeapon(index);
        if (sceneScript != null)
        {
            WeaponBase wb = hunterWeapon[index].GetComponent<WeaponBase>();
            sceneScript.WeaponText.text = wb != null ? wb.weaponName : "None";
        }
    }
    // 【新增：复位旋转的工具方法】
    private void ResetCurrentWeaponRotation()
    {
        if (!rotationsCaptured || hunterWeapon == null) return;
        
        GameObject weaponObj = hunterWeapon[currentWeaponIndex];
        if (weaponObj != null)
        {
            // 还原旋转
            weaponObj.transform.localRotation = originalLocalRotations[currentWeaponIndex];
            // 还原位置
            weaponObj.transform.localPosition = originalLocalPositions[currentWeaponIndex];
        }
    }
    [Command] private void CmdTriggerGunAnimation() => RpcTriggerGunAnimation();

    [ClientRpc]
    private void RpcTriggerGunAnimation()
    {
        if (hunterAnimator == null) return;
        // --- 新增：判断武器类型 ---
        WeaponBase currentWp = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        if (currentWp != null && currentWp.weaponName == "Gun")
        {
            // 只有普通猎枪才触发 Shoot (进入 Shoot_Single)
            hunterAnimator.SetTrigger("Shoot");
        }
        // 只有静止开火时才侧身
        if (hunterAnimator.GetFloat("speed") < 0.1f)
        {
            Transform modelTrans = hunterAnimator.transform;
            if (!hasCapturedRotation) { originalModelRotation = modelTrans.localRotation; hasCapturedRotation = true; }
            modelTrans.localRotation = originalModelRotation * Quaternion.Euler(0, shootVisualAngle, 0);
            isRotatedForShooting = true;
        }
    }

    // 猎枪动画事件回调
    public void ExecuteAttackEffect()
    {
        if (!isLocalPlayer || isFinishingSingleShot) return;
        WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        // 猎枪在此通过第11帧事件触发真实射击
        if (currentWeapon != null && currentWeapon.weaponName == "Gun")
        {
            CmdExecuteRealGunFire(Camera.main.transform.position, Camera.main.transform.forward);
            if (isWaitingForMultiShot) multiShotTimer = 0f;
        }
    }

    private void EnterMultiShotWaitMode()
    {
        isWaitingForMultiShot = true; multiShotTimer = 0f;
        hunterAnimator.ResetTrigger("Shoot");
        hunterAnimator.Play("Shoot_multiple", 1, 0f);
        hunterAnimator.SetFloat("ShootSpeed", 0f);
        // 【新增】告诉其他客户端：进入持枪等待状态
        CmdSyncGunAnimationState("Shoot_multiple", 0f); 
    }

    private void HandleManualMultiShot()
    {
        multiShotTimer = 0f;
        hunterAnimator.SetFloat("ShootSpeed", currentBaseShootSpeed);
        hunterAnimator.Play("Shoot_multiple", 1, 0f);
        // 【新增】告诉其他客户端：再次开火
        CmdSyncGunAnimationState("Shoot_multiple", currentBaseShootSpeed); 
    }

    private void ExitMultiShotWaitMode(bool wasInterrupted)
    {
        isWaitingForMultiShot = false;
        hunterAnimator.SetFloat("ShootSpeed", currentBaseShootSpeed);
        hunterAnimator.ResetTrigger("Shoot");
        if (wasInterrupted)
        {
            hunterAnimator.Play("Default", 1, 0f);
            if (isRotatedForShooting) StopShootingVisuals(true);
            // 【新增】被打断回默认
            CmdSyncGunAnimationState("Default", currentBaseShootSpeed);
        }
        else
        {
            isFinishingSingleShot = true;
            hunterAnimator.Play("shoot_ending", 1, 0f);
            if (isRotatedForShooting) StopShootingVisuals(false);
            // 【新增】正常收枪
            CmdSyncGunAnimationState("shoot_ending", currentBaseShootSpeed);
        }
    }

    [Command] private void CmdResetGunRotation(bool snap) => RpcResetGunRotation(snap);
    [ClientRpc]
    private void RpcResetGunRotation(bool snap)
    {
        if (isLocalPlayer || hunterAnimator == null || !hasCapturedRotation) return;
        StopCoroutine("RotateBackRoutine");
        if (snap) hunterAnimator.transform.localRotation = originalModelRotation;
        else StartCoroutine("RotateBackRoutine");
    }

    private IEnumerator RotateBackRoutine()
    {
        float elapsed = 0f;
        Quaternion startRot = hunterAnimator.transform.localRotation;
        while (elapsed < returnSmoothTime)
        {
            elapsed += Time.deltaTime;
            hunterAnimator.transform.localRotation = Quaternion.Slerp(startRot, originalModelRotation, elapsed / returnSmoothTime);
            yield return null;
        }
        hunterAnimator.transform.localRotation = originalModelRotation;
    }

    [Command]
    private void CmdExecuteRealGunFire(Vector3 origin, Vector3 direction)
    {
        WeaponBase wb = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        if (wb != null) { wb.OnFire(origin, direction); RpcFireEffect(currentWeaponIndex); }
    }

    [Command] void CmdChangeWeapon(int index) => currentWeaponIndex = index;

    [Command]
    void CmdFireWeapon(Vector3 origin, Vector3 direction)
    {
        WeaponBase wb = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        if (wb != null && wb.CanFire())
        {
            wb.UpdateCooldown();
            wb.OnFire(origin, direction);
            RpcFireEffect(currentWeaponIndex);
        }
    }

    [ClientRpc]
    void RpcFireEffect(int index)
    {
        WeaponBase wb = hunterWeapon[index].GetComponent<WeaponBase>();
        OnWeaponFired?.Invoke(index);

        if (hunterAnimator != null)
        {
            // 蜂蜜枪每次开火都重置连发动画第0帧，产生后坐力感
            if (wb.weaponName == "HoneyGun")
            {
                hunterAnimator.Play("Shoot_multiple", 1, 0f);
            }
            else if (wb.weaponName == "Fist")
            {
                string sName = nextPunchIsRight ? "Punch_R" : "Punch_L";
                AudioManager.Instance?.Play3D(sName, transform.position);
                hunterAnimator.SetBool("isPunchRight", nextPunchIsRight);
                hunterAnimator.SetTrigger("Punch");
                nextPunchIsRight = !nextPunchIsRight;
            }
        }
    }

    private void HandleCooldownUI()
    {
        if (sceneScript == null) return;
        WeaponBase wb = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        if (wb != null)
        {
            float ratio = wb.CooldownRatio;
            if (ratio > 0) { sceneScript.UpdateRevertUI(ratio, true); wasCoolingDown = true; }
            else if (wasCoolingDown) { sceneScript.UpdateRevertUI(0, false); wasCoolingDown = false; }
        }
    }

    private void HandleExecutionCheck(Vector3 origin, Vector3 direction)
    {
        if (sceneScript == null) return;
        WitchPlayer targetWitch = null;
        if (Physics.Raycast(origin + direction * 0.6f, direction, out RaycastHit hit, executionRange))
        {
            GamePlayer target = hit.collider.GetComponentInParent<GamePlayer>();
            if (target is WitchPlayer witch && witch.currentHealth > 0 && witch.isTrappedByNet) targetWitch = witch;
        }
        if (targetWitch != null)
        {
            sceneScript.ExecutionText.gameObject.SetActive(true);
            if (Input.GetKeyDown(KeyCode.F)) { CmdExecuteWitch(targetWitch.netId); sceneScript.ExecutionText.gameObject.SetActive(false); }
        }
        else sceneScript.ExecutionText.gameObject.SetActive(false);
    }

    [Command]
    private void CmdExecuteWitch(uint targetId)
    {
        if (isStunned) return;
        if (NetworkServer.spawned.TryGetValue(targetId, out NetworkIdentity id))
        {
            WitchPlayer w = id.GetComponent<WitchPlayer>();
            if (w != null && w.isTrappedByNet && Vector3.Distance(transform.position, w.transform.position) <= executionRange + 1.5f)
            {
                w.ServerGetExecuted(executionDamage);
                isStunned = true; StartCoroutine(RecoverFromExecution());
            }
        }
    }

    [Server] private IEnumerator RecoverFromExecution() { yield return new WaitForSeconds(executionRecoveryTime); isStunned = false; }

    [TargetRpc]
    public void TargetBlindEffect(NetworkConnection t, float d)
    {
        if (sceneScript?.blindPanel != null && !sceneScript.blindPanel.activeSelf) AudioManager.Instance?.Play2D("致盲耳鸣音");
        CameraDrunkEffect.Instance?.PlayDrunkEffect(d, 0.1f);
    }

    protected override void OnJumpTriggered()
    {
        if (isLocalPlayer) CmdTriggerJumpAnimation();
    }

    [Command] void CmdTriggerJumpAnimation() => RpcOnJump(UnityEngine.Random.Range(0, 2));

    [ClientRpc]
    void RpcOnJump(int i)
    {
        if (hunterAnimator != null)
        {
            hunterAnimator.transform.localRotation = Quaternion.identity;
            hunterAnimator.SetInteger("JumpIndex", i); hunterAnimator.SetTrigger("isJump");
            AudioManager.Instance?.Play2D("jump_sound");
        }
    }

    protected override void Attack() { }
}