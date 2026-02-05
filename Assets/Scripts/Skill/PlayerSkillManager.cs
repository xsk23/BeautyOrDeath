using UnityEngine;
using Mirror;


public class PlayerSkillManager : NetworkBehaviour
{
    [Header("Skills Configuration")]
    public SkillBase[] skills; // 在 Inspector 中把具体的技能组件拖进去

    private GamePlayer player;

    public override void OnStartLocalPlayer()
    {
        player = GetComponent<GamePlayer>();
        
        // 检查 UI 数组
        //if (SceneScript.Instance.skillSlots == null) Debug.LogError("SceneScript.Instance.skillSlots is NULL!");
        //else Debug.Log($"UI Slots Count: {SceneScript.Instance.skillSlots.Length}");

        for (int i = 0; i < skills.Length; i++)
        {

            //Debug.Log($"Processing Skill {i}: {skills[i].GetType().Name}, Icon: {skills[i].icon?.name ?? "NULL"}");

            if (i < SceneScript.Instance.skillSlots.Length)
            {   
                skills[i].Init(player);
                SceneScript.Instance.skillSlots[i].Setup(skills[i].icon, skills[i].triggerKey.ToString());
                SceneScript.Instance.skillSlots[i].gameObject.SetActive(true);
            }
        }
    }

    public override void OnStartServer()
    {
        // 服务器端初始化拥有者引用
        player = GetComponent<GamePlayer>();
        foreach (var skill in skills)
        {
            if(skill != null) skill.Init(player);
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