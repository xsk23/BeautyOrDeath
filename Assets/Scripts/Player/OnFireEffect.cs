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
        // 取消订阅，防止内存泄漏
        if (hunterPlayer)
            hunterPlayer.OnWeaponFired -= PlayEffects;
    }

    // 特效逻辑写在这里
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
        
        if (currentWeapon.weaponName == "HoneyGun")
        {
            // 建议音效名：honey_fire
            // 因为蜂蜜枪射速快，Play3D 会在位置生成一个临时的 AudioSource 播放
            AudioManager.Instance?.Play3D("honey_fire", transform.position);
        }

        if (currentWeapon.weaponName == "Gun")
        {
            // 开启协程，延迟 0.4 秒（根据你拉栓动画的时长调整）播放上膛音
            StartCoroutine(PlayChamberDelayed(0.2f));
        }
    }
    private IEnumerator PlayChamberDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        AudioManager.Instance?.Play3D("Chamber", transform.position);
    }
}
