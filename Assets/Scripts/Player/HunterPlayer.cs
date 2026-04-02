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

    protected override bool CanJump()
    {
        return base.CanJump() && !IsInMeleeLockout;
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

    public override void Update()
    {
        base.Update();
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver) return;

        if (isLocalPlayer)
        {
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

    [Command] private void CmdTriggerGunAnimation() => RpcTriggerGunAnimation();

    [ClientRpc]
    private void RpcTriggerGunAnimation()
    {
        if (hunterAnimator == null) return;
        hunterAnimator.SetTrigger("Shoot");
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
    }

    private void HandleManualMultiShot()
    {
        multiShotTimer = 0f;
        hunterAnimator.SetFloat("ShootSpeed", currentBaseShootSpeed);
        hunterAnimator.Play("Shoot_multiple", 1, 0f);
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
        }
        else
        {
            isFinishingSingleShot = true;
            hunterAnimator.Play("shoot_ending", 1, 0f);
            if (isRotatedForShooting) StopShootingVisuals(false);
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