using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mirror;

public class HUDExtension : MonoBehaviour
{
    private void OnEnable()
    {
        SceneManager.activeSceneChanged += HandleSceneChanged;//注册场景切换事件
    }
    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= HandleSceneChanged;//注销场景切换事件
    }
    private void HandleSceneChanged(Scene oldScene, Scene newScene)
    {
        GetComponent<NetworkManagerHUD>().enabled = newScene.name != "Menu";//在非菜单场景启用HUD
    }
}
