using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Class : MonoBehaviour
{
    public CommanderData activeCommander;
    public GameObject footmanPrefab;
    public GameManager gm;
    public Button abilityButton; 
    public TextMeshProUGUI abilityButtonText; // optional ?shows ability name/status

    public Animator abilityAnimator;
    public string abilityAnimationTrigger = "CastAbility";
    public float fallbackAnimationDuration = 1.5f;

    public ParticleSystem abilityParticlePrefab;
    public Transform particleSpawnPoint;
    public float fallbackParticleDuration = 1f;

    private bool isPlayingAbilitySequence = false;
    public static bool AbilityPlaying = false; // Cursor checks this to block world input
    public enum Classes
    {
        // Commanding officer, varying buffs, debuffs, and abilities
        TrooperGuy, // Has better troopers, transports are slower, vic are slower. Ability: Summons free footmen on all owned properties(summoning)
        RangedGuy, // Ranged Units has smaller min range, all units have 1 less ammo max. Ability: Ranged units extra range and damage.(combat)
        /* havent made */WaterGuy, // Units Stronger when next to water, Air units weaker. Ability: Halve enemy fuel and ammo, enemy supply can't resupply or heal for 1 day 1 night.(sabotage)
        AirGuy, // Air units more Ammo, Fuel, and attack, Damaged units less effective. Ability: if able, when friendly units are attacked, they attack first (combat)
        /* havent made */TrapGuy, // Trappers more trap ammo, traps deal more damage, units less defence. Ability: for 1 day 1 night, new enemy units made take damage(sabotage)
        NoAbilityGuy, // All units have extra attack and defence, no debuffs. Ability: none
        /* havent made */EconomyGuy, // Gets more money, units weaker. Ability: for 1 day 1 night, your units are stronger(combat)
        /* havent made */StrongGuy, // Units Stronger, Costs more. Ability: Gain money(economy)
        /* havent made */TechGuy, // Gets another tech researched everyday, units weaker. Ability: Creates unique unit at headquarters (prototype) (summoning)
        /* havent made */NotDieGuy, // Penalty for damaged units in combat reduced, units have less max health. Ability: units health can't be reduced to 1 (combat)
        /* havent made */StealthGuy, // extra attack and mobility at night, weaker non-ranged units at day. Ability: invisible units extra damage, cannot be seen by detection (combat)
        InfoGuy, // all units extra vision at night, bases have 10 less defence. Ability: if night, whole map revealed, if day, friendly unit type and health hidden, lasts 1 day 1 night (combat)
        /* havent made */FightEnemyGuy, // units heal 2 hp after defeating an enemy unit, All units slower. Ability: units leave burning tiles on floor for 1 day 1 night, gives no tile defence and deals 2 damage to passing units.(combat)
    }

    // Start is called before the first frame update
    void Start()
    {
        gm = FindFirstObjectByType<GameManager>();
        AbilityPlaying = false; // static, so it would carry over if a scene changed mid-ability
        if (GameSettings.PlayerCommander != null) activeCommander = GameSettings.PlayerCommander;
    }

    // Update is called once per frame
    void Update()
    {
        UpdateAbilityButton();
    }

    void UpdateAbilityButton()
    {
        if (abilityButton == null) return;

        bool furyReady = CommanderManager.Instance != null && CommanderManager.Instance.PlayerAbilityReady();
        bool playerTurn = gm != null && gm.IsPlayerTurn;
        abilityButton.interactable = furyReady && !isPlayingAbilitySequence && playerTurn;

        if (abilityButtonText != null && activeCommander != null)
        {
            abilityButtonText.text = isPlayingAbilitySequence
                ? $"{activeCommander.abilityName}..."
                : furyReady
                    ? $"{activeCommander.abilityName} — READY!"
                    : $"{activeCommander.abilityName} ({CommanderManager.Instance?.playerResource ?? 0}/{CommanderManager.Instance?.PlayerResourceMax ?? 0})";
        }
    }

    public void UseAbility()
    {
        Debug.Log($"[Ability] UseAbility called | activeCommander={activeCommander?.commanderName ?? "NULL"}");

        if (isPlayingAbilitySequence)
        {
            Debug.Log("[Ability] Already playing — ignoring extra click");
            return;
        }
        if (activeCommander == null)
        {
            Debug.Log("[Ability] No activeCommander assigned on Class.cs — aborting");
            return;
        }
        if (CommanderManager.Instance == null)
        {
            Debug.Log("[Ability] CommanderManager.Instance is null — aborting");
            return;
        }
        if (!CommanderManager.Instance.PlayerAbilityReady())
        {
            Debug.Log("[Ability] Not ready yet");
            return;
        }
        if (gm != null && !gm.IsPlayerTurn)
        {
            Debug.Log("[Ability] Can't use abilities during the enemy turn");
            return;
        }

        StartCoroutine(PlayAbilitySequence());
    }

    IEnumerator PlayAbilitySequence()
    {
        isPlayingAbilitySequence = true;
        AbilityPlaying = true;

        yield return StartCoroutine(PlayAbilityAnimation());
        yield return StartCoroutine(PlayAbilityParticles());

        bool success = ExecuteAbilityLogic();

        if (success)
        {
            CommanderManager.Instance.ConsumePlayerAbility();
            Debug.Log("[Ability] Fury consumed after successful use");
        }
        else
        {
            Debug.LogWarning("[Ability] Ability did not execute — Fury NOT consumed");
        }

        isPlayingAbilitySequence = false;
        AbilityPlaying = false;
    }
    IEnumerator PlayAbilityAnimation()
    {
        if (abilityAnimator != null)
        {
            abilityAnimator.SetTrigger(abilityAnimationTrigger);
            yield return null; // let the Animator enter the new state before reading its length
            float length = abilityAnimator.GetCurrentAnimatorStateInfo(0).length;
            yield return new WaitForSeconds(length);
        }
        else
        {
            Debug.Log("[Ability] No animator assigned yet — using placeholder wait");
            yield return new WaitForSeconds(fallbackAnimationDuration);
        }
    }
    IEnumerator PlayAbilityParticles()
    {
        if (abilityParticlePrefab != null)
        {
            Vector3 spawnPos = particleSpawnPoint != null ? particleSpawnPoint.position : transform.position;
            ParticleSystem instance = Instantiate(abilityParticlePrefab, spawnPos, Quaternion.identity);
            instance.Play();

            float duration = instance.main.duration + instance.main.startLifetime.constantMax;
            Destroy(instance.gameObject, duration + 0.5f);
            yield return new WaitForSeconds(duration);
        }
        else
        {
            Debug.Log("[Ability] No particle prefab assigned yet — using placeholder wait");
            yield return new WaitForSeconds(fallbackParticleDuration);
        }
    }
    bool ExecuteAbilityLogic()
    {
        if (activeCommander.commanderName == "TrooperGuy")
            return TrooperGuyAbility(true);
        if (activeCommander.commanderName == "NoAbilityGuy")
        {
            Debug.Log("[Ability] This commander has no ability");
            return false;
        }
        Debug.LogWarning($"[Ability] No ability implemented for commanderName '{activeCommander.commanderName}'");
        return false;
    }

    // Returns true if the ability went off (and its Fury was spent)
    public bool UseEnemyAbility(string commanderName)
    {
        if (CommanderManager.Instance == null) return false;
        if (!CommanderManager.Instance.EnemyAbilityReady()) return false;

        bool success = false;
        if (commanderName == "TrooperGuy")
            success = TrooperGuyAbility(false);
        else
            Debug.Log($"[Ability] No enemy ability implemented for '{commanderName}' yet");

        if (success)
            CommanderManager.Instance.ConsumeEnemyAbility();
        return success;
    }

    // For the AI's summoning decision: how many units the ability would summon right now,
    // and the most it could summon once that side's own units step off its properties
    public void GetSummonCount(string commanderName, bool forPlayer, out int now, out int best)
    {
        now = 0;
        best = 0;
        if (commanderName != "TrooperGuy" || footmanPrefab == null) return;

        foreach (TileFunction tile in SummonTiles(forPlayer))
        {
            UnitController standing = GridUtil.UnitAt(tile.transform.position);
            if (standing == null) now++;
            bool blockedByOpponent = standing != null && (forPlayer ? standing.isEnemy : (standing.isPlayer || standing.isAlly));
            if (!blockedByOpponent) best++;
        }
    }

    // For the AI's sabotage decision: roughly how many funds' worth the ability would deny the opponent right now.
    // No sabotage ability exists yet (WaterGuy, TrapGuy), so this is where their estimates go
    public float EstimateDenial(string commanderName, bool forPlayer)
    {
        return 0f;
    }

    // Every property the side owns (HQ, base of any kind, or city) that a footman can stand on.
    // Used by both the ability and the AI's count, so the AI's estimate always matches what the ability does
    List<TileFunction> SummonTiles(bool forPlayer)
    {
        List<TileFunction> result = new List<TileFunction>();
        UnitController footman = footmanPrefab != null ? footmanPrefab.GetComponent<UnitController>() : null;
        UnitTypeExtra footmanData = footman != null ? footman.unitData : null;

        foreach (TileFunction tile in FindObjectsByType<TileFunction>(FindObjectsSortMode.None))
        {
            bool property = tile.isCaptureable || tile.isBase || tile.isHQ;
            bool owned = forPlayer ? tile.ownedByPlayer : tile.ownedByEnemy;
            if (property && owned && tile.CanUnitEnter(footmanData)) result.Add(tile);
        }
        return result;
    }

    #region Commander Abilities
    // Summons a free footman on every one of the side's empty properties (HQ, bases and cities)
    bool TrooperGuyAbility(bool forPlayer)
    {
        if (footmanPrefab == null)
        {
            Debug.LogWarning("[Ability] footmanPrefab not assigned on Class.cs");
            return false;
        }

        int spawnedCount = 0;
        foreach (TileFunction tile in SummonTiles(forPlayer))
        {
            if (GridUtil.IsOccupied(tile.transform.position)) continue; // no stacking

            GameObject instance = Instantiate(footmanPrefab, tile.transform.position, Quaternion.identity);
            UnitController unit = instance.GetComponent<UnitController>();

            if (unit != null)
            {
                unit.SetSide(forPlayer, false, !forPlayer);
                gm.RegisterUnit(unit);
                spawnedCount++;
                Debug.Log($"[Ability] Footman spawned at {tile.gameObject.name}");
            }
        }

        if (spawnedCount == 0)
            Debug.LogWarning("[Ability] No empty property on that side — nothing spawned");

        return spawnedCount > 0;
    }
    #endregion
}
