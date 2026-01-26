 using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class SingletonAutoMono<T> : MonoBehaviour where T : SingletonAutoMono<T>
{
    private static T _instance;
    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                // 尝试在场景中找到已有的实例
                _instance = FindObjectOfType<T>();
                if (_instance == null)
                {
                    // 如果没有找到，则创建一个新的 GameObject 并附加该组件
                    GameObject singletonObject = new GameObject(typeof(T).Name);
                    singletonObject.name = typeof(T).ToString();
                    _instance = singletonObject.AddComponent<T>();
                    DontDestroyOnLoad(singletonObject); // 可选：在场景切换时不销毁
                }
            }
            return _instance;
        }
    }

    protected virtual void Awake()
    {
        // 确保只有一个实例存在
        if (_instance == null)
        {
            _instance = this as T;
            DontDestroyOnLoad(gameObject); // 可选：在场景切换时不销毁
        }
        else if (_instance != this)
        {
            Destroy(gameObject); // 销毁重复的实例
        }
    }
}
