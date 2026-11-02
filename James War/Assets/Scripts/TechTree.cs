using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TechTree : MonoBehaviour
{
    public static TechTree Instance;

    [Header("Extra starting unlocks (for testing)")]
    [Tooltip("Prefab list indices unlocked at the start on top of the basic units. Leave empty for normal play")]
    public List<int> playerStartingUnlocks = new List<int>(); // e.g. 0,1,2 for Footman,Scout,AntiVic
    public List<int> enemyStartingUnlocks = new List<int>();

    [Header("Research per day ?")]
    public int researchPerDay = 1;

    [Header("Upgrades")]
    [Tooltip("How much each upgrade level adds to primary attack, secondary attack and defence")]
    public float upgradeBonus = 5f;

    private List<int> playerUnlocked = new List<int>();
    private List<int> enemyUnlocked = new List<int>();

    // Upgrade level per unit category, for each side
    private Dictionary<UnitTypeExtra.UnitCategory, int> playerUpgrades = new Dictionary<UnitTypeExtra.UnitCategory, int>();
    private Dictionary<UnitTypeExtra.UnitCategory, int> enemyUpgrades = new Dictionary<UnitTypeExtra.UnitCategory, int>();

    private int playerResearchLeft = 0;
    private int enemyResearchLeft = 0;
    private GameManager gm;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // Apply starting unlocks
        playerUnlocked.AddRange(playerStartingUnlocks);
        enemyUnlocked.AddRange(enemyStartingUnlocks);
    }

    // Basic units (Is Basic ticked on their unit data) are always unlocked
    public bool IsUnlocked(int index, bool isPlayer)
    {
        UnitTypeExtra data = DataAt(index);
        if (data != null && data.isBasic) return true;
        return isPlayer ? playerUnlocked.Contains(index) : enemyUnlocked.Contains(index);
    }

    // Unit data of the prefab at this index in GameManager's Prefab List, or null
    UnitTypeExtra DataAt(int index)
    {
        if (gm == null) gm = FindFirstObjectByType<GameManager>();
        GameObject prefab = gm != null ? gm.GetPrefabAtIndex(index) : null;
        UnitController unit = prefab != null ? prefab.GetComponent<UnitController>() : null;
        return unit != null ? unit.unitData : null;
    }

    public bool Unlock(int index, bool isPlayer)
    {
        if (isPlayer)
        {
            if (playerResearchLeft <= 0)
            {
                Debug.Log("[TechTree] Player has no research points to spend");
                return false;
            }
            if (IsUnlocked(index, true))
            {
                Debug.Log($"[TechTree] Index {index} already unlocked");
                return false;
            }
            playerUnlocked.Add(index);
            playerResearchLeft--;
            Debug.Log($"Player unlocked unit index {index}");
            return true;
        }
        else
        {
            if (enemyResearchLeft <= 0) return false;
            if (IsUnlocked(index, false)) return false;
            enemyUnlocked.Add(index);
            enemyResearchLeft--;
            Debug.Log($"Enemy unlocked unit index {index}");
            return true;
        }
    }

    // Called by GameManager when a new day begins
    public void GrantDailyResearch()
    {
        playerResearchLeft += researchPerDay;
        enemyResearchLeft += researchPerDay;
        Debug.Log($"Research granted ?player has {playerResearchLeft} research point(s)");
    }

    // A category's upgrade opens once every unit of that category in the Prefab List is unlocked
    public bool CanUpgrade(UnitTypeExtra.UnitCategory category, bool isPlayer)
    {
        if (gm == null) gm = FindFirstObjectByType<GameManager>();
        if (gm == null) return false;

        bool anyInCategory = false;
        for (int i = 0; i < gm.prefabList.Count; i++)
        {
            UnitTypeExtra data = DataAt(i);
            if (data == null || data.unitCategory != category) continue;
            anyInCategory = true;
            if (!IsUnlocked(i, isPlayer)) return false;
        }
        return anyInCategory;
    }

    // Repeatable: each level costs a research point and stacks another upgradeBonus
    public bool Upgrade(UnitTypeExtra.UnitCategory category, bool isPlayer)
    {
        if (!CanUpgrade(category, isPlayer)) return false;
        if (isPlayer)
        {
            if (playerResearchLeft <= 0)
            {
                Debug.Log("[TechTree] Player has no research points to spend");
                return false;
            }
            playerResearchLeft--;
        }
        else
        {
            if (enemyResearchLeft <= 0) return false;
            enemyResearchLeft--;
        }

        Dictionary<UnitTypeExtra.UnitCategory, int> levels = isPlayer ? playerUpgrades : enemyUpgrades;
        levels[category] = GetUpgradeLevel(category, isPlayer) + 1;
        Debug.Log($"{(isPlayer ? "Player" : "Enemy")} upgraded {category} to level {levels[category]}");
        return true;
    }

    public int GetUpgradeLevel(UnitTypeExtra.UnitCategory category, bool isPlayer)
    {
        Dictionary<UnitTypeExtra.UnitCategory, int> levels = isPlayer ? playerUpgrades : enemyUpgrades;
        return levels.TryGetValue(category, out int level) ? level : 0;
    }

    // Added to primary attack, secondary attack and defence of that side's units in the category
    public float GetUpgradeBonus(UnitTypeExtra.UnitCategory category, bool isPlayer)
        => GetUpgradeLevel(category, isPlayer) * upgradeBonus;

    public bool PlayerHasResearch() => playerResearchLeft > 0;
    public bool EnemyHasResearch() => enemyResearchLeft > 0;

    public List<int> GetPlayerUnlocked() => playerUnlocked;
    public List<int> GetEnemyUnlocked() => enemyUnlocked;
}

