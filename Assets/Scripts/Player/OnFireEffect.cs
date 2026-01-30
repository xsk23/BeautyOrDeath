using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.Audio;


public class OnFireEffect : MonoBehaviour
{
    [Header("引用")]
    public HunterPlayer hunterPlayer;
    public AudioSource audioSource;

    void OnEnable()
    {
        // 订阅事件
        if (hunterPlayer)
            hunterPlayer.OnWeaponFired += PlayEffects;
    }

    void OnDisable()
    {
        // ★ 记得取消订阅，防止内存泄漏
        if (hunterPlayer)
            hunterPlayer.OnWeaponFired -= PlayEffects;
    }

    // 真正的特效逻辑写在这里
    void PlayEffects(int weaponIndex)
    {
        if (weaponIndex < 0 || weaponIndex >= hunterPlayer.hunterWeapon.Length) return;
        WeaponBase currentWeapon = hunterPlayer.hunterWeapon[weaponIndex].GetComponent<WeaponBase>();
        // 1. 枪口火光
        if (currentWeapon.muzzleFlash != null)
        {
            currentWeapon.muzzleFlash.GetComponent<ParticleSystem>().Play();
        }

        // B. 播放声音
        if (currentWeapon.fireSound != null)
        {
            // PlayOneShot 允许声音重叠，适合高射速
            audioSource.PlayOneShot(currentWeapon.fireSound);
        }

        // 4. 甚至可以加屏幕震动
        // CameraShaker.Shake(0.1f); 
    }
}
