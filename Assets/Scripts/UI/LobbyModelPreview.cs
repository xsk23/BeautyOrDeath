using UnityEngine;
using UnityEngine.UI;
using Mirror;

public class LobbyModelPreview : MonoBehaviour
{
    [Header("UI Buttons")]
    public Button maleButton;
    public Button femaleButton;

    [Header("Models")]
    public Animator witchMale;
    public Animator witchFemale;
    public Animator hunterMale;
    public Animator hunterFemale;

    [Header("Movement Settings")]
    public float forwardZ = -1.5f; 
    public float backwardZ = 1.0f; 
    public float lerpSpeed = 10f; // 增加速度让反馈更即时

    [Header("Rotation Settings")]
    public Vector3 facingRotation = new Vector3(0, 180, 0); // 如果模型背对镜头，修改这里的 Y

    private Vector3 wMaleBase, wFemaleBase, hMaleBase, hFemaleBase;
    private Gender currentGender;

    private void Start()
    {
        // 1. 记录初始位置
        // 建议在编辑器里把这4个模型放在同一个 Z 轴坐标上
        wMaleBase = witchMale.transform.localPosition;
        wFemaleBase = witchFemale.transform.localPosition;
        hMaleBase = hunterMale.transform.localPosition;
        hFemaleBase = hunterFemale.transform.localPosition;

        // 2. 绑定按钮
        maleButton.onClick.AddListener(() => UpdateSelection(Gender.Male));
        femaleButton.onClick.AddListener(() => UpdateSelection(Gender.Female));

        // 3. 初始读取性别并直接应用（不等待 Lerp）
        currentGender = PlayerSettings.Instance.selectedGender;
        ApplySelection(currentGender, true);
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
        
        UpdateAnimatorParams();

        if (immediate)
        {
            // 瞬间移动位置，防止第一帧看到错位
            SetPos(witchMale.transform, wMaleBase, gender == Gender.Male);
            SetPos(witchFemale.transform, wFemaleBase, gender == Gender.Female);
            SetPos(hunterMale.transform, hMaleBase, gender == Gender.Male);
            SetPos(hunterFemale.transform, hFemaleBase, gender == Gender.Female);
        }
    }

    private void Update()
    {
        // 平滑移动逻辑
        MoveModel(witchMale.transform, wMaleBase, currentGender == Gender.Male);
        MoveModel(witchFemale.transform, wFemaleBase, currentGender == Gender.Female);
        MoveModel(hunterMale.transform, hMaleBase, currentGender == Gender.Male);
        MoveModel(hunterFemale.transform, hFemaleBase, currentGender == Gender.Female);
    }

    private void MoveModel(Transform trans, Vector3 basePos, bool isSelected)
    {
        Vector3 target = GetTargetPos(basePos, isSelected);
        trans.localPosition = Vector3.Lerp(trans.localPosition, target, Time.deltaTime * lerpSpeed);
    }

    private void SetPos(Transform trans, Vector3 basePos, bool isSelected)
    {
        trans.localPosition = GetTargetPos(basePos, isSelected);
    }

    private Vector3 GetTargetPos(Vector3 basePos, bool isSelected)
    {
        float offset = isSelected ? forwardZ : backwardZ;
        return new Vector3(basePos.x, basePos.y, basePos.z + offset);
    }

    private void UpdateAnimatorParams()
    {
        SetAnim(witchMale, currentGender == Gender.Male);
        SetAnim(witchFemale, currentGender == Gender.Female);
        SetAnim(hunterMale, currentGender == Gender.Male);
        SetAnim(hunterFemale, currentGender == Gender.Female);
    }

    private void SetAnim(Animator anim, bool isSelected)
    {
        if (anim == null) return;
        anim.SetBool("IsSelected", isSelected);
        // 【关键修复】强制 Animator 立即评估状态，而不是等下一帧
        anim.Update(0); 
    }
    // 新增：只在切换时调用的重置方法
    private void ResetAllRotations()
    {
        Quaternion fixedRot = Quaternion.Euler(facingRotation);
        if (witchMale) witchMale.transform.localRotation = fixedRot;
        if (witchFemale) witchFemale.transform.localRotation = fixedRot;
        if (hunterMale) hunterMale.transform.localRotation = fixedRot;
        if (hunterFemale) hunterFemale.transform.localRotation = fixedRot;
    }
}