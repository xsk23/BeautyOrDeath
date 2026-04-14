using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using Mirror;

public class TabInfoManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject tabInfoPanel;      
    public Transform rowContainer;       
    public GameObject tabRowPrefab;      

    [Header("Global Game Stats (New)")]
    public TextMeshProUGUI treesNeededText;      // 对应 Trees needed
    public TextMeshProUGUI mapResourceText;     // 对应 Ancient Trees on Map
    public TextMeshProUGUI teamAliveCountText;  // 对应角色的存活统计
    
    [Header("Scouting Reward (Witch Only)")]
    public GameObject scoutingSection;           // 整个奖励UI组
    public Slider scoutingProgressBar;           // 进度条
    public TextMeshProUGUI scoutingRatioText;    // 显示 5/20

    [Header("Data")]
    public List<SkillData> skillDatabase; 

    private Dictionary<GamePlayer, TabRowUI> activeRows = new Dictionary<GamePlayer, TabRowUI>();

    private void Start()
    {
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

        // 面板打开时，每一帧刷新所有数据
        if (tabInfoPanel.activeSelf)
        {
            RefreshPlayerList();
            RefreshGlobalStats();
        }
    }

    private void TogglePanel(bool show)
    {
        tabInfoPanel.SetActive(show);
        if (show)
        {
            RefreshPlayerList();
            RefreshGlobalStats();
        }
    }

    // 1. 刷新玩家列表行 (Scoreboard)
    private void RefreshPlayerList()
    {
        List<GamePlayer> toRemove = new List<GamePlayer>();
        foreach (var pair in activeRows)
        {
            if (pair.Key == null) toRemove.Add(pair.Key);
        }
        foreach (var key in toRemove)
        {
            if(activeRows[key] != null) Destroy(activeRows[key].gameObject);
            activeRows.Remove(key);
        }

        foreach (var player in GamePlayer.AllPlayers)
        {
            if (player == null) continue;

            if (!activeRows.ContainsKey(player))
            {
                GameObject newRow = Instantiate(tabRowPrefab, rowContainer);
                TabRowUI script = newRow.GetComponent<TabRowUI>();
                activeRows.Add(player, script);
            }
            activeRows[player].UpdateRow(player, skillDatabase);
        }
    }

    // 2. 刷新全局战况 (你想要塞进去的资讯 1-4)
    private void RefreshGlobalStats()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        // --- (1) Trees Needed ---
        int delivered = gm.deliveredTreesCount;
        int total = gm.totalRequiredTrees;
        int remaining = Mathf.Max(0, total - delivered);
        if (treesNeededText != null)
        {
            treesNeededText.text = $"GOAL: <color=yellow>{remaining}</color> TREES REMAINING";
        }

        // --- (2) Ancient Trees on Map ---
        if (mapResourceText != null)
        {
            mapResourceText.text = $"MAP RESOURCES: <color=#00FF00>{gm.availableAncientTreesCount}</color> ANCIENT TREES LEFT";
        }

        // --- (3) Team Alive Count ---
        if (teamAliveCountText != null)
        {
            teamAliveCountText.text = $"<color=cyan>HUNTERS: {gm.aliveHuntersCount}</color>  |  <color=magenta>WITCHES: {gm.aliveWitchesCount}</color>";
        }

        // --- (4) Scouting Reward (仅本地是女巫时显示) ---
        var localPlayer = NetworkClient.localPlayer?.GetComponent<WitchPlayer>();
        if (localPlayer != null && scoutingSection != null)
        {
            scoutingSection.SetActive(true);
            int current = localPlayer.scoutedCount % localPlayer.treesPerReward;
            int max = localPlayer.treesPerReward;
            
            if(scoutingProgressBar != null) scoutingProgressBar.value = (float)current / max;
            if(scoutingRatioText != null) scoutingRatioText.text = $"SCOUTING PROGRESS: {current} / {max}";
        }
        else if (scoutingSection != null)
        {
            scoutingSection.SetActive(false);
        }
    }
}