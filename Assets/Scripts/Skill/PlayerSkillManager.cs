using UnityEngine;
using Mirror;
using System.Collections; // 必须引用协程
using System.Collections.Generic;

public class PlayerSkillManager : NetworkBehaviour
{
    [Header("Skill Configuration")]
    public List<SkillData> skillDatabase; // 在预制体里把 7 个 SkillData 资产拖进去
    
    private SkillBase[] activeSkillsArray; 
    private GamePlayer player;

    public override void OnStartLocalPlayer()
    {
        player = GetComponent<GamePlayer>();
        
        // 使用协程确保 SceneScript 已经初始化完成
        StartCoroutine(InitSkillsAndUIRoutine());
    }

    private IEnumerator InitSkillsAndUIRoutine()
    {
        // 1. 等待场景中的 SceneScript 准备就绪
        while (SceneScript.Instance == null || SceneScript.Instance.skillSlots == null)
        {
            yield return null;
        }

        // 2. 获取选中的脚本名称列表（从持久化单例读取）
        List<string> selectedClasses = (player is WitchPlayer) 
            ? PlayerSettings.Instance.selectedWitchSkillNames 
            : PlayerSettings.Instance.selectedHunterSkillNames;

        // --- 【新增：同步给其他玩家】 ---
        if (selectedClasses != null && selectedClasses.Count >= 2)
        {
            player.CmdSyncSkillNames(selectedClasses[0], selectedClasses[1]);
        }

        // 如果是大厅直接进游戏测试，列表可能为空，做一个保底逻辑
        if (selectedClasses == null || selectedClasses.Count == 0)
        {
            Debug.LogWarning("[PlayerSkillManager] 选中的技能列表为空，请检查 Lobby 选择逻辑。");
            yield break;
        }

        List<SkillBase> runtimeSkills = new List<SkillBase>();

        // 3. 激活并映射技能
        for (int i = 0; i < selectedClasses.Count; i++)
        {
            string className = selectedClasses[i];
            
            // 获取挂在玩家预制体身上的对应脚本组件
            SkillBase skillComp = GetComponent(className) as SkillBase;

            if (skillComp != null)
            {
                //Debug.Log($"[SkillDebug] 成功找到组件: {className}");
                // 激活脚本逻辑
                skillComp.enabled = true;
                skillComp.Init(player);
                
                // 强制分配按键：第一个选中的是 Q，第二个选中的是 E
                skillComp.triggerKey = (i == 0) ? KeyCode.Q : KeyCode.E;
                
                runtimeSkills.Add(skillComp);

                // --- 【核心修改：更新游戏内 UI】 ---
                // 从数据库中根据脚本类名找到对应的图标资产
                var data = skillDatabase.Find(d => d.scriptClassName == className);
                if (data != null)
                {
                    // 将图标和分配的按键名称（"Q" 或 "E"）传给 SceneScript 的 UI 槽位
                    if (i < SceneScript.Instance.skillSlots.Length)
                    {
                        SceneScript.Instance.skillSlots[i].Setup(data.icon, skillComp.triggerKey.ToString());
                        SceneScript.Instance.skillSlots[i].gameObject.SetActive(true);
                    }
                }
            }
            else
            {
                Debug.LogError($"[SkillDebug] 未找到组件: {className}，请确保已挂载在玩家预制体上。(可能是skillData那里有空格！！！！！！！！！！！！！！！！)");
            }
        }
        
        activeSkillsArray = runtimeSkills.ToArray();
    }

    public override void OnStartServer()
    {
        player = GetComponent<GamePlayer>();
        foreach (var s in GetComponents<SkillBase>())
        {
            s.Init(player);
        }
    }

    // public override void OnStartClient()
    // {
    //     base.OnStartClient();
    //     // 如果是本地玩家，OnStartLocalPlayer 已经处理过了，这里跳过避免重复
    //     if (isLocalPlayer) return;

    //     player = GetComponent<GamePlayer>();
    //     foreach (var s in GetComponents<SkillBase>())
    //     {
    //         s.Init(player);
    //     }
    // }

    private void Update()
    {
        if (!isLocalPlayer || activeSkillsArray == null) return;

        // 处理技能按键触发
        if (Cursor.lockState == CursorLockMode.Locked && !player.isChatting && !player.isStunned)
        {
            foreach (var skill in activeSkillsArray)
            {
                if (skill != null && Input.GetKeyDown(skill.triggerKey))
                {
                    skill.TryCast();
                }
            }
        }

        // 更新 UI 冷却进度条
        if (SceneScript.Instance != null && SceneScript.Instance.skillSlots != null)
        {
            for (int i = 0; i < activeSkillsArray.Length; i++)
            {
                if (i < SceneScript.Instance.skillSlots.Length && activeSkillsArray[i] != null)
                {
                    SceneScript.Instance.skillSlots[i].UpdateCooldown(activeSkillsArray[i].CooldownRatio);
                }
            }
        }
    }
}