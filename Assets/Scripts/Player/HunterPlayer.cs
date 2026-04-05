using Unity.VisualScripting;
using UnityEngine;
using Mirror;
using System;
using System.Collections.Generic;
using System.Collections;

public class HunterPlayer : GamePlayer
{
    [Header("Honey Supply Settings")]
    public float refillTime = 2.0f;
    public float maxRefillDistance = 2.5f; // 【关键修复】限制补给距离
    private float refillTimer = 0f;
    private HoneySupplyStation currentFocusStation; // 【新增】当前视线聚焦的蜂蜜罐
    public LayerMask supplyLayer;
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
    private float rotationLerpWeight = 0f; // 0 为完全跟随动画，1 为完全对准准星
    [Tooltip("旋转平滑速度")]
    public float rotationSmoothSpeed = 15f;
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

    public void OnWeaponChanged(int oldIndex, int newIndex)
    {
        // 1. 刷新手里的武器模型
        RefreshWeaponVisibility(newIndex);

        // 2. 【核心修复】当且仅当网络同步确认、模型切换完毕时，再更新UI文字，防止被旧的 Update 覆盖
        if (isLocalPlayer && sceneScript != null && sceneScript.WeaponText != null)
        {
            WeaponBase wb = hunterWeapon[newIndex].GetComponent<WeaponBase>();
            sceneScript.WeaponText.text = wb != null ? wb.weaponName : "None";
        }
    }
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
            UpdateHoneyGunUI();
            HandleHoneyRefill();
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
                                    // 猎枪开火判定
                                    if (isWaitingForMultiShot)
                                    {
                                        // 已经在等待状态下，直接重播动作无需等待 Trigger
                                        HandleManualMultiShot();
                                    }
                                    else
                                    {
                                        // 【关键修复 1】：既然能走到这里，说明是新的一轮射击，强制解除收枪拦截状态！
                                        isFinishingSingleShot = false;

                                        // 首次开火，走标准 Trigger 进入 Shoot_multiple
                                        CmdTriggerGunAnimation();
                                    }
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
    private void UpdateHoneyGunUI()
    {
        if (sceneScript == null || sceneScript.WeaponText == null) return;
        WeaponBase wb = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        if (wb != null && wb.weaponName == "HoneyGun")
        {
            HoneyWeapon hw = (HoneyWeapon)wb;
            // 实时显示弹药量
            sceneScript.WeaponText.text = $"HoneyGun <color=yellow>[{hw.currentAmmo}/{hw.maxAmmo}]</color>";
        }
    }

    private void HandleHoneyRefill()
    {
        WeaponBase wb = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        // 如果手里拿的不是蜂蜜枪，清除高亮并返回
        if (wb == null || wb.weaponName != "HoneyGun")
        {
            ClearStationFocus();
            ResetRefill();
            return;
        }

        HoneyWeapon hw = (HoneyWeapon)wb;
        // 如果子弹是满的，清除高亮并返回
        if (hw.currentAmmo >= hw.maxAmmo)
        {
            ClearStationFocus();
            ResetRefill();
            return;
        }

        // 1. 发射射线检测蜂蜜罐
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
        HoneySupplyStation hitStation = null;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRefillDistance, supplyLayer))
        {
            float actualDist = Vector3.Distance(transform.position, hit.point);
            if (hit.collider.CompareTag("HoneySupply") && actualDist <= maxRefillDistance)
            {
                // 获取蜂蜜罐专有脚本
                hitStation = hit.collider.GetComponentInParent<HoneySupplyStation>();

                // 如果罐子已经是空的了，当作没看见（不准交互也不高亮）
                if (hitStation != null && hitStation.isEmpty)
                {
                    hitStation = null;
                }
            }
        }

        // 2. 状态切换逻辑：处理高亮的开启与关闭
        if (hitStation != currentFocusStation)
        {
            // 取消旧物体的高亮
            if (currentFocusStation != null)
            {
                currentFocusStation.SetHighlight(false);
            }

            // 赋值新物体
            currentFocusStation = hitStation;

            // 开启新物体的高亮
            if (currentFocusStation != null)
            {
                currentFocusStation.SetHighlight(true);
            }
        }

        // 3. 处理按键补给逻辑
        if (currentFocusStation != null)
        {
            if (Input.GetMouseButton(1)) // 按住右键
            {
                refillTimer += Time.deltaTime;
                float progress = Mathf.Clamp01(refillTimer / refillTime);
                if (sceneScript != null) sceneScript.UpdateRevertUI(progress, true);

                if (refillTimer >= refillTime)
                {
                    // 【核心修改】将准星对准的具体罐子ID发给服务器
                    CmdRefillHoneyGun(currentFocusStation.netId);
                    refillTimer = 0;
                    AudioManager.Instance?.Play2D("UI点击（木头）");

                    // 吸完之后，因为罐子变空了，立刻清除本地的高亮焦点
                    ClearStationFocus();
                }
                return;
            }
        }

        // 如果松开右键、没指着满罐子、或者走远了，重置进度条
        if (Input.GetMouseButtonUp(1) || refillTimer > 0)
        {
            ResetRefill();
        }
    }

    // 【新增】辅助方法：清除当前视线焦点和高亮
    private void ClearStationFocus()
    {
        if (currentFocusStation != null)
        {
            currentFocusStation.SetHighlight(false);
            currentFocusStation = null;
        }
    }
    private void ResetRefill()
    {
        refillTimer = 0;
        if (sceneScript != null) sceneScript.UpdateRevertUI(0, false);
    }

    [Command]
    private void CmdRefillHoneyGun(uint stationNetId)
    {
        WeaponBase wb = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();
        if (wb != null && wb.weaponName == "HoneyGun")
        {
            // 在服务器上找到客户端请求的那个蜂蜜罐
            if (NetworkServer.spawned.TryGetValue(stationNetId, out NetworkIdentity stationIdentity))
            {
                HoneySupplyStation station = stationIdentity.GetComponent<HoneySupplyStation>();

                // 确保罐子存在且没有被别人抢先吸干
                if (station != null && !station.isEmpty)
                {
                    // 距离二次校验（服务器端防止作弊）
                    float dist = Vector3.Distance(transform.position, station.transform.position);
                    if (dist <= maxRefillDistance + 1.5f) // 留一点网络延迟的容差
                    {
                        ((HoneyWeapon)wb).ServerRefill();

                        // 【核心修改】让蜂蜜罐执行消耗逻辑（变空并开启恢复倒计时）
                        station.ServerConsume();

                        Debug.Log($"[Server] {playerName} refilled at pot {stationNetId}.");
                    }
                }
            }
        }
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
        // bool isShootingRotate = stateInfo.IsName("Shoot_Single") || stateInfo.IsName("Shoot_multiple");
        bool isShooting = stateInfo.IsName("Shoot_multiple");
        bool isMoving = syncedSpeed > 0.1f;

        // 只有 移动 + 开火 时才位移坐标
        // bool shouldOffset = isShooting && isMoving;
        bool shouldOffset = isShooting;
        weaponOffsetWeight = Mathf.Lerp(weaponOffsetWeight, shouldOffset ? 1f : 0f, Time.deltaTime * 30f);

        // 3. 应用位置位移 (所有客户端都会为该猎人执行 Lerp)
        weaponObj.transform.localPosition = Vector3.Lerp(
            originalLocalPositions[currentWeaponIndex],
            weaponMoveHoldPos,
            weaponOffsetWeight
        );

        // 1. 确定目标权重：正在射击或准备射击时为 1，否则为 0
        float targetWeight = (isShooting && (GameManager.Instance == null || GameManager.Instance.CurrentState != GameManager.GameState.GameOver)) ? 1f : 0f;

        // 2. 平滑更新权重
        rotationLerpWeight = Mathf.Lerp(rotationLerpWeight, targetWeight, Time.deltaTime * rotationSmoothSpeed);

        if (rotationLerpWeight > 0.001f)
        {
            // 计算目标瞄准旋转（世界空间）
            Quaternion targetWorldRotation;
            if (isLocalPlayer)
            {
                targetWorldRotation = Camera.main.transform.rotation * Quaternion.Euler(fpWeaponRotationOffset);
            }
            else
            {
                targetWorldRotation = Quaternion.Euler(syncedPitch, transform.eulerAngles.y, 0) * Quaternion.Euler(fpWeaponRotationOffset);
            }

            // 获取默认的局部旋转（从数组中取）并转为世界空间
            // 或者直接用 weaponObj.transform.parent.rotation * originalLocalRotations[...]
            Quaternion defaultWorldRotation = weaponObj.transform.parent.rotation * originalLocalRotations[currentWeaponIndex];

            // 使用 Slerp 在“默认姿态”和“瞄准姿态”之间插值
            // 注意：这里我们直接操作 transform.rotation 以保证对准精准
            weaponObj.transform.rotation = Quaternion.Slerp(defaultWorldRotation, targetWorldRotation, rotationLerpWeight);
        }
        else
        {
            // 权重极低时，完全回归动画控制
            weaponObj.transform.localRotation = originalLocalRotations[currentWeaponIndex];
        }
    }
    // ==========================================
    // 新版 3秒等待收枪逻辑
    // ==========================================
    private void HandleStandardGunAnimations()
    {
        AnimatorStateInfo stateInfo = hunterAnimator.GetCurrentAnimatorStateInfo(1);

        // 1. 清理收枪状态：只要回到默认或保持，就标志结束完毕
        if (isFinishingSingleShot)
        {
            if (stateInfo.IsName("Holding_Idle") || stateInfo.IsName("Default"))
                isFinishingSingleShot = false;
        }

        // 2. 捕捉开火动画的末尾：当 Shoot_multiple 播放到最后时，冻结动画并进入等待
        if (!isWaitingForMultiShot && !isFinishingSingleShot && stateInfo.IsName("Shoot_multiple"))
        {
            // 注意：如果你的动画师设定里设置了退出时间(Exit Time)，这个判定最好在其之前触发
            if (stateInfo.normalizedTime >= 0.90f && hunterAnimator.GetFloat("ShootSpeed") > 0.01f)
            {
                EnterMultiShotWaitMode();
            }
        }

        // 3. 处理 3 秒等待倒计时
        if (isWaitingForMultiShot)
        {
            multiShotTimer += Time.deltaTime;

            // 3秒未开枪 -> 强制进入 shoot_ending
            if (multiShotTimer >= 3.0f)
            {
                ExitMultiShotWaitMode(false);
            }
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
        }
    }

    private void EnterMultiShotWaitMode()
    {
        isWaitingForMultiShot = true;
        multiShotTimer = 0f;
        hunterAnimator.ResetTrigger("Shoot"); // 清空残留Trigger
        hunterAnimator.SetFloat("ShootSpeed", 0f); // 冻结动画，停留在举枪姿势
        CmdSyncGunAnimationState("Shoot_multiple", 0f);
    }
    private void HandleManualMultiShot()
    {
        // 确保连发状态下也不会被错误的收枪状态拦截
        isFinishingSingleShot = false;
        multiShotTimer = 0f; // 重置计时器
        hunterAnimator.SetFloat("ShootSpeed", currentBaseShootSpeed); // 恢复速度
        hunterAnimator.Play("Shoot_multiple", 1, 0f); // 从第0帧重播开火
        CmdSyncGunAnimationState("Shoot_multiple", currentBaseShootSpeed);
    }

    private void ExitMultiShotWaitMode(bool wasInterrupted)
    {
        isWaitingForMultiShot = false;
        hunterAnimator.SetFloat("ShootSpeed", currentBaseShootSpeed); // 恢复速度
        hunterAnimator.ResetTrigger("Shoot");

        if (wasInterrupted)
        {
            // 如果是因为跑动等被打断，直接回默认
            hunterAnimator.Play("Default", 1, 0f);
            if (isRotatedForShooting) StopShootingVisuals(true);
            CmdSyncGunAnimationState("Default", currentBaseShootSpeed);
        }
        else
        {
            // 正常超时收枪：播放 shoot_ending，之后 Animator 自动连线到 Holding_Idle
            isFinishingSingleShot = true;
            hunterAnimator.Play("shoot_ending", 1, 0f);
            if (isRotatedForShooting) StopShootingVisuals(false); // 平滑回正视角
            CmdSyncGunAnimationState("shoot_ending", currentBaseShootSpeed);
        }
    }
    // ==========================================

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