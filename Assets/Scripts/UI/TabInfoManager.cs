using UnityEngine;
using System.Collections.Generic;

public class TabInfoManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject tabInfoPanel;      // 对应你的 TabInfoPanel
    public Transform rowContainer;       // TabInfoGroup 生成的父物体 (如果没有 LayoutGroup 建议加一个)
    public GameObject tabRowPrefab;      // 你的 TabInfoGroup 预制体

    private Dictionary<GamePlayer, TabRowUI> activeRows = new Dictionary<GamePlayer, TabRowUI>();
    [Header("Data")]
    public List<SkillData> skillDatabase; // 在 Inspector 中拖入所有技能的 ScriptableObject
    private void Start()
    {
        // 初始关闭
        tabInfoPanel.SetActive(false);
    }

    private void Update()
    {
        // 检测 Tab 键
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            TogglePanel(true);
        }
        if (Input.GetKeyUp(KeyCode.Tab))
        {
            TogglePanel(false);
        }

        // 如果面板打开着，实时刷新数据
        if (tabInfoPanel.activeSelf)
        {
            RefreshData();
        }
    }

    private void TogglePanel(bool show)
    {
        tabInfoPanel.SetActive(show);
        if (show)
        {
            RefreshData();
        }
    }

    private void RefreshData()
    {
        // 1. 清理已退出的玩家行
        List<GamePlayer> toRemove = new List<GamePlayer>();
        foreach (var pair in activeRows)
        {
            if (pair.Key == null) toRemove.Add(pair.Key);
        }
        foreach (var key in toRemove)
        {
            Destroy(activeRows[key].gameObject);
            activeRows.Remove(key);
        }

        // 2. 更新或生成所有玩家的信息
        foreach (var player in GamePlayer.AllPlayers)
        {
            if (player == null) continue;

            if (!activeRows.ContainsKey(player))
            {
                // 生成新行
                GameObject newRow = Instantiate(tabRowPrefab, rowContainer);
                TabRowUI script = newRow.GetComponent<TabRowUI>();
                activeRows.Add(player, script);
            }

            // 【关键修改】传递数据库引用
            activeRows[player].UpdateRow(player, skillDatabase);
        }
    }
}