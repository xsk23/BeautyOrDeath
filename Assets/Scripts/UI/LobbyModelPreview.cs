using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class LobbyModelPreview : MonoBehaviour
{
    // 【新增】单例，方便 UI 脚本调用刷新
    public static LobbyModelPreview Instance;
    [Header("UI Buttons")]
    public Button maleButton;
    public Button femaleButton;

    [Header("Models")]
    public Animator witchMale;
    public Animator witchFemale;
    public Animator hunterMale;
    public Animator hunterFemale;
    [Header("Item Variants")]
    public Animator witchMaleCloak;
    public Animator witchFemaleCloak;
    public Animator witchMaleAmulet;   // 【新增】
    public Animator witchFemaleAmulet; // 【新增】
    public Animator witchMaleBroom;    // 【新增】
    public Animator witchFemaleBroom;  // 【新增】

    [Header("Movement Settings")]
    public float forwardZ = -1.5f; 
    public float backwardZ = 1.0f; 
    public float lerpSpeed = 10f; // 增加速度让反馈更即时

    [Header("Rotation Settings")]
    public Vector3 facingRotation = new Vector3(0, 180, 0); // 如果模型背对镜头，修改这里的 Y
    [Header("Config")]
    public string cloakItemName = "InvisibilityCloak"; // 必须与 WitchItemData 里的类名一致

    // 记录所有基础坐标
    private Vector3 wMaleBase, wFemaleBase, hMaleBase, hFemaleBase;
    private Vector3 wMaleCloakBase, wFemaleCloakBase;
    private Vector3 wMaleAmuletBase, wFemaleAmuletBase;
    private Vector3 wMaleBroomBase, wFemaleBroomBase;
    private Gender currentGender;
    private void Awake() => Instance = this; // 初始化单例
    private void Start()
    {
        // 1. 记录初始位置
        // 建议在编辑器里把这4个模型放在同一个 Z 轴坐标上
        wMaleBase = witchMale.transform.localPosition;
        wFemaleBase = witchFemale.transform.localPosition;
        hMaleBase = hunterMale.transform.localPosition;
        hFemaleBase = hunterFemale.transform.localPosition;
        // 【新增】记录斗篷版位置
        wMaleCloakBase = witchMaleCloak.transform.localPosition;
        wFemaleCloakBase = witchFemaleCloak.transform.localPosition;
        wMaleAmuletBase = witchMaleAmulet.transform.localPosition;
        wFemaleAmuletBase = witchFemaleAmulet.transform.localPosition;
        wMaleBroomBase = witchMaleBroom.transform.localPosition;
        wFemaleBroomBase = witchFemaleBroom.transform.localPosition;
        // 2. 绑定按钮
        maleButton.onClick.AddListener(() => UpdateSelection(Gender.Male));
        femaleButton.onClick.AddListener(() => UpdateSelection(Gender.Female));

        // 3. 初始读取性别并直接应用（不等待 Lerp）
        currentGender = PlayerSettings.Instance.selectedGender;
        ApplySelection(currentGender, true);
    }
    // 【新增方法】供道具选择 UI 调用，当玩家切换道具时刷新模型
    public void RefreshItemSelection()
    {
        ApplySelection(currentGender, false);
    }
    private void UpdateSelection(Gender gender)
    {
        if (currentGender == gender) return;
        ApplySelection(gender, false);
    }

    private void ApplySelection(Gender gender, bool immediate)
    {
        currentGender = gender;
        PlayerSettings.Instance.SetGender((int)gender);
        // 1. 【核心修复】在切换性别的瞬间，立刻把所有模型的旋转拉回初始方向
        // 这样可以清除上一段动画可能残留的微小旋转误差
        ResetAllRotations();
        if (NetworkClient.active && NetworkClient.localPlayer != null)
        {
            NetworkClient.localPlayer.GetComponent<PlayerScript>().CmdUpdateGender(gender);
        }

        // 按钮交互
        if (maleButton) maleButton.interactable = (gender != Gender.Male);
        if (femaleButton) femaleButton.interactable = (gender != Gender.Female);

        if (immediate)
        {
            UpdateAllPositions(true);
        }
    }
    private void UpdateAllPositions(bool immediate)
    {
        string selectedItem = PlayerSettings.Instance.selectedWitchItemName;

        // --- 核心判定：男巫组 ---
        // 只有选了对应的道具，该模型才会 SetActive(true)，并根据性别决定 forwardZ/backwardZ
        HandleModelLogic(witchMale, wMaleBase, currentGender == Gender.Male, selectedItem == "", immediate);
        HandleModelLogic(witchMaleCloak, wMaleCloakBase, currentGender == Gender.Male, selectedItem == "InvisibilityCloak", immediate);
        HandleModelLogic(witchMaleAmulet, wMaleAmuletBase, currentGender == Gender.Male, selectedItem == "LifeAmulet", immediate);
        HandleModelLogic(witchMaleBroom, wMaleBroomBase, currentGender == Gender.Male, selectedItem == "MagicBroom", immediate);

        // --- 核心判定：女巫组 ---
        HandleModelLogic(witchFemale, wFemaleBase, currentGender == Gender.Female, selectedItem == "", immediate);
        HandleModelLogic(witchFemaleCloak, wFemaleCloakBase, currentGender == Gender.Female, selectedItem == "InvisibilityCloak", immediate);
        HandleModelLogic(witchFemaleAmulet, wFemaleAmuletBase, currentGender == Gender.Female, selectedItem == "LifeAmulet", immediate);
        HandleModelLogic(witchFemaleBroom, wFemaleBroomBase, currentGender == Gender.Female, selectedItem == "MagicBroom", immediate);

        // 猎人保持原样
        HandleModelLogic(hunterMale, hMaleBase, currentGender == Gender.Male, true, immediate);
        HandleModelLogic(hunterFemale, hFemaleBase, currentGender == Gender.Female, true, immediate);
    }
    // 辅助方法：统一处理位置、动画和显隐
    private void HandleModelLogic(Animator anim, Vector3 basePos, bool isGenderSelected, bool isItemMatch, bool immediate)
    {
        if (anim == null) return;

        // 只有该性别的该道具模型应该显示
        bool shouldBeVisible = isItemMatch;

        if (anim.gameObject.activeSelf != shouldBeVisible)
        {
            anim.gameObject.SetActive(shouldBeVisible);
            if (shouldBeVisible) anim.transform.localRotation = Quaternion.Euler(facingRotation);
        }

        if (!shouldBeVisible) return;

        // 决定前后
        Vector3 targetPos = GetTargetPos(basePos, isGenderSelected);

        if (immediate)
        {
            anim.transform.localPosition = targetPos;
            anim.transform.localRotation = Quaternion.Euler(facingRotation);
        }
        else
        {
            anim.transform.localPosition = Vector3.Lerp(anim.transform.localPosition, targetPos, Time.deltaTime * lerpSpeed);
        }

        anim.SetBool("IsSelected", isGenderSelected);
    }
    private void Update()
    {
        UpdateAllPositions(false);
    }
    private Vector3 GetTargetPos(Vector3 basePos, bool isSelected)
    {
        float offset = isSelected ? forwardZ : backwardZ;
        return new Vector3(basePos.x, basePos.y, basePos.z + offset);
    }

    // 新增：只在切换时调用的重置方法
    private void ResetAllRotations()
    {
        Quaternion fixedRot = Quaternion.Euler(facingRotation);
        Animator[] all = { witchMale, witchFemale, hunterMale, hunterFemale, 
                           witchMaleCloak, witchFemaleCloak, witchMaleAmulet, 
                           witchFemaleAmulet, witchMaleBroom, witchFemaleBroom };
        foreach (var a in all) if (a != null) a.transform.localRotation = fixedRot;
    }
}