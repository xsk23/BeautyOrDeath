using UnityEngine;
using Mirror;

public class NetLauncherWeapon : WeaponBase
{
  [Header("兜网设置")]
  public GameObject netPrefab; // 拖入上面的兜网 Prefab
  public float BulletSpeed = 20f; // 网的飞行速度
  public float lifeTime = 5f; // 网的存在时间

  public override void OnFire(Vector3 origin, Vector3 direction)
  {
    // 冷却
    nextFireTime = Time.time + fireRate;

    //服务器生成实体
    if (isServer)
    {
      // 在枪口位置生成网
      // 注意：虽然射击方向是 direction (摄像机朝向)，但为了让网从枪口飞出，我们放在 firePoint
      GameObject net = Instantiate(netPrefab, firePoint.position, Quaternion.LookRotation(direction));
      net.GetComponent<Rigidbody>().velocity = direction * BulletSpeed;
      // 【新增】获取发射者的阵营并传给网子
      PlayerRole shooterRole = GetComponentInParent<GamePlayer>().playerRole;
      NetworkServer.Spawn(net);
      // 设置网的生命周期
      Destroy(net, lifeTime);
    }
  }
}