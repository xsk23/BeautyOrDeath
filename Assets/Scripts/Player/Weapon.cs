using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Weapon : MonoBehaviour
{
    public Transform firePoint;//子弹发射点
    public GameObject bulletPrefab;//子弹预制体
    public float bulletSpeed;//子弹速度
    public float bulletLifetime;//子弹存活时间
    public int bulletCount;//子弹数量
    public float cooldownTime;//子弹发射间隔时间
}
