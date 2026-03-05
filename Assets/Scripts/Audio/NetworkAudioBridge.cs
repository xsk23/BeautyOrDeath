using UnityEngine;
using Mirror;

public class NetworkAudioBridge : NetworkBehaviour
{
    public static NetworkAudioBridge Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // 服务器调用这个，通知全网播放 3D 声音
    [Server]
    public void ServerPlay3DAt(string soundName, Vector3 position)
    {
        RpcPlay3D(soundName, position);
    }

    [ClientRpc]
    private void RpcPlay3D(string soundName, Vector3 position)
    {
        // 客户端收到命令，调用本地的 AudioManager 播放
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.Play3D(soundName, position);
        }
    }
}