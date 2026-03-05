using UnityEngine;
using System.Collections;

public class BGMController : MonoBehaviour
{
    public static BGMController Instance;

    [Header("Audio Settings")]
    public AudioSource musicSource;
    public float maxVolume = 0.5f;
    public float fadeDuration = 1.5f;

    private void Awake()
    {
        // 简单的单例，方便在切换场景前调用淡出
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
        
        // 确保 AudioSource 设置正确
        if (musicSource == null) musicSource = GetComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.volume = 0;
    }

    private void Start()
    {
        // 场景开始时执行淡入
        StartCoroutine(FadeMusic(0, maxVolume, fadeDuration));
    }

    public void StartFadeOut()
    {
        // 供切换场景的按钮调用
        StopAllCoroutines();
        StartCoroutine(FadeMusic(musicSource.volume, 0, fadeDuration));
    }

    private IEnumerator FadeMusic(float startVol, float targetVol, float duration)
    {
        if (!musicSource.isPlaying) musicSource.Play();

        float timer = 0;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            musicSource.volume = Mathf.Lerp(startVol, targetVol, timer / duration);
            yield return null;
        }
        musicSource.volume = targetVol;

        if (targetVol <= 0) musicSource.Stop();
    }
}