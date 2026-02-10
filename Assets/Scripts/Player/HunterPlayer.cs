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

    // 【新增】在初始化时赋值给父类的字段
    private void Awake()
    {
        goalText = "Hunt Down The Witch Until the Time Runs Out!";
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
        // 【新增】确保猎人看到的是隐藏的道具槽
        if (SceneScript.Instance != null && SceneScript.Instance.itemSlot != null)
        {
            SceneScript.Instance.itemSlot.gameObject.SetActive(false);
        }
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
            if (Input.GetMouseButtonDown(0))
            {
                WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();

                // 检查冷却
                if (currentWeapon != null && currentWeapon.CanFire())
                {
                    currentWeapon.UpdateCooldown();
                    // ★ 关键：这里只发送指令，具体逻辑多态分发
                    CmdFireWeapon(Camera.main.transform.position, Camera.main.transform.forward);
                    //触发事件
                    OnWeaponFired?.Invoke(currentWeaponIndex);
                }
            }
            // 处理冷却UI
            HandleCooldownUI();
            // 处决检查
            HandleExecutionCheck(Camera.main.transform.position, Camera.main.transform.forward);
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
        // ★ 关键细节：如果是本地玩家，刚才在 Update 里已经播过了，就别播第二次了
        if (isLocalPlayer) return;
        // 触发事件
        OnWeaponFired?.Invoke(weaponIndex);
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
}