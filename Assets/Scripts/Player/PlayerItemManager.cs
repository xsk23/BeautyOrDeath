// --- PlayerItemManager.cs ---
using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class PlayerItemManager : NetworkBehaviour
{
    [Header("Data")]
    public List<WitchItemData> itemDatabase; // 【新增】拖入所有女巫道具的 ScriptableObject
    
    private WitchPlayer witch;
    private WitchItemBase activeItemInstance; // 缓存当前激活的道具脚本

    public override void OnStartLocalPlayer()
    {
        witch = GetComponent<WitchPlayer>();
        string selectedClassName = PlayerSettings.Instance.selectedWitchItemName;

        if (string.IsNullOrEmpty(selectedClassName)) return;

        // 获取所有道具
        WitchItemBase[] allItems = GetComponentsInChildren<WitchItemBase>(true);
        
        foreach (var item in allItems)
        {
            bool isMatch = item.GetType().Name == selectedClassName;
            item.isActive = isMatch;
            item.enabled = isMatch;
            item.gameObject.SetActive(isMatch);
            
            if (isMatch)
            {
                activeItemInstance = item;
                
                // --- 【核心修改：初始化道具 UI】 ---
                var data = itemDatabase.Find(d => d.scriptClassName == selectedClassName);
                if (data != null && SceneScript.Instance != null && SceneScript.Instance.itemSlot != null)
                {
                    // 设置图标和按键文字 "F"
                    SceneScript.Instance.itemSlot.Setup(data.icon, "F");
                    SceneScript.Instance.itemSlot.gameObject.SetActive(true);
                }

                // 更新同步和逻辑
                witch.currentItemIndex = System.Array.IndexOf(witch.witchItems, item.gameObject);
                witch.CmdChangeItem(witch.currentItemIndex);
            }
        }
    }

    private void Update()
    {
        // 只有本地玩家且有激活道具时更新 UI 遮罩
        if (!isLocalPlayer || activeItemInstance == null) return;

        if (SceneScript.Instance != null && SceneScript.Instance.itemSlot != null)
        {
            SceneScript.Instance.itemSlot.UpdateCooldown(activeItemInstance.CooldownRatio);
        }
    }
}