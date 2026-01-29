using Unity.VisualScripting;
using UnityEngine;
using Mirror;

public class HunterPlayer : GamePlayer
{
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
    public override void OnStartServer()
    {
        base.OnStartServer();

        moveSpeed = 7f;
        // mouseSensitivity = 2.5f;
        manaRegenRate = 8f;
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
        }
    }
    public override void Update()
    {
        base.Update();
        if (isLocalPlayer)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                CmdChangeWeapon(0);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                CmdChangeWeapon(1);
            }
            if (Input.GetAxis("Mouse ScrollWheel") > 0f)
            {
                int nextIndex = (currentWeaponIndex + 1) % hunterWeapon.Length;
                CmdChangeWeapon(nextIndex);

            }
            else if (Input.GetAxis("Mouse ScrollWheel") < 0f)
            {
                int nextIndex = (currentWeaponIndex - 1 + hunterWeapon.Length) % hunterWeapon.Length;
                CmdChangeWeapon(nextIndex);
            }
            if (Input.GetMouseButtonDown(0))
            {
                WeaponBase currentWeapon = hunterWeapon[currentWeaponIndex].GetComponent<WeaponBase>();

                // 检查冷却
                if (currentWeapon != null && currentWeapon.CanFire())
                {
                    // ★ 关键：这里只发送指令，具体逻辑多态分发
                    CmdFireWeapon(Camera.main.transform.position, Camera.main.transform.forward);
                }
            }
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
            currentWeapon.OnFire(origin, direction);
        }
    }
}