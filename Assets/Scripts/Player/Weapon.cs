using UnityEngine;
using Mirror;

public abstract class WeaponBase : NetworkBehaviour
{
    [Header("通用设置")]
    public string weaponName;
    public float damage = 20f;       // 伤害（兜网可能没伤害，但有禁锢效果）
    public float fireRate = 1.0f;   // 射击间隔
    public Transform firePoint;     // 枪口位置（子弹/射线发出的地方）
    public ParticleSystem muzzleFlash; // 枪口火光特效
    public AudioClip fireSound;    // 开火声音

    // 内部冷却计时
    public float nextFireTime = 0f;

    // 返回冷却进度（0~1）
    public float CooldownRatio
    {
        get
        {
            float timeLeft = nextFireTime - Time.time;
            if (timeLeft <= 0) return 0f;
            return Mathf.Clamp01(timeLeft / fireRate);
        }
    }

    // 判断是否冷却完毕
    public bool CanFire()
    {
        return Time.time >= nextFireTime;
    }

    // ★ 抽象方法：具体开火逻辑交给子类实现
    // origin: 射击起点（通常是摄像机位置）
    // direction: 射击方向（通常是摄像机正前方）
    public void UpdateCooldown()
    {
        nextFireTime = Time.time + fireRate;
    }
    public abstract void OnFire(Vector3 origin, Vector3 direction);


}
