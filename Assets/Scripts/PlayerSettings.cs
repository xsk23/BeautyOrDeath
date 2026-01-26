using UnityEngine;

public class PlayerSettings : MonoBehaviour
{
    public static PlayerSettings Instance { get; private set; }

    public string PlayerName { get; set; } = "";  // 預設值

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // 可選：提供清除方法（斷線重連時用）
    public void Clear()
    {
        PlayerName = "Player";
    }
}