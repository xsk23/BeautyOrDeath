using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class SoundAction
{
    public string soundName;       // 声音的唯一标识符（如 "Footstep_Dirt", "Shotgun_Fire"）
    public AudioClip[] clips;      // 数组：支持同类音效随机播放（如 3 种不同泥土脚步声）
    [Range(0f, 1f)] public float volume = 1.0f;
    public bool randomPitch = false; // 是否随机音高（防单调）
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("音效库配置")]
    public SoundAction[] soundLibrary;
    private Dictionary<string, SoundAction> soundDictionary;

    [Header("2D音效源 (UI、系统音)")]
    public AudioSource source2D;

    [Header("3D音效对象池配置")]
    public GameObject audioSourcePrefab; // 需要一个挂了 AudioSource 的空物体Prefab
    public int poolSize = 10;
    private Queue<AudioSource> sourcePool3D;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 初始化字典，查找速度 O(1)
        soundDictionary = new Dictionary<string, SoundAction>();
        foreach (var sound in soundLibrary)
        {
            if (!soundDictionary.ContainsKey(sound.soundName))
                soundDictionary.Add(sound.soundName, sound);
        }

        // 初始化 3D 音效对象池
        sourcePool3D = new Queue<AudioSource>();
        for (int i = 0; i < poolSize; i++)
        {
            AudioSource newSource = Instantiate(audioSourcePrefab, transform).GetComponent<AudioSource>();
            newSource.gameObject.SetActive(false);
            sourcePool3D.Enqueue(newSource);
        }
    }

    // --- 播放 2D 声音（如 UI点击、耳鸣、心跳声） ---
    public void Play2D(string name)
    {
        if (soundDictionary.TryGetValue(name, out SoundAction soundData) && soundData.clips.Length > 0)
        {
            AudioClip clip = soundData.clips[Random.Range(0, soundData.clips.Length)];
            if (soundData.randomPitch) source2D.pitch = Random.Range(0.9f, 1.1f);
            else source2D.pitch = 1f;
            
            source2D.PlayOneShot(clip, soundData.volume);
        }
    }

    // --- 播放 3D 声音（如 枪声、变身音效、狗叫，具有空间衰减） ---
    public void Play3D(string name, Vector3 position)
    {
        if (soundDictionary.TryGetValue(name, out SoundAction soundData) && soundData.clips.Length > 0)
        {
            AudioSource source = GetPooledSource();
            if (source == null) return; // 池满了且都在播放，直接丢弃（防止爆音）

            source.transform.position = position;
            source.gameObject.SetActive(true);

            AudioClip clip = soundData.clips[Random.Range(0, soundData.clips.Length)];
            source.clip = clip;
            source.volume = soundData.volume;
            
            if (soundData.randomPitch) source.pitch = Random.Range(0.85f, 1.15f);
            else source.pitch = 1f;

            source.Play();

            // 播放完毕后自动回收
            StartCoroutine(ReturnToPool(source, clip.length));
        }
        else
        {
            Debug.LogWarning($"[AudioManager] Sound '{name}' not found!");
        }
    }

    private AudioSource GetPooledSource()
    {
        if (sourcePool3D.Count > 0) return sourcePool3D.Dequeue();
        return null; 
    }

    private System.Collections.IEnumerator ReturnToPool(AudioSource source, float delay)
    {
        yield return new WaitForSeconds(delay);
        source.gameObject.SetActive(false);
        sourcePool3D.Enqueue(source);
    }
}