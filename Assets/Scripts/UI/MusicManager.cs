using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [System.Serializable]
    public struct MusicGroup
    {
        public string groupName;
        [Header("Menu & Lobby")]
        public AudioClip startMenuBGM;
        public AudioClip lobbyRoomBGM;
        [Header("In Game")]
        public AudioClip inGameNormalBGM;
        public AudioClip inGameFastBGM;
    }

    public enum SceneZone { None, Menu, Lobby, Game }

    [Header("BGM Sets (成套配对)")]
    public MusicGroup[] musicGroups;

    [Header("Settings")]
    public float maxVolume = 0.5f;
    public float fadeDuration = 1.5f;
    public float fastModeThreshold = 60f; // 剩余60秒切换

    private AudioSource sourceA;
    private AudioSource sourceB;
    private bool isSourceAActive = true;
    private Coroutine activeFadeRoutine;

    private int currentGroupIndex = -1;
    private SceneZone currentZone = SceneZone.None;
    private bool isFastModeActive = false; // 标记是否已经切到了快节奏音乐
    private bool hasHandledGameOverMusic = false; // 新增变量

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        sourceA = gameObject.AddComponent<AudioSource>();
        sourceB = gameObject.AddComponent<AudioSource>();
        SetupSource(sourceA);
        SetupSource(sourceB);

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

    private void Update()
    {
        // 只有在游戏对局中才需要检测时间切换 BGM
        if (currentZone == SceneZone.Game)
        {
            CheckGameTimer();
            CheckGameOver();
        }
    }

    private void HandleBGMForScene(string sceneName)
    {
        if (musicGroups == null || musicGroups.Length == 0) return;

        SceneZone lastZone = currentZone;
        currentZone = GetZoneForScene(sceneName);

        // 如果是从游戏区回到非游戏区，或者初次进入，则重置随机索引
        if (lastZone == SceneZone.Game || lastZone == SceneZone.None)
        {
            currentGroupIndex = Random.Range(0, musicGroups.Length);
            isFastModeActive = false; // 重置快节奏标记
            Debug.Log($"[Music] Session Reset. Picked Music Group: {musicGroups[currentGroupIndex].groupName}");
        }

        // 播放对应分区的音乐
        if (currentZone == SceneZone.Menu)
        {
            CrossFadeTo(musicGroups[currentGroupIndex].startMenuBGM);
        }
        else if (currentZone == SceneZone.Lobby)
        {
            CrossFadeTo(musicGroups[currentGroupIndex].lobbyRoomBGM);
        }
        else if (currentZone == SceneZone.Game)
        {
            // 刚进游戏场景，播放正常的 InGame BGM
            isFastModeActive = false;
            hasHandledGameOverMusic = false; // 重置游戏结束音乐处理标志
            CrossFadeTo(musicGroups[currentGroupIndex].inGameNormalBGM);
        }
    }

    private void CheckGameTimer()
    {
        // 检查 GameManager 里的计时器
        if (GameManager.Instance != null && !isFastModeActive)
        {
            // 如果计时器小于 60s 且游戏还没结束
            if (GameManager.Instance.gameTimer > 0 && GameManager.Instance.gameTimer <= fastModeThreshold)
            {
                if (GameManager.Instance.CurrentState == GameManager.GameState.InGame)
                {
                    isFastModeActive = true;
                    Debug.Log("[Music] Time Running Out! Switching to Fast BGM.");
                    CrossFadeTo(musicGroups[currentGroupIndex].inGameFastBGM);
                }
            }
        }
    }

    private void CheckGameOver()
    {
        if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
        {
            if (!hasHandledGameOverMusic) // 增加判断
            {
                hasHandledGameOverMusic = true;
                Debug.Log("[Music] Victory Zone Entered. Fading out In-Game BGM.");
                CrossFadeTo(null);
            }
        }
    }

    private SceneZone GetZoneForScene(string sceneName)
    {
        if (sceneName.StartsWith("MyScene")) return SceneZone.Game;
        if (sceneName == "LobbyRoom") return SceneZone.Lobby;
        return SceneZone.Menu; // StartMenu, ConnectRoom 等
    }

    public void CrossFadeTo(AudioClip newClip)
    {
        AudioSource activeSource = isSourceAActive ? sourceA : sourceB;
        if (activeSource.clip == newClip && activeSource.isPlaying && activeSource.volume > 0) return;

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
            if (newClip != null) fadeInSource.volume = Mathf.Lerp(0, maxVolume, t);

            yield return null;
        }

        fadeOutSource.volume = 0;
        fadeOutSource.Stop();
        fadeOutSource.clip = null;
        
        if (newClip != null) fadeInSource.volume = maxVolume;

        isSourceAActive = !isSourceAActive;
        activeFadeRoutine = null;
    }
}