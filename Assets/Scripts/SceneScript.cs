using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class SceneScript : NetworkBehaviour
{
    public Text canvasBulletText;//显示子弹文本
    public Text canvasStatusText;//显示消息文本
    public PlayerScript playerScript;//玩家脚本引用

    [SyncVar(hook = nameof(OnStatusTextChanged))]
    public string statusText;//同步的状态消息

    //状态消息同步变量
    private void OnStatusTextChanged(string oldText, string newText)
    {
        canvasStatusText.text = newText;
    }
    //按钮发送信息
    public void ButtonSendMessage()
    {
        Debug.Log("尝试发送消息");
        if(playerScript!=null && playerScript.isLocalPlayer)
        {
            playerScript.CmdSendPlayerMessage();
        }
    }
    // 按钮退出房间
    public void ButtonQuitGame()
    {
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
    //切换场景按钮
    public void ButtonSwitchScene()
    {
       if(isServer)//只有主机可以切换场景
       {
            var scene = SceneManager.GetActiveScene();//获取当前场景名字
            NetworkManager.singleton.ServerChangeScene(
                scene.name== "MyScene" ? "MyOtherScene" : "MyScene"
            );//服务器切换场景
       }
       else
       {
            Debug.Log("You are not the host!");
       }
    }
}
