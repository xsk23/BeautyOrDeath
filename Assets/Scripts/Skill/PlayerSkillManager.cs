using UnityEngine;
using Mirror;
using System.Collections.Generic;


public class PlayerSkillManager : NetworkBehaviour
{
    // 为了显示图标，我们还是需要数据库，但不涉及预制体
    public List<SkillData> skillDatabase; 
    public SkillBase[] skills; 
    private GamePlayer player;

    public override void OnStartLocalPlayer()
    {
        player = GetComponent<GamePlayer>();
        
        // 1. 获取选中的脚本名称
        List<string> selectedClasses = (player is WitchPlayer) 
            ? PlayerSettings.Instance.selectedWitchSkillNames 
            : PlayerSettings.Instance.selectedHunterSkillNames;

        List<SkillBase> activeSkills = new List<SkillBase>();

        // 2. 遍历选中的两个名称
        for (int i = 0; i < selectedClasses.Count; i++)
        {
            string className = selectedClasses[i];
            
            // 【核心：直接通过字符串类名获取挂在自己身上的组件】
            SkillBase skillComp = GetComponent(className) as SkillBase;

            if (skillComp != null)
            {
                // 激活组件
                skillComp.enabled = true;
                skillComp.Init(player);
                
                // 分配按键 Q 和 E
                skillComp.triggerKey = (i == 0) ? KeyCode.Q : KeyCode.E;
                
                activeSkills.Add(skillComp);

                // 更新 UI 图标 (从数据库找图标)
                var data = skillDatabase.Find(d => d.scriptClassName == className);
                if (data != null && i < SceneScript.Instance.skillSlots.Length)
                {
                    SceneScript.Instance.skillSlots[i].Setup(data.icon, skillComp.triggerKey.ToString());
                    SceneScript.Instance.skillSlots[i].gameObject.SetActive(true);
                }
            }
        }
        
        // 覆盖 skills 数组供 Update 使用
        this.skills = activeSkills.ToArray();
    }

    public override void OnStartServer()
    {
        player = GetComponent<GamePlayer>();
        // 服务器需要初始化身上所有的技能组件，以响应客户端的 Command
        foreach (var s in GetComponents<SkillBase>())
        {
            s.Init(player);
        }
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        // 1. 处理输入
        if (Cursor.lockState == CursorLockMode.Locked && !player.isChatting && !player.isStunned)
        {
            foreach (var skill in skills)
            {
                if (skill != null && Input.GetKeyDown(skill.triggerKey))
                {
                    skill.TryCast();
                }
            }
        }

        // 2. 更新 UI 冷却遮罩
        if (SceneScript.Instance != null && SceneScript.Instance.skillSlots != null)
            {
                for (int i = 0; i < skills.Length; i++)
                {
                    
                    // 如果技能本身为空，或者越界，或者对应的 UI 槽位没配置，就跳过
                    if (skills[i] == null) continue;
                    if (i >= SceneScript.Instance.skillSlots.Length) continue;
                    if (SceneScript.Instance.skillSlots[i] == null) continue;
                    

                    SceneScript.Instance.skillSlots[i].UpdateCooldown(skills[i].CooldownRatio);
                }
            }
    }
}