using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class SceneScript : MonoBehaviour
{
    public TextMeshProUGUI RoleText;//显示角色的文本
    public TextMeshProUGUI NameText;//显示名字的文本
    public Slider HealthSlider;//血量滑动条
    public Slider ManaSlider;//法力值滑动条

    public void ButtonQuitGame()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log("尝试退出游戏");
        // 不需要判断 playerScript，直接操作 NetworkManager
        
        // 情况A: 我是 Host (既是服务器又是客户端)
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            // Host 退出，服务器关闭，所有人都会掉线，这是正常现象
            NetworkManager.singleton.StopHost();
        }
        // 情况B: 我只是 Client (普通玩家)
        else if (NetworkClient.isConnected)
        {
            // Client 退出，只断开我自己，服务器和其他人不受影响
            NetworkManager.singleton.StopClient();
        }
        // 情况C: 我只是 Server (专用服务器模式，一般UI点不到这里)
        else if (NetworkServer.active)
        {
            NetworkManager.singleton.StopServer();
        }

        // 注意：不需要手动 SceneManager.LoadScene("StartMenu");
        // 只要你在 NetworkManager 面板里设置了 "Offline Scene" 为 StartMenu
        // Mirror 会在断开连接后自动跳转回菜单。
    }

}
