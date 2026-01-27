using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;
public class Menu : MonoBehaviour
{
    NetworkManager manager;
    private void Start()
    {
        manager = FindObjectOfType<NetworkManager>();
    }
    //点击停止按钮
    public void OnClickStopBtn()
    {
        if (NetworkServer.active && NetworkClient.isConnected)
        {
            manager.StopHost();
        }
        else if (NetworkClient.isConnected)
        {
            manager.StopClient();
        }
        else if (NetworkServer.active)
        {
            manager.StopServer();
        }
    }
}
