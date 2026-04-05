using UnityEngine;
using Mirror;
using System.Collections;

public class HoneySupplyStation : NetworkBehaviour
{
  [Header("模型引用 (必须是同级兄弟节点)")]
  public GameObject fullModel;   // 满蜂蜜的模型
  public GameObject emptyModel;  // 空蜂蜜的模型

  [Header("高亮材质设置")]
  public Material outlineMaterialSource; // 在这里拖入你的 Mat_Outline 材质球
  private Material outlineInstance;
  private Renderer[] fullRenderers;

  // 存储材质数组，用于切换
  private Material[][] originalMaterials;
  private Material[][] highlightedMaterials;

  [Header("刷新设置")]
  public float respawnTime = 30f; // 多少秒后重新变满

  [SyncVar(hook = nameof(OnStateChanged))]
  public bool isEmpty = false;

  private bool isHighlighted = false;

  void Awake()
  {
    // === 完美复刻图1的描边逻辑 ===
    if (fullModel != null)
    {
      fullRenderers = fullModel.GetComponentsInChildren<Renderer>();
      originalMaterials = new Material[fullRenderers.Length][];
      highlightedMaterials = new Material[fullRenderers.Length][];

      if (outlineMaterialSource != null)
      {
        outlineInstance = new Material(outlineMaterialSource);
        // 强制写入图1的完美参数：黄色、透视、极细的描边
        outlineInstance.SetColor("_OutlineColor", Color.yellow);
        outlineInstance.SetFloat("_ZTestMode", 4f);       // 穿透显示
        outlineInstance.SetFloat("_OutlineWidth", 0.03f); // 描边粗细 (解决变成实心黄块的问题)
      }

      for (int i = 0; i < fullRenderers.Length; i++)
      {
        originalMaterials[i] = fullRenderers[i].sharedMaterials;

        if (outlineInstance != null)
        {
          var mats = new Material[originalMaterials[i].Length + 1];
          for (int j = 0; j < originalMaterials[i].Length; j++)
          {
            mats[j] = originalMaterials[i][j];
          }
          mats[mats.Length - 1] = outlineInstance;
          highlightedMaterials[i] = mats;
        }
      }
    }
  }

  public override void OnStartClient()
  {
    UpdateVisuals(isEmpty);
  }

  private void OnStateChanged(bool oldVal, bool newVal)
  {
    UpdateVisuals(newVal);
    // 如果变成了空状态，强制关闭高亮
    if (newVal) SetHighlight(false);
  }

  private void UpdateVisuals(bool emptyState)
  {
    // 显隐模型
    if (fullModel != null) fullModel.SetActive(!emptyState);
    if (emptyModel != null) emptyModel.SetActive(emptyState);
  }

  // 供猎人视线调用的高亮方法
  public void SetHighlight(bool active)
  {
    if (isEmpty || fullRenderers == null || outlineInstance == null) return;
    if (isHighlighted == active) return;

    isHighlighted = active;

    // 切换材质数组实现高亮
    for (int i = 0; i < fullRenderers.Length; i++)
    {
      if (fullRenderers[i] == null) continue;
      fullRenderers[i].materials = active ? highlightedMaterials[i] : originalMaterials[i];
    }
  }

  [Server]
  public void ServerConsume()
  {
    if (isEmpty) return;

    isEmpty = true;
    UpdateVisuals(true); // 服务器也更新一下状态
    StartCoroutine(RespawnRoutine());
  }

  [Server]
  private IEnumerator RespawnRoutine()
  {
    yield return new WaitForSeconds(respawnTime);
    isEmpty = false;
    UpdateVisuals(false); // 服务器也更新一下状态

    // 播放刷新音效
    GameManager.Instance?.ServerPlay3DAt("pop_sound", transform.position);
  }

  void OnDestroy()
  {
    if (outlineInstance != null) Destroy(outlineInstance);
  }
}