using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class MusicManager: MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Audio Clips")]
    public AudioClip startMenuBGM;
    public AudioClip lobbyRoomBGM;

    [Header("Settings")]
    public float maxVolume = 0.5f;
    public float fadeDuration = 1.5f;

    private AudioSource sourceA;
    private AudioSource sourceB;
    private bool isSourceAActive = true;
    private Coroutine activeFadeRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 初始化两个 AudioSource 用于交叉淡化
        sourceA = gameObject.AddComponent<AudioSource>();
        sourceB = gameObject.AddComponent<AudioSource>();
        
        SetupSource(sourceA);
        SetupSource(sourceB);

        // 监听场景切换
        SceneManager.activeSceneChanged += OnSceneChanged;
    }

    private void SetupSource(AudioSource source)
    {
        source.loop = true;
        source.playOnAwake = false;
        source.volume = 0;
    }

    private void Start()
    {
        // 初始场景检查
        HandleBGMForScene(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnSceneChanged;
    }

    private void OnSceneChanged(Scene oldScene, Scene newScene)
    {
        HandleBGMForScene(newScene.name);
    }

    private void HandleBGMForScene(string sceneName)
    {
        if (sceneName == "StartMenu")
        {
            CrossFadeTo(startMenuBGM);
        }
        else if (sceneName == "LobbyRoom")
        {
            CrossFadeTo(lobbyRoomBGM);
        }
        else if (sceneName == "MyScene")
        {
            // 进入游戏场景，淡出所有音乐
            CrossFadeTo(null);
        }
    }

    public void CrossFadeTo(AudioClip newClip)
    {
        // 如果目标片段已经在播放且没在淡出，则跳过
        AudioSource activeSource = isSourceAActive ? sourceA : sourceB;
        if (activeSource.clip == newClip && newClip != null && activeSource.volume > 0) return;

        if (activeFadeRoutine != null) StopCoroutine(activeFadeRoutine);
        activeFadeRoutine = StartCoroutine(CrossFadeRoutine(newClip));
    }

    private IEnumerator CrossFadeRoutine(AudioClip newClip)
    {
        AudioSource fadeInSource = isSourceAActive ? sourceB : sourceA;
        AudioSource fadeOutSource = isSourceAActive ? sourceA : sourceB;

        if (newClip != null)
        {
            fadeInSource.clip = newClip;
            fadeInSource.Play();
        }

        float timer = 0;
        float startOutVol = fadeOutSource.volume;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float t = timer / fadeDuration;

            fadeOutSource.volume = Mathf.Lerp(startOutVol, 0, t);
            if (newClip != null)
                fadeInSource.volume = Mathf.Lerp(0, maxVolume, t);

            yield return null;
        }

        fadeOutSource.volume = 0;
        fadeOutSource.Stop();
        
        if (newClip != null) fadeInSource.volume = maxVolume;

        isSourceAActive = !isSourceAActive;
        activeFadeRoutine = null;
    }
}