using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using System.Linq;
using Unity.Mathematics;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
#region Ints/Floats/Bools/etc
    public TextMeshProUGUI moneyText;
    public TextMeshProUGUI dayNightText;
    public TextMeshProUGUI furyText;
    public GameObject GetPrefabAtIndex(int index)
    {
        if (index >=0 && index < prefabList.Count)
        {
            return prefabList[index];
        }
        return null;
    }    
    public bool CanAfford(int cost)
    {
        return money >= cost;
    }
    public bool EnemyCanAfford(int cost)
    {
        return enemyMoney >= cost;
    }

    public bool IsDay => isDay;
    public int Day => dayTracker;
    public int EnemyMoney => enemyMoney;
    public bool IsPlayerTurn => isPlayerTurn;

    public List<GameObject> prefabList;

    [Header("Starting funds")]
    [Tooltip("Money each side has on day 1, before any income")]
    public int playerStartingFunds = 5000;
    public int enemyStartingFunds = 5000;

    [Header("Resupply")]
    [Tooltip("HP a unit heals each turn it's resupplied, by a supply unit or by standing on its own side's property")]
    public float resupplyHeal = 10f;

    [Header("Second player head start")]
    [Tooltip("Moving first is an advantage, so the side moving second starts with a free unit on its HQ, ready to move")]
    public bool secondPlayerFreeUnit = true;
    [Tooltip("Which unit to give. Leave empty to use the basic infantry (Footman) from the Prefab List")]
    public GameObject secondPlayerFreeUnitPrefab;

    List<UnitController> unitCount = new List<UnitController>();

    private int baseNumber;
    private int enemyBaseNumber;
    private int allyBaseNumber;
    private int money;
    private int enemyMoney;
    private int allyMoney;
    private int dayTracker;
    private int numberOfPlayers;
    private int dayNightCounter;
    public int useMoney => money;


    private bool playerHQAlive;
    private bool enemyHQAlive;

    private bool isDay;
    private bool isPlayerTurn;
    #endregion

    // Start is called before the first frame update
    void Start()
    {
        //GameObject obj = GameObject.Find("Cursor");
        //cursor = obj.GetComponent<Cursor>();
        money = playerStartingFunds;
        enemyMoney = enemyStartingFunds;
        isDay = true;
        isPlayerTurn = true;
        baseNumber = 0;
        enemyBaseNumber = 0;
        dayTracker = 1;
        playerHQAlive = true;
        enemyHQAlive = true;
        dayNightText.text = ($"Day {dayTracker}");

        CountStartingBases();
        GiveSecondPlayerHeadStart();

        FogOfWar fog = FogOfWar.Instance;
        if (fog != null) fog.RefreshVisibility();
    }

    // Update is called once per frame
    void Update()
    {
        moneyText.text = ($"Funds - {money}");
        if (furyText != null && CommanderManager.Instance != null)
            furyText.text = $"Fury - {CommanderManager.Instance.playerResource}" +
                            $"/{CommanderManager.Instance.PlayerResourceMax}";
    }
#region Property Interactions

    public void PlayerLostHQ()
    {
        playerHQAlive = false; CheckWinLose(); 
    }
    // The side moving second (the enemy) starts with a free unit on its HQ, ready to move on its first turn.
    // If the HQ is taken it goes on a free tile next to it; with no HQ on the map it uses one of the enemy's bases
    void GiveSecondPlayerHeadStart()
    {
        if (!secondPlayerFreeUnit) return;

        GameObject prefab = secondPlayerFreeUnitPrefab != null ? secondPlayerFreeUnitPrefab : BasicInfantryPrefab();
        UnitController template = prefab != null ? prefab.GetComponent<UnitController>() : null;
        if (template == null)
        {
            Debug.LogWarning("[Head start] No unit to give: set Second Player Free Unit Prefab, or put the Footman in the Prefab List");
            return;
        }

        TileFunction hq = null;
        TileFunction backupBase = null;
        foreach (TileFunction tile in FindObjectsByType<TileFunction>(FindObjectsSortMode.None))
        {
            if (tile.isHQ && tile.isEnemyHQ) hq = tile;
            else if (backupBase == null && tile.ownedByEnemy && tile.GetComponent<UnitSpawner>() != null) backupBase = tile;
        }
        TileFunction home = hq != null ? hq : backupBase;
        if (home == null)
        {
            Debug.LogWarning("[Head start] The enemy has no HQ or base to put its free unit on");
            return;
        }
        if (hq == null) Debug.LogWarning("[Head start] No enemy HQ on the map, so the free unit goes on an enemy base instead");

        Vector2? spot = FreeSpotOnOrBeside(home.transform.position, template.unitData);
        if (spot == null)
        {
            Debug.LogWarning($"[Head start] {home.name} and the tiles around it are all taken, so no free unit");
            return;
        }

        UnitController unit = Instantiate(prefab, spot.Value, Quaternion.identity).GetComponent<UnitController>();
        unit.SetSide(false, false, true);
        RegisterUnit(unit);
        Debug.Log($"[Head start] Enemy gets a free {prefab.name} at {spot.Value} for moving second");
    }

    // First basic infantry unit in the Prefab List (the Footman)
    GameObject BasicInfantryPrefab()
    {
        foreach (GameObject prefab in prefabList)
        {
            UnitController unit = prefab != null ? prefab.GetComponent<UnitController>() : null;
            UnitTypeExtra data = unit != null ? unit.unitData : null;
            if (data != null && data.isBasic && data.unitCategory == UnitTypeExtra.UnitCategory.Trooper && data.canCapture)
                return prefab;
        }
        return null;
    }

    // The given tile if it's free, otherwise a free neighbouring tile the unit can stand on
    Vector2? FreeSpotOnOrBeside(Vector2 pos, UnitTypeExtra data)
    {
        foreach (Vector2 offset in new[] { Vector2.zero, Vector2.up, Vector2.down, Vector2.left, Vector2.right })
        {
            Vector2 spot = pos + offset;
            TileFunction tile = GridUtil.TileAt(spot);
            if (tile != null && tile.CanUnitEnter(data) && !GridUtil.IsOccupied(spot)) return spot;
        }
        return null;
    }

    void CountStartingBases()
    {
        // Every owned property earns income (bases, HQs, other capturables), the same as capturing one adds to it
        TileFunction[] allTiles = FindObjectsByType<TileFunction>(FindObjectsSortMode.None);
        foreach (TileFunction tile in allTiles)
        {
            if (!tile.isCaptureable && !tile.isBase && !tile.isHQ) continue;
            if (tile.ownedByPlayer) baseNumber++;
            else if (tile.ownedByEnemy) enemyBaseNumber++;
        }
        Debug.Log($"Starting bases — Player: {baseNumber} | Enemy: {enemyBaseNumber}");
    }
    public void EnemyLostHQ()
    {
        enemyHQAlive = false; CheckWinLose(); 
    }

    public void GetBase()
    {
        baseNumber++;
    }
    public void LoseBase()
    {
        baseNumber--;
    }
    public void EnemyGetBase() { enemyBaseNumber++; }
    public void EnemyLoseBase() { enemyBaseNumber--; }
    void ProcessCaptures(bool playerCaptures)
    {
        // Captures in progress are lost if the capturing unit moved away or died
        foreach (TileFunction tile in FindObjectsByType<TileFunction>(FindObjectsSortMode.None))
            tile.ResetIfCapturerLeft();

        foreach (UnitController unit in unitCount)
        {
            if (unit == null) continue;
            if (unit.unitData == null || !unit.unitData.canCapture) continue;
            if (unit.isLoaded) continue; // inside a transport, not standing on the tile

            // Only process the right faction
            if (playerCaptures && !unit.isPlayer && !unit.isAlly) continue;
            if (!playerCaptures && !unit.isEnemy) continue;

            // Find capturable tile at unit's position
            Collider2D[] hits = Physics2D.OverlapPointAll(unit.transform.position);
            foreach (Collider2D hit in hits)
            {
                TileFunction tile = hit.GetComponent<TileFunction>();
                if (tile != null && tile.isCaptureable)
                {
                    tile.AttemptCapture(unit);
                    break;
                }
            }
        }
    }
    void ProcessSupply(bool playerSupplies)
    {
        Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

        foreach (UnitController supplier in unitCount)
        {
            if (supplier == null) continue;
            if (!supplier.CanSupplyOthers()) continue;
            if (supplier.isLoaded) continue; // a supplier parked inside a transport can't reach anyone

            bool supplierOnSide = playerSupplies ? (supplier.isPlayer || supplier.isAlly) : supplier.isEnemy;
            if (!supplierOnSide) continue;

            Vector2 supplierPos = supplier.transform.position;

            foreach (Vector2 dir in dirs)
            {
                Collider2D[] hits = Physics2D.OverlapPointAll(supplierPos + dir);
                foreach (Collider2D hit in hits)
                {
                    UnitController other = hit.GetComponent<UnitController>();
                    if (other == null || other == supplier) continue;

                    bool sameTeam = playerSupplies ? (other.isPlayer || other.isAlly) : other.isEnemy;
                    if (!sameTeam) continue;

                    other.ReceiveSupply(10, resupplyHeal);
                }
            }
        }

        // Units standing on one of their side's own properties (bases, HQ) are resupplied the same way
        foreach (UnitController unit in unitCount)
        {
            if (unit == null || unit.isLoaded) continue;
            bool onSide = playerSupplies ? (unit.isPlayer || unit.isAlly) : unit.isEnemy;
            if (!onSide) continue;

            TileFunction tile = GridUtil.TileAt(unit.transform.position);
            if (tile == null || !tile.isCaptureable) continue;
            if (playerSupplies ? tile.ownedByPlayer : tile.ownedByEnemy)
                unit.ReceiveSupply(10, resupplyHeal);
        }
    }

    // Air units burn fuel at the end of their side's turn (runs after supply, so a refuel can save them)
    void ProcessFuel(bool playerSide)
    {
        // Loop over a copy: units that crash unregister themselves
        foreach (UnitController unit in new List<UnitController>(unitCount))
        {
            if (unit == null) continue;
            bool onSide = playerSide ? (unit.isPlayer || unit.isAlly) : unit.isEnemy;
            if (onSide) unit.BurnTurnFuel();
        }
    }

    #endregion
    #region debug tools
    public void GetRich(InputAction.CallbackContext context)
    {
        if (context.performed)
            money += 1000;
    }
    public void timeTravel(InputAction.CallbackContext context)
    {
        if (isDay && (context.performed))
        {
            Debug.Log($"Day {dayTracker} is over");
        }
        else if (!isDay && (context.performed))
        {
            Debug.Log($"Night {dayTracker} is over");
        }
    }
    #endregion
#region Money, and Time
    public void AddMoney(int amount)
    {
        money += amount;
    }

    public void EnemyAddMoney(int amount)
    {
        enemyMoney += amount;
    }
    public void TurnEnd()
    {
        if (gameOver) return; // won or lost: the map is about to restart
        unitCount.RemoveAll(u => u == null);
        Debug.Log($"TurnEnd called - {unitCount.Count} units registered");
        if (isPlayerTurn)
        {
            ProcessCaptures(true);
            ProcessSupply(true);
            ProcessFuel(true);
            unitCount.Where(u => u.isPlayer || u.isAlly).ToList().ForEach(u => u.ClearTint()); // tints only show on your own turn
            isPlayerTurn = false;
            unitCount.Where(u => u.isEnemy).ToList().ForEach(u => u.NextTurn());

            EnemyAi ai = FindFirstObjectByType<EnemyAi>();
            if (ai != null)
                StartCoroutine(ai.EnemyTurn());
            else
            {
                Debug.LogWarning("No EnemyAI — skipping enemy turn");
                isPlayerTurn = true;
                AdvancePhase();
            }
        }
        else
        {
            ProcessCaptures(false);
            ProcessSupply(false);
            ProcessFuel(false);
            unitCount.Where(u => u.isEnemy).ToList().ForEach(u => u.ClearTint());
            isPlayerTurn = true;
            AdvancePhase();
            unitCount.Where(u => u.isPlayer || u.isAlly).ToList().ForEach(u => u.NextTurn());
        }

        CheckWinLose();
    }

    void AdvancePhase()
    {
        FogOfWar fog = FogOfWar.Instance;
        if (isDay)
        {
            isDay = false;
            dayNightText.text = $"Night {dayTracker}";
            Debug.Log($"Night {dayTracker} begins");
            if (fog != null) fog.UpdateFog(true);
        }
        else
        {
            isDay = true;
            dayTracker++;
            dayNightText.text = $"Day {dayTracker}";
            AddMoney(1000 * baseNumber);
            EnemyAddMoney(1000 * enemyBaseNumber);
            Debug.Log($"Day {dayTracker} begins");
            if (fog != null) fog.UpdateFog(false);

            // Research is granted once per full day, at the Night -> Day transition
            if (TechTree.Instance != null)
            {
                TechTree.Instance.GrantDailyResearch();
                Cursor cursor = FindFirstObjectByType<Cursor>();
                if (cursor != null) cursor.OpenResearchMenu();
            }
        }
    }

    #endregion
        #region Win/Loss
    public void CheckWinLose()
    {
        bool playerHasUnits = unitCount.Any(u => u != null && (u.isPlayer || u.isAlly));
        bool enemyHasUnits = unitCount.Any(u => u != null && u.isEnemy);

        // HQ loss — immediate regardless of day
        if (!playerHQAlive)
        {
            Debug.Log("DEFEAT — Player HQ captured!");
            TriggerLose();
            return;
        }
        if (!enemyHQAlive)
        {
            Debug.Log("VICTORY — Enemy HQ captured!");
            TriggerWin();
            return;
        }

        // Unit wipeout — only after day 3 to avoid instant loss at game start
        if (dayTracker >= 3)
        {
            if (!playerHasUnits)
            {
                Debug.Log("DEFEAT — All player units destroyed!");
                TriggerLose();
                return;
            }
            if (!enemyHasUnits)
            {
                Debug.Log("VICTORY — All enemy units destroyed!");
                TriggerWin();
                return;
            }
        }
    }

    private bool gameOver = false; // once someone has won, turns stop (see TurnEnd)
    void TriggerWin()
    {
        gameOver = true;
        Time.timeScale = 0f;
        Debug.Log("=== VICTORY ===");
    }
    void TriggerLose()
    {
        gameOver = true;
        Time.timeScale = 0f;
        Debug.Log("=== DEFEAT ===");
    }

    #endregion
#region Unit Interactions
    public void RegisterUnit(UnitController unit)
    {
        if (!unitCount.Contains(unit))
        {
            unitCount.Add(unit);

            // Fog only refreshed when a unit moved or night fell, so a newly built, summoned or given unit saw nothing
            // until it moved (and an enemy one appearing at night stayed visible). Every new unit passes through here
            FogOfWar fog = FogOfWar.Instance;
            if (fog != null) fog.RefreshVisibility();
        }
    }

    public void UnregisterUnit(UnitController unit)
    {
        unitCount.Remove(unit);
    }
    #endregion
}
