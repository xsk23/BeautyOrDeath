using UnityEngine;
using Mirror;
using System.Collections;

public class InvisibilityCloak : WitchItemBase
{
    [Header("斗篷参数")]
    public float duration = 5.0f; // 隐身持续时间
    public float speedMultiplier = 1.5f; // 加速倍率
    public AudioClip witchScreamSound; // 嘲讽音效

    private void Awake()
    {
        isActive = true;
        itemName = "Invisibility Cloak";
        cooldown = 15f;
    }

    public override void OnActivate()
    {
        nextUseTime = Time.time + cooldown;
        WitchPlayer player = GetComponentInParent<WitchPlayer>();
        if (player == null)
        {
            Debug.LogError("InvisibilityCloak: No WitchPlayer found on parent.");
            return;
        }
        Debug.Log($"{player.playerName} is activating Invisibility Cloak.");
        player.CmdUseInvisibilityCloak();
    }

    [Server]
    public void ServerActivateEffect(WitchPlayer player)
    {
        UpdateCooldown();
        Debug.Log($"{player.playerName} activated Invisibility Cloak on server.");
        StartCoroutine(CloakRoutine(player));
        RpcPlayScream(player.transform.position);
    }

    [Server]
    private IEnumerator CloakRoutine(WitchPlayer player)
    {
        float originalSpeed = player.moveSpeed;

        // 1. 设置隐身状态 
        player.isStealthed = true;
        Debug.Log($"{player.playerName} Stealth ON");

        // 2. 加速
        player.moveSpeed *= speedMultiplier;

        Debug.Log($"{player.playerName} used Cloak (Stealth ON)");

        yield return new WaitForSeconds(duration);

        // 3. 恢复状态
        if (player != null)
        {
            player.isStealthed = false;
            player.moveSpeed = originalSpeed;
            Debug.Log($"{player.playerName} Stealth OFF");
        }
    }

    [ClientRpc]
    private void RpcPlayScream(Vector3 pos)
    {
        if (witchScreamSound != null)
            AudioSource.PlayClipAtPoint(witchScreamSound, pos, 1.0f);
    }
}