using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Mirror;
using UnityEngine.UI;

public class NetworkManagerHUD_UGUI : MonoBehaviour
{
    NetworkManager manager;
    public GameObject StartButtonGroup;//开始按钮组
    public GameObject StopButtonGroup;//停止按钮组
    public Text StatusText;//状态文本
    public Button HostButton;//主机按钮
    public Button ClientButton;//客户端按钮
    public InputField inputFieldIP;//IP输入框
    public InputField inputFieldPort;//端口输入框
    public Button ServerOnlyButton;//仅服务器按钮
    public Button StopButton;//停止按钮

    //点击创建Server
    public void OnClickServerOnltBtn()
    {
        manager.StartServer();
    }
    //点击创建client
    private void OnClickClient()
    {
        manager.StartClient();
    }
    //点击创建Host
    private void OnClickHost()
    {
        manager.StartHost();
    }
    //点击停止按钮
    private void OnClickStopBtn()
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
    void StatusLabels()
    {
        // host mode
        // display separately because this always confused people:
        //   Server: ...
        //   Client: ...
        if (NetworkServer.active && NetworkClient.active)
        {
            // host mode
            StatusText.text = $"<b>Host</b>: running via {Transport.active}";
        }
        else if (NetworkServer.active)
        {
            // server only
            StatusText.text = $"<b>Server</b>: running via {Transport.active}";
        }
        else if (NetworkClient.isConnected)
        {
            // client only
            StatusText.text = $"<b>Client</b>: connected to {manager.networkAddress} via {Transport.active}";
        }
    }


    void Start()
    {
        manager = FindObjectOfType<NetworkManager>();
        //按钮绑定事件
        HostButton.onClick.AddListener(OnClickHost);
        ClientButton.onClick.AddListener(OnClickClient); 
        ServerOnlyButton.onClick.AddListener(OnClickServerOnltBtn);
        StopButton.onClick.AddListener(OnClickStopBtn);
    }
    void Update()
    {
        if (!NetworkClient.isConnected && !NetworkServer.active)
        {
            if (!NetworkClient.active)
            {
                manager.networkAddress = inputFieldIP.text;
                // only show a port field if we have a port transport
                // we can't have "IP:PORT" in the address field since this only
                // works for IPV4:PORT.
                // for IPV6:PORT it would be misleading since IPV6 contains ":":
                // 2001:0db8:0000:0000:0000:ff00:0042:8329
                if (Transport.active is PortTransport portTransport)
                {
                    // use TryParse in case someone tries to enter non-numeric characters
                    if (ushort.TryParse(inputFieldPort.text, out ushort port))
                        portTransport.Port = port;
                }      
                StatusText.text = "";
            }  
            else
            {
                // Connecting
                StatusText.text = $"Connecting to {manager.networkAddress}..";              
            }
            StartButtonGroup.SetActive(true);
            StopButtonGroup.SetActive(false);   
            
        }
        else
        {
            StatusLabels();
        }
        if (NetworkServer.active && NetworkClient.active)
        {
            StartButtonGroup.SetActive(false);
            StopButtonGroup.SetActive(true);
        }
        else if (NetworkServer.active)
        {
            // server only
            StartButtonGroup.SetActive(false);
            StopButtonGroup.SetActive(true);        
        }
        else if (NetworkClient.isConnected)
        {
            // client only
            StartButtonGroup.SetActive(false);
            StopButtonGroup.SetActive(true);        
        }            
    }
}
