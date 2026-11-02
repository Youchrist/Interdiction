using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using Unity.VisualScripting;
using System.Security.Cryptography.X509Certificates;
using Unity.Multiplayer.Center.Common;
using TMPro;
public class UnitController : MonoBehaviour
{
#region Ints/Floats/Bools/etc
    public UnitTypeExtra unitData;
    public GameObject trapPrefab;
    public TextMeshPro healthText;

    public int GetMoveRange()
    {
        if (unitData == null)
        {
            return 1;
        }
        int bonus = CommanderManager.Instance != null ? CommanderManager.Instance.GetMoveBonus(unitData.unitCategory, isPlayer) : 0;
        return Mathf.Max(1, unitData.moveRange + bonus);
    }
    public int GetFuelMax()
    {
        if (unitData == null) return 0;
        if (unitData.fuelCap < 0) return -1; // -1 = infinite fuel, never modified

        int bonus = CommanderManager.Instance != null
            ? CommanderManager.Instance.GetMaxFuelBonus(unitData.unitCategory, isPlayer)
            : 0;
        return unitData.fuelCap + bonus;
    }
    public int GetFuel() => fuel;

    // Tiles this unit can move right now: its move range, limited by fuel (-1 fuel = unlimited)
    public int GetMoveBudget()
    {
        int range = GetMoveRange();
        if (unitData == null || fuel < 0) return range;
        return Mathf.Min(range, fuel);
    }

    // End-of-turn upkeep: air units burn 2 fuel and crash when they run out
    public void BurnTurnFuel()
    {
        if (unitData == null || !unitData.isAir || fuel < 0) return;
        if (isLoaded) return; // parked on a carrier
        fuel = Mathf.Max(0, fuel - 2);
        if (fuel > 0) return;

        Debug.Log($"{gameObject.name} ran out of fuel and crashed");
        health = 0;
        UpdateHealthDisplay();
        DestroyCargo();
        gm.UnregisterUnit(this); // removed right away so this turn's win/lose check doesn't count it
    }
    public bool CanSupplyOthers() => unitData != null && unitData.canSupply;

    public int GetAttackRangeMin()
    {
        if (unitData == null) return 1;
        int bonus = CommanderManager.Instance != null
            ? CommanderManager.Instance.GetMinRangeBonus(unitData.unitCategory, isPlayer)
            : 0;
        return Mathf.Max(1, unitData.attackRangeMin + bonus);
    }
    public int GetAttackRangeMax()
    {
        if (unitData == null) return 1;
        int bonus = CommanderManager.Instance != null
            ? CommanderManager.Instance.GetMaxRangeBonus(unitData.unitCategory, isPlayer)
            : 0;
        return Mathf.Max(1, unitData.attackRangeMax + bonus);
    }

    public bool IsInvisibleAndUndetected(bool checkingFromPlayerSide)
    {
        if (unitData == null || !unitData.isInvis) return false;

        UnitController[] allUnits = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
        foreach (UnitController u in allUnits)
        {
            bool onCheckingSide = checkingFromPlayerSide ?  (u.isPlayer || u.isAlly) : u.isEnemy;
            if (!onCheckingSide) continue;
            if (u.unitData == null || !u.unitData.canSeeInvis) continue;
            
            float dist = Mathf.Abs (u.transform.position.x - transform.position.x) + Mathf.Abs (u.transform.position.y - transform.position.y);
            if (dist <= u.GetVisionRange()) return false;
        }
        return true;
    }
    public int GetAmmo()
    {

        if (unitData == null) return 0;
        int bonus = CommanderManager.Instance != null
            ? CommanderManager.Instance.GetMaxAmmoBonus(unitData.unitCategory, isPlayer)
            : 0;
        return Mathf.Max(1, unitData.ammoCap + bonus);
    }
    public float GetHealth() => unitData != null ? unitData.health : 50f;
    public float GetDamageMultiplier() => GetDamageMultiplierAt(health);

    // Fighting strength at a given HP: steps of 20%, so 61-80% health hits at 80%. 0 once destroyed.
    // The AI uses it to value a hit by how much strength it takes away
    public float GetDamageMultiplierAt(float hp)
    {
        if (hp <= 0) return 0f;
        float healthPercent100 = hp / healthMax * 100f; // 0-100 scale
        int bracket = Mathf.Max(1, Mathf.CeilToInt(healthPercent100 / 20f)); // round UP to next 20% bracket
        bracket = Mathf.Min(bracket, 5); // cap at full-health bracket
        return bracket * 0.2f;
    }

    public int GetVisionRange()
    {
        if (unitData == null) return 1;
        int bonus = CommanderManager.Instance != null
            ? CommanderManager.Instance.GetVisionBonus(unitData.unitCategory, isPlayer)
            : 0;
        return unitData.visionRange + bonus;
    }
    public float GetAttackPower()
    {
        if (unitData == null) return 10f;
        float mult = CommanderManager.Instance != null
            ? CommanderManager.Instance.GetAttackMult(unitData.unitCategory, isPlayer)
            : 1f;
        return Mathf.Max(1, (unitData.primaryAttackPower + GetUpgradeBonus()) * mult);
    }
    // Flat bonus from this side's repeatable category upgrade in the tech tree
    float GetUpgradeBonus()
    {
        if (unitData == null || TechTree.Instance == null) return 0f;
        return TechTree.Instance.GetUpgradeBonus(unitData.unitCategory, isPlayer || isAlly);
    }
    public float GetAttackPower2()
    {
        if (unitData == null) return 10f;
        float mult = CommanderManager.Instance != null
            ? CommanderManager.Instance.GetAttackMult(unitData.unitCategory, isPlayer)
            : 1f;
        return Mathf.Max(1, (unitData.secondaryAttackPower + GetUpgradeBonus()) * mult);
    }
    public float GetDefence()
    {
        if (unitData == null) return 10f;
        float mult = CommanderManager.Instance != null
            ? CommanderManager.Instance.GetDefenceMult(unitData.unitCategory, isPlayer)
            : 1f;
        return Mathf.Max(1, (unitData.defencePower + GetUpgradeBonus()) * mult);
    }
    public int GetCurrentTileDefence()
    {
        // Air units never get tile defence
        if (unitData != null && unitData.isAir) return 0;

        Collider2D[] hits = Physics2D.OverlapPointAll(transform.position);
        foreach (Collider2D hit in hits)
        {
            TileFunction tile = hit.GetComponent<TileFunction>();
            if (tile != null)
                return tile.GetTileDefence(unitData);
        }
        return 0; // no tile found
    }
    public bool CanAttackTarget(UnitController target)
    {
        return CanAttack() && CanHitTarget(target);
    }
    // The player's chosen weapon is the one that fires, so that weapon has to be able to hit the target
    public bool CanAttackTargetWith(UnitController target, AttackType weapon)
    {
        return CanAttackTarget(target) && WeaponHits(weapon, target);
    }
    // Ignores whether this unit already acted, so defenders can counter on the other side's turn
    public bool CanHitTarget(UnitController target)
    {
        if (unitData == null) return false;
        if (target.isPlayer == isPlayer) return false; // can't attack own team
        if (target.isLoaded) return false; // tucked inside a transport
        return WeaponHits(AttackType.Primary, target) || WeaponHits(AttackType.Secondary, target);
    }
    public List<UnitController> cargo = new List<UnitController>();
    public bool isLoaded = false;
    private Collider2D col;

    private bool isActiveUnit = false;
    private SpriteRenderer spriteRenderer;
    private TeamColorApplier teamColorApplier;
    private bool hasMoved;
    private bool hasAttacked;
    public enum AttackType {Primary, Secondary}
        
    [Header("Unit Status")]
    private bool isAirUnit;
    private bool isGroundUnit;
    private bool isWaterUnit;
    public bool isPlayer;
    public bool isAlly;
    public bool isEnemy;

    public int ammo;
    public float moveSpeed = 8f;
    private int ammoMax;
    private int fuel;
    private int fuelMax;
    private int vision;
    private int range;

    private float health;
    private float healthMax;
    private float unitPower;
    private float unitDefence;
    
    public float changeHealth => health;
    public float GetHealthPercent()
    {
        return health / healthMax;
    }
    public bool IsDead => health <= 0;
    private GameManager gm;
    #endregion
    // Start is called before the first frame update
    private void Start()
    {
        gm.RegisterUnit(this);
        Debug.Log($"{gameObject.name} registered with GameMAnger");
    }
    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        teamColorApplier = GetComponent<TeamColorApplier>();
        RefreshTeamColor();
        col = GetComponent<Collider2D>();
        if (isAlly || isPlayer)
        {
            spriteRenderer.flipX = true;
        }
        ammoMax = GetAmmo();
        ammo = ammoMax;
        fuelMax = GetFuelMax();
        fuel = fuelMax;
        healthMax = GetHealth();
        health = healthMax;
        UpdateHealthDisplay();
        isActiveUnit = false;
        hasAttacked = false; // Set to True later
        hasMoved = false; // Set to True Later
        GameObject GameManager = GameObject.Find("GameManager");
        gm = GameManager.GetComponent<GameManager>();
    }
    // Update is called once per frame
    void Update()
    {  
        if(health <= 0)
        {
            Debug.Log($"{gameObject.name}Dead");
            DestroyCargo();
            gm.UnregisterUnit(this);
            Destroy(gameObject);
        }
    }
    // Everything aboard dies with its transport, including what carried transports hold
    void DestroyCargo()
    {
        foreach (UnitController c in cargo)
        {
            if (c == null) continue;
            c.DestroyCargo();
            Debug.Log($"{c.gameObject.name} was destroyed along with {gameObject.name}");
            gm.UnregisterUnit(c);
            Destroy(c.gameObject);
        }
        cargo.Clear();
    }
    // Total price of everything aboard, including what carried transports hold
    int CargoValue()
    {
        int total = 0;
        foreach (UnitController c in cargo)
            if (c != null && c.unitData != null) total += c.unitData.price + c.CargoValue();
        return total;
    }
#region On-Map functions
    public bool IsVisibleToPlayer { get; private set; } = true; // kept up to date by FogOfWar
    public bool WasAmbushed { get; private set; }

    public void SetVisibility(bool visible)
    {
        IsVisibleToPlayer = visible;
        spriteRenderer.enabled = visible;
        if (healthText != null) healthText.enabled = visible;
        if (typeLabel != null) typeLabel.enabled = visible;
    }
    void UpdateHealthDisplay()
    {
        if (healthText == null) return;
        healthText.text = Mathf.CeilToInt(health).ToString();
    }
    public void SpawnIn()
    {
        hasMoved = true;
        hasAttacked = true;
        RefreshTint();
        RefreshTeamColor();
    }

    // Turn-state tint: green while selected, yellow once moved with its action still unused,
    // dark once it has done everything this turn, white when it hasn't done anything yet
    static readonly Color DoneTint = new Color(0.35f, 0.35f, 0.35f);
    void RefreshTint()
    {
        if (spriteRenderer == null) return;
        if (isActiveUnit) spriteRenderer.color = Color.green;
        else if (hasMoved && hasAttacked) spriteRenderer.color = DoneTint;
        else if (hasMoved || hasAttacked) spriteRenderer.color = Color.yellow;
        else spriteRenderer.color = Color.white;
    }
    // Called when this side's turn ends, so the tints only show during the side's own turn
    public void ClearTint()
    {
        if (spriteRenderer != null) spriteRenderer.color = Color.white;
    }
    public bool HasMoved => hasMoved;
    public bool CanAct()
    {
        return !hasMoved || !hasAttacked;
    }

    public bool CanAttack()
    {
        if (hasAttacked)
        {
            return false;
        }
        if (unitData != null && unitData.isRanged && hasMoved)
        {
            return false;
        }
        return true;

    }
    public void SetSelected(bool selected)
    {
        isActiveUnit = selected;
        RefreshTint();
    }
    public Coroutine MoveTo(Vector2 targetPos, System.Action onComplete = null)
    {
        WasAmbushed = false;
        List<Vector2> path = FindPath(targetPos);
        if (path == null)
        {
            Debug.Log($"{gameObject.name} could not find a valid path to {targetPos}");
            onComplete?.Invoke();
            return null;
        }
        hasMoved = true;
        RefreshTint();
        return StartCoroutine(MoveAlongPath(path, onComplete));
    }
    public Coroutine MoveAlongExplicitPath(List<Vector2> waypoints, System.Action onComplete = null)
    {
        WasAmbushed = false;
        if (waypoints == null || waypoints.Count == 0)
        {
            onComplete?.Invoke();
            return null;
        }

        hasMoved = true;
        RefreshTint();
        return StartCoroutine(MoveAlongPath(waypoints, onComplete));
    }
    // Shortest route to destination; among equally short routes, the one with the fewest turns
    public List<Vector2> FindPath(Vector2 destination)
    {
        Vector2 start = transform.position;
        if (start == destination) return new List<Vector2>();

        int moveRange = GetMoveBudget();
        Vector2[] directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

        // Each search state is a tile plus the direction it was entered from, so turns are counted exactly.
        // Priority = steps + 0.01 per turn, which keeps shortest routes first and breaks ties by fewest turns.
        var steps = new Dictionary<(Vector2 pos, int dir), int>();
        var priority = new Dictionary<(Vector2 pos, int dir), float>();
        var cameFrom = new Dictionary<(Vector2 pos, int dir), (Vector2 pos, int dir)>();
        var frontier = new List<(Vector2 pos, int dir)>();

        var startState = (pos: start, dir: -1);
        steps[startState] = 0;
        priority[startState] = 0f;
        frontier.Add(startState);
        (Vector2 pos, int dir)? goal = null;

        while (frontier.Count > 0)
        {
            int bestIndex = 0;
            for (int i = 1; i < frontier.Count; i++)
                if (priority[frontier[i]] < priority[frontier[bestIndex]]) bestIndex = i;

            var current = frontier[bestIndex];
            frontier.RemoveAt(bestIndex);
            if (current.pos == destination) { goal = current; break; }

            for (int d = 0; d < directions.Length; d++)
            {
                Vector2 next = current.pos + directions[d];
                int newSteps = steps[current] + 1;
                if (newSteps > moveRange || !CanPassThrough(next)) continue;

                bool turned = current.dir != -1 && current.dir != d;
                float newPriority = priority[current] + 1f + (turned ? 0.01f : 0f);

                var state = (pos: next, dir: d);
                if (priority.TryGetValue(state, out float known) && known <= newPriority) continue;

                steps[state] = newSteps;
                priority[state] = newPriority;
                cameFrom[state] = current;
                frontier.Add(state);
            }
        }

        if (goal == null) return null;

        List<Vector2> path = new List<Vector2>();
        var walk = goal.Value;
        while (!walk.Equals(startState))
        {
            path.Add(walk.pos);
            walk = cameFrom[walk];
        }
        path.Reverse();
        return path;
    }
    // includeLoadSpots keeps tiles where moving there would load: a transport this unit fits in,
    // or (for a transport) a unit it can pick up
    public Dictionary<Vector2, int> GetReachableTiles(bool includeLoadSpots = false)
    {
        Vector2 start = transform.position;
        int moveRange = GetMoveBudget();
        Vector2[] directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

        Dictionary<Vector2, int> costSoFar = new Dictionary<Vector2, int>();
        Queue<Vector2> frontier = new Queue<Vector2>();
        frontier.Enqueue(start);
        costSoFar[start] = 0;

        while (frontier.Count > 0)
        {
            Vector2 current = frontier.Dequeue();
            foreach (Vector2 dir in directions)
            {
                Vector2 next = current + dir;
                int newCost = costSoFar[current] + 1;
                if (newCost > moveRange) continue;
                if (costSoFar.ContainsKey(next) && costSoFar[next] <= newCost) continue;
                if (!CanPassThrough(next)) continue;

                costSoFar[next] = newCost;
                frontier.Enqueue(next);
            }
        }

        // Friendly units can be walked through, but not stopped on. Unseen enemies look like empty tiles.
        foreach (Vector2 pos in new List<Vector2>(costSoFar.Keys))
        {
            UnitController other = GridUtil.UnitAt(pos, this);
            if (pos == start || other == null || other.IsHiddenFrom(this)) continue;
            if (includeLoadSpots && IsLoadPartner(other)) continue;
            costSoFar.Remove(pos);
        }
        return costSoFar;
    }

    // Player and allies are one side, enemies the other
    public bool IsFriendlyTo(UnitController other) => (isPlayer || isAlly) == (other.isPlayer || other.isAlly);

    // True if the viewer's side can't currently see this unit (night fog or invisibility)
    public bool IsHiddenFrom(UnitController viewer)
    {
        if (IsFriendlyTo(viewer)) return false;
        if (viewer.isPlayer || viewer.isAlly) return !IsVisibleToPlayer;
        return IsInvisibleAndUndetected(false); // the AI ignores night fog
    }

    // Tiles this unit's path may cross: enterable terrain (or a bridge) with no visible enemy on it
    public bool CanPassThrough(Vector2 pos)
    {
        TileFunction tile = GridUtil.TileAt(pos);
        if (tile == null || IsBlockedByEnemy(pos)) return false;
        return tile.CanUnitEnter(unitData) || CrossesBridge(pos);
    }

    // A water transport that carries land units doubles as a bridge
    public bool IsBridge => unitData != null && unitData.canTransport && unitData.isWater
                            && unitData.cargoType == UnitTypeExtra.CargoType.LandUnits;

    // Land units can cross a friendly bridge's tile like solid ground. They still can't stop on it,
    // because GetReachableTiles drops tiles with a unit on them
    bool CrossesBridge(Vector2 pos)
    {
        if (unitData == null || !unitData.isGround) return false;
        UnitController other = GridUtil.UnitAt(pos, this);
        return other != null && IsFriendlyTo(other) && other.IsBridge;
    }

    // Unseen enemies don't block, so the path can run into them (an ambush)
    bool IsBlockedByEnemy(Vector2 pos)
    {
        UnitController other = GridUtil.UnitAt(pos, this);
        return other != null && !IsFriendlyTo(other) && !other.IsHiddenFrom(this);
    }
  
    private IEnumerator MoveAlongPath(List<Vector2> path, System.Action onComplete)
    {
        foreach (Vector2 waypoint in path)
        {
            // Ran into an enemy we couldn't see: stop on the current tile and lose the rest of the turn
            UnitController blocker = GridUtil.UnitAt(waypoint, this);
            if (blocker != null && !IsFriendlyTo(blocker))
            {
                WasAmbushed = true;
                EndTurn();
                if (isPlayer || isAlly) blocker.SetVisibility(true); // reveal the ambusher
                Debug.Log($"{gameObject.name} was ambushed by {blocker.gameObject.name}!");
                break;
            }

            while (Vector2.Distance(transform.position, waypoint) > 0.01f)
            {
                transform.position = Vector2.MoveTowards(transform.position, waypoint, moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = waypoint;
            if (fuel > 0) fuel--; // 1 fuel per tile (-1 = unlimited)
            if (unitData != null && !unitData.isTrapImmune)
                TrafficMap.Record(waypoint, isPlayer || isAlly); // routes that could set off traps

            Collider2D[] hits = Physics2D.OverlapPointAll(waypoint);
            foreach (Collider2D hit in hits)
            {
                Trap trap = hit.GetComponent<Trap>();
                if (trap != null)
                {
                    trap.TriggerTrap(this);
                    break;
                }
            }
            // Killed by the trap: stop here but still report the move as finished,
            // otherwise whoever is waiting on this move (Cursor or the AI) waits forever
            if (IsDead) break;

            FogOfWar fog = FogOfWar.Instance;
            if (fog != null)
            {
                fog.RefreshIfNight();
            }
        }
        onComplete?.Invoke();
    }
    #endregion
# region Combat functions
    public void TakeDamage(float incomingPower)
    {
        int tileDefence = GetCurrentTileDefence();
        float actualDamage = Mathf.Max(1f, incomingPower - GetDefence() - tileDefence);
        health -= actualDamage;
        UpdateHealthDisplay();
        AddDamageFury();
        //Debug.Log($"{gameObject.name} took {actualDamage} damage | HP remaining: {health}");
    }
    public void TakeRawDamage(float amount)
    {
        health -= amount;
        UpdateHealthDisplay();
        Debug.Log($"{gameObject.name} took {amount} raw damage (armor ignored) | HP remaining: {health}");
        AddDamageFury();
    }
    // Taking a hit gives this side Fury: price x2. If the hit destroys a transport,
    // everything aboard counts as hit too
    void AddDamageFury()
    {
        if (unitData == null || CommanderManager.Instance == null) return;
        int gain = unitData.price * 2;
        if (health <= 0) gain += CargoValue() * 2;
        if (isPlayer || isAlly) CommanderManager.Instance.AddPlayerResource(gain);
        else if (isEnemy) CommanderManager.Instance.AddEnemyResource(gain);
    }
    public void UseAttack()
    {
        hasAttacked = true;
    }
    // An attack power of 0 or -1 means the unit doesn't have that weapon
    public bool HasPrimaryWeapon => unitData != null && unitData.primaryAttackPower > 0;
    public bool HasSecondaryWeapon => unitData != null && unitData.secondaryAttackPower > 0;
    public bool IsUnarmed => !HasPrimaryWeapon && !HasSecondaryWeapon;
    // A main-weapon-only unit that has used up its ammo, so it can't attack until resupplied
    public bool IsOutOfAmmo => HasPrimaryWeapon && !HasSecondaryWeapon && ammo <= 0;

    // Whether this weapon can fire at the target right now: the unit has it, the main one has ammo left,
    // and it's ticked to hit the target's kind of unit (James War > Weapon Targets)
    public bool WeaponHits(AttackType weapon, UnitController target)
    {
        if (unitData == null || target == null) return false;
        bool ready = weapon == AttackType.Primary ? HasPrimaryWeapon && ammo > 0 : HasSecondaryWeapon;
        return ready && unitData.WeaponHits(weapon == AttackType.Primary, target.unitData);
    }

    // The weapon a unit picks for itself against this target: for counter-attacks, the AI's attacks and its
    // damage estimates. Null when neither weapon can hit it
    AttackType? ChooseWeapon(UnitController target)
    {
        bool primary = WeaponHits(AttackType.Primary, target);
        bool secondary = WeaponHits(AttackType.Secondary, target);

        // Both can hit: the harder hitter fires. Health and the target's defence are the same for both weapons,
        // so more power always means more damage. A tie goes to the secondary, which doesn't use ammo
        if (primary && secondary)
            return GetAttackPower() > GetAttackPower2() ? AttackType.Primary : AttackType.Secondary;
        if (primary) return AttackType.Primary;
        if (secondary) return AttackType.Secondary;
        return null;
    }

    // Hits the target with that weapon. Main weapon shots use ammo
    void FireAt(UnitController target, AttackType weapon)
    {
        float multiplier = GetDamageMultiplier();
        if (weapon == AttackType.Primary)
        {
            target.TakeDamage(GetAttackPower() * multiplier);
            ammo--;
        }
        else
        {
            target.TakeDamage(GetAttackPower2() * multiplier);
        }
    }

    // Damage this unit's attack would deal to the target right now, using the same maths as FireAt and TakeDamage.
    // The AI uses it to look ahead without actually attacking
    public float EstimateDamage(UnitController target)
    {
        AttackType? weapon = ChooseWeapon(target);
        if (weapon == null) return 0f;

        float power = (weapon == AttackType.Primary ? GetAttackPower() : GetAttackPower2()) * GetDamageMultiplier();
        return Mathf.Max(1f, power - target.GetDefence() - target.GetCurrentTileDefence());
    }

    // weapon = the one the player chose. Left out (the AI), the unit picks for itself
    public void PerformAttack(UnitController target, AttackType? weapon = null)
    {
        // Attacker hits defender
        AttackType? firing = weapon ?? ChooseWeapon(target);
        if (firing != null && WeaponHits(firing.Value, target)) FireAt(target, firing.Value);
        AddKillFury(target);
        hasAttacked = true;

        // Counter-attack: defender hits back if still alive and attacker is in range
        if (target.health > 0 && target.CanHitTarget(this))
        {
            float counterDist = Mathf.Abs(transform.position.x - target.transform.position.x)
                              + Mathf.Abs(transform.position.y - target.transform.position.y);

            if (counterDist >= target.GetAttackRangeMin() &&
                counterDist <= target.GetAttackRangeMax())
            {
                // Counter uses the defender's own ammo and weapons
                AttackType? counter = target.ChooseWeapon(this);
                if (counter != null) target.FireAt(this, counter.Value);
                target.AddKillFury(this);
                Debug.Log($"{target.name} counter-attacks");
            }
        }
    }

    // Destroying a unit gives the killer's side Fury equal to its price,
    // plus the price of everything aboard if it was a transport
    void AddKillFury(UnitController victim)
    {
        if (victim.health > 0 || victim.unitData == null || CommanderManager.Instance == null) return;
        int killGain = victim.unitData.price + victim.CargoValue();
        if (isPlayer || isAlly) CommanderManager.Instance.AddPlayerResource(killGain);
        else if (isEnemy) CommanderManager.Instance.AddEnemyResource(killGain);
    }
    #endregion
#region Trapper/Minelayer
    public bool CanPlaceOrDisarmTrap()
    {
        return unitData != null && unitData.canTrap && !hasAttacked;
    }
    // Whether Place Trap does anything here: disarm an enemy trap, or place one if the tile has no trap and ammo is left
    public bool CanUseTrapHere()
    {
        if (!CanPlaceOrDisarmTrap()) return false;
        Trap existingTrap = TrapHere();
        if (existingTrap != null) return IsEnemyTrap(existingTrap);
        return ammo > 0 && trapPrefab != null && CanTrapTile(transform.position);
    }
    // A new trap only goes where a vehicle or ship could set it off (not on mountains, say)
    public static bool CanTrapTile(Vector2 pos)
    {
        TileFunction tile = GridUtil.TileAt(pos);
        return tile != null && tile.VehiclesCanEnter();
    }
    Trap TrapHere()
    {
        foreach (Collider2D hit in Physics2D.OverlapPointAll(transform.position))
        {
            Trap trap = hit.GetComponent<Trap>();
            if (trap != null) return trap;
        }
        return null;
    }
    // Standing on the other side's trap (trap units are immune, so it's still there to disarm)
    public bool IsOnEnemyTrap()
    {
        Trap trap = TrapHere();
        return trap != null && IsEnemyTrap(trap);
    }
    bool IsEnemyTrap(Trap trap)
    {
        return (isPlayer || isAlly) ? trap.isEnemy : (trap.isPlayer || trap.isAlly);
    }
    public void UseTrapAbility()
    {
        if (!CanUseTrapHere())
        {
            return;
        }
        Vector2 pos = transform.position;

        Trap existingTrap = TrapHere();
        if (existingTrap != null)
        {
            Debug.Log($"{gameObject.name} disarmed trap");
            Destroy(existingTrap.gameObject);
            ammo++;
            hasAttacked = true;
            return;
        }

        GameObject instance = Instantiate(trapPrefab, pos, Quaternion.identity);
        ammo--; // each trap placed uses one trap ammo, disarming gives it back
        Trap trap = instance.GetComponent<Trap>();
        if (trap != null)
        {
            trap.isPlayer = isPlayer;
            trap.isAlly = isAlly;
            trap.isEnemy = isEnemy;
        }
        hasAttacked = true;
        Debug.Log($"{gameObject.name} placed a trap");
    }
    #endregion
#region Transports
    public bool CanLoadOnto(UnitController transport)
    {
        if (transport == null || transport.unitData == null) return false;
        if (!transport.unitData.canTransport) return false;
        if (transport == this) return false;
        if (isLoaded) return false;

        bool sameFaction = (isPlayer && transport.isPlayer)
                         || (isAlly && transport.isAlly)
                         || (isEnemy && transport.isEnemy);
        if (!sameFaction) return false;
        if (!transport.CanCarry(this)) return false;

        return transport.cargo.Count < transport.unitData.cargoCapacity;
    }
    // Whether this transport's Cargo Type allows the unit (ignores room left). Carried transports keep their own cargo,
    // so a Truck can ride in a Water Support or Air Support, and a Helicopter or Air Support on an Air Carrier
    public bool CanCarry(UnitController cargoUnit)
    {
        UnitTypeExtra c = cargoUnit.unitData;
        if (unitData == null || c == null) return false;
        switch (unitData.cargoType)
        {
            case UnitTypeExtra.CargoType.Infantry: return c.unitCategory == UnitTypeExtra.UnitCategory.Trooper;
            case UnitTypeExtra.CargoType.LandUnits: return c.isGround;
            case UnitTypeExtra.CargoType.AirUnits: return c.isAir;
        }
        return false;
    }
    // True if moving onto the other unit's tile would load: this unit into it, or it into this transport
    public bool IsLoadPartner(UnitController other) => CanLoadOnto(other) || other.CanLoadOnto(this);
    public void LoadOnto(UnitController transport)
    {
        if (!CanLoadOnto(transport)) return;

        transport.cargo.Add(this);
        isLoaded = true;
        hasMoved = true;
        hasAttacked = true;

        // Carriers refuel and rearm aircraft as they land
        if (transport.unitData.cargoType == UnitTypeExtra.CargoType.AirUnits)
            ReceiveSupply(fuelMax, 0f);

        SetVisibility(false);
        if (col != null) col.enabled = false;

        Debug.Log($"{gameObject.name} loaded onto {transport.gameObject.name}");
    }
    public void UnloadTo(Vector2 pos)
    {
        transform.position = pos;
        isLoaded = false;
        hasMoved = true;    // used its move disembarking
        hasAttacked = false; // can still attack if in range
        RefreshTint();

        SetVisibility(true);
        if (col != null) col.enabled = true;

        Debug.Log($"{gameObject.name} unloaded at {pos}");
    }
    #endregion
    public void EndTurn()
    {
        hasMoved = true;
        hasAttacked = true;
        isActiveUnit = false;
        RefreshTint();
    }
    public void NextTurn()
    {
        hasMoved = false;
        hasAttacked = false;
        isActiveUnit = false;
        RefreshTint();
    }
    // Which side a freshly made unit fights for: its flags, which way it faces and its team colour.
    // Prefabs default to the player's side, so anything spawned for another side needs this
    public void SetSide(bool player, bool ally, bool enemy)
    {
        isPlayer = player;
        isAlly = ally;
        isEnemy = enemy;
        if (spriteRenderer != null) spriteRenderer.flipX = player || ally; // player side faces right, enemies left
        RefreshTeamColor();
    }

    public void RefreshTeamColor()
    {
        if (teamColorApplier != null)
            teamColorApplier.ApplyTeamColor(isPlayer, isAlly, isEnemy);

        if (typeLabel == null) CreateTypeLabel();
        if (typeLabel != null)
            typeLabel.color = isPlayer ? new Color(0.4f, 0.65f, 1f) : isAlly ? new Color(1f, 0.85f, 0.2f) : isEnemy ? new Color(1f, 0.35f, 0.35f) : Color.white;
    }

    // Placeholder until every unit has its own art: the unit type's Short Name across the top of the unit,
    // in its team's colour (blue = player, red = enemy, yellow = ally)
    private TextMeshPro typeLabel;
    void CreateTypeLabel()
    {
        if (unitData == null || string.IsNullOrEmpty(unitData.shortName)) return;

        GameObject labelObject = new GameObject("TypeLabel");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0.38f, 0f);

        typeLabel = labelObject.AddComponent<TextMeshPro>();
        typeLabel.text = unitData.shortName;
        typeLabel.fontSize = 2.6f;
        typeLabel.fontStyle = FontStyles.Bold;
        typeLabel.alignment = TextAlignmentOptions.Center;
        typeLabel.textWrappingMode = TextWrappingModes.NoWrap;
        typeLabel.rectTransform.sizeDelta = new Vector2(1.4f, 0.4f);
        typeLabel.outlineWidth = 0.3f; // dark edge so it reads on any tile colour
        typeLabel.outlineColor = Color.black;

        // Draw on the same layer as the health number, so it sits above the unit
        MeshRenderer labelRenderer = typeLabel.GetComponent<MeshRenderer>();
        Renderer reference = healthText != null ? healthText.GetComponent<Renderer>() : spriteRenderer;
        labelRenderer.sortingLayerID = reference.sortingLayerID;
        labelRenderer.sortingOrder = reference.sortingOrder + 1;
    }
    public void ReceiveSupply(int fuelAmount, float healthAmount)
    {
        ammoMax = GetAmmo();
        ammo = ammoMax; // full ammo restore

        if (fuelMax >= 0) // skip infinite-fuel units
            fuel = Mathf.Min(fuel + fuelAmount, fuelMax);

        health = Mathf.Min(health + healthAmount, healthMax);
        UpdateHealthDisplay();

        Debug.Log($"[Supply] {gameObject.name} resupplied | ammo={ammo}/{ammoMax} fuel={fuel}/{fuelMax} hp={health}/{healthMax}");
    }
}
