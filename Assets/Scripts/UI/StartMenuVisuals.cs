using UnityEngine;
using System.Collections.Generic;

public class StartMenuVisuals : MonoBehaviour
{
    [Header("树木设置")]
    public Transform treesParent; // 拖入层级中的 Vegetation/Trees 对象

    [Header("相机旋转设置")]
    public Transform cameraTransform; // 拖入 Main Camera
    public float rotationSpeed = 2.0f; // 旋转速度（度/秒）
    public Vector3 rotationAxis = Vector3.up; // 绕 Y 轴旋转

    void Start()
    {
        RandomizeTrees();
    }

    void Update()
    {
        RotateCamera();
    }

    private void RandomizeTrees()
    {
        if (treesParent == null)
        {
            Debug.LogWarning("[Visuals] 未指定 Trees Parent，无法执行随机隐藏。");
            return;
        }

        // 1. 获取所有子物体（树）
        List<GameObject> allTrees = new List<GameObject>();
        foreach (Transform child in treesParent)
        {
            allTrees.Add(child.gameObject);
        }

        if (allTrees.Count == 0) return;

        // 2. 打乱列表顺序 (洗牌算法)
        for (int i = 0; i < allTrees.Count; i++)
        {
            GameObject temp = allTrees[i];
            int randomIndex = Random.Range(i, allTrees.Count);
            allTrees[i] = allTrees[randomIndex];
            allTrees[randomIndex] = temp;
        }

        // 3. 隐藏前一半的树
        int countToHide = allTrees.Count / 2;
        for (int i = 0; i < countToHide; i++)
        {
            allTrees[i].SetActive(false);
        }

        Debug.Log($"[Visuals] 初始树木总数: {allTrees.Count}, 已隐藏: {countToHide}");
    }

    private void RotateCamera()
    {
        if (cameraTransform == null) return;

        // 缓慢进行 360 度循环旋转
        cameraTransform.Rotate(rotationAxis, rotationSpeed * Time.deltaTime, Space.World);
    }
}