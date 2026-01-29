using UnityEngine;
using Mirror;

public abstract class WeaponBase : NetworkBehaviour
{
    [Header("通用设置")]
    public string weaponName;
    public float damage = 20f;       // 伤害（兜网可能没伤害，但有禁锢效果）
    public float fireRate = 1.0f;   // 射击间隔
    public Transform firePoint;     // 枪口位置（子弹/射线发出的地方）

    // 内部冷却计时
    protected float nextFireTime = 0f;

    // 判断是否冷却完毕
    public bool CanFire()
    {
        return Time.time >= nextFireTime;
    }

    // ★ 抽象方法：具体开火逻辑交给子类实现
    // origin: 射击起点（通常是摄像机位置）
    // direction: 射击方向（通常是摄像机正前方）
    public abstract void OnFire(Vector3 origin, Vector3 direction);


}
