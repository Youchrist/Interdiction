using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

public class EnemyAi : MonoBehaviour
{
    [Header("Commander ability")]
    [Tooltip("Combat abilities: use when at least this share of the AI's units can attack this turn...")]
    [Range(0f, 1f)] public float combatAttackerShare = 0.25f;
    [Tooltip("...or when this turn's attacks could deal at least this many funds' worth of damage")]
    public float combatDamageValue = 20000f;
    [Tooltip("Sabotage abilities: use when they would deny the opponent at least this many funds' worth")]
    public float sabotageDenialValue = 20000f;

    [Header("Building")]
    [Tooltip("The AI builds a capturing infantry unit (first, before anything else) whenever it has fewer than this many")]
    public int minimumInfantry = 3;
    [Tooltip("...except when the AI is down to one base, has at least this much money and it's at least Late Game Day")]
    public int richFunds = 20000;
    public int lateGameDay = 10;
    [Tooltip("Units whose counter value is within this share of the best are treated as equally good; the priciest of them is built")]
    [Range(0f, 1f)] public float counterTolerance = 0.1f;
    [Tooltip("Most of each unarmed unit type (Truck, Water Transport, Air Support) the AI keeps. It builds no more of that type until one is lost")]
    public int maxEachUnarmed = 1;

    [Header("Transports")]
    [Tooltip("Chance a transport carries its passenger to the player's HQ as a flank, instead of to the main army")]
    [Range(0f, 1f)] public float flankChance = 0.3f;
    [Tooltip("How close to its destination a transport gets before dropping its cargo")]
    public int dropDistance = 2;

    [Header("Trappers and Mine Layers")]
    [Tooltip("How much of last round's traffic is remembered each round (lower = only recent routes matter)")]
    [Range(0f, 1f)] public float trafficMemory = 0.8f;
    [Tooltip("Rounds before a tile searched for the player's traps is worth searching again")]
    public int resweepAfterTurns = 5;

    private GameManager gm;

    // Tiles (objectives, units to supply, passengers) one AI unit has already picked this turn, so others go elsewhere
    private HashSet<Vector2> claimed = new HashSet<Vector2>();
    // Transports carrying a passenger: true = flanking to the player's HQ, false = to the main army
    private Dictionary<UnitController, bool> flankPlan = new Dictionary<UnitController, bool>();
    // Units dropped off by a flanking transport: they make for the player's HQ
    private HashSet<UnitController> flankers = new HashSet<UnitController>();

    private int turnNumber = 0;
    // Turns a ready summoning ability has been held back, waiting for more properties to clear
    private int summonHeldTurns = 0;
    // Trap unit types the player has fielded at some point (their traps may still be out there)
    private HashSet<UnitTypeExtra> opponentTrapKinds = new HashSet<UnitTypeExtra>();
    // Tiles searched for the player's traps, and on which turn
    private Dictionary<Vector2, int> sweptOnTurn = new Dictionary<Vector2, int>();
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        gm = FindFirstObjectByType<GameManager>();
    }
    IEnumerator TrySpawnUnits()
    {
        if (gm == null) yield break;

        List<UnitSpawner> spawners = new List<UnitSpawner>();
        foreach (UnitSpawner s in FindObjectsByType<UnitSpawner>(FindObjectsSortMode.None))
            if (s.isEnemy) spawners.Add(s);
        // Land bases first, so this turn's infantry gets first call on the money
        spawners.Sort((a, b) => (b.isGroundSpawner ? 1 : 0).CompareTo(a.isGroundSpawner ? 1 : 0));

        bool needInfantry = InfantryRequired(spawners.Count);
        List<UnitController> playerArmy = PlayerTargets();

        foreach (UnitSpawner spawner in spawners)
        {
            if (GridUtil.IsOccupied(spawner.transform.position)) continue; // something's standing on it

            GameObject pick = needInfantry ? BestBuild(spawner, playerArmy, true) : null;
            if (pick != null) needInfantry = false;
            else pick = BestBuild(spawner, playerArmy, false);

            if (pick == null)
            {
                Debug.Log($"[EnemyAI] Nothing to build at {spawner.gameObject.name} (tech unlocks, terrain or funds)");
                continue;
            }

            int price = pick.GetComponent<UnitController>().unitData.price;
            spawner.SpawnUnit(pick);
            gm.EnemyAddMoney(-price);
            Debug.Log($"[EnemyAI] Built {pick.name} at {spawner.gameObject.name} for {price}");
            yield return new WaitForSeconds(0.2f);
        }
    }

    // Capturing wins games, so every turn's builds include a capturing infantry unit. The exception: down to
    // a single base, late in the game, with money to burn. Then that one base is better spent on something big
    bool InfantryRequired(int baseCount)
    {
        bool bigSpender = baseCount == 1 && gm.EnemyMoney >= richFunds && gm.Day >= lateGameDay;
        return !bigSpender && CapturingInfantryCount() < minimumInfantry;
    }

    // Capturing infantry the AI already has (Trappers lay traps instead of capturing, so they don't count)
    int CapturingInfantryCount()
    {
        int count = 0;
        foreach (UnitController u in FindObjectsByType<UnitController>(FindObjectsSortMode.None))
            if (u.isEnemy && !u.IsDead && u.unitData != null && IsCapturingInfantry(u.unitData)) count++;
        return count;
    }

    static bool IsCapturingInfantry(UnitTypeExtra data)
        => data.unitCategory == UnitTypeExtra.UnitCategory.Trooper && data.canCapture && !data.canTrap;

    static bool IsUnarmedType(UnitTypeExtra data) => data.primaryAttackPower <= 0 && data.secondaryAttackPower <= 0;

    // How many units of this type the AI has, ones riding in transports included
    int EnemyUnitCount(UnitTypeExtra data)
    {
        int count = 0;
        foreach (UnitController u in FindObjectsByType<UnitController>(FindObjectsSortMode.None))
            if (u.isEnemy && !u.IsDead && u.unitData == data) count++;
        return count;
    }

    // Counter first, then priciest: of the units this base can build right now, the ones that would do the most
    // damage to the player's current army (in funds), with near-ties (counterTolerance) going to the priciest.
    // infantryOnly limits it to infantry that capture (Trappers lay traps instead, so they don't count)
    GameObject BestBuild(UnitSpawner spawner, List<UnitController> playerArmy, bool infantryOnly)
    {
        List<(GameObject prefab, UnitTypeExtra data, float counter)> options = new List<(GameObject, UnitTypeExtra, float)>();
        float bestCounter = 0f;

        for (int i = 0; i < gm.prefabList.Count; i++)
        {
            GameObject prefab = gm.GetPrefabAtIndex(i);
            UnitController uc = prefab != null ? prefab.GetComponent<UnitController>() : null;
            if (uc == null || uc.unitData == null) continue;
            UnitTypeExtra data = uc.unitData;

            if (TechTree.Instance != null && !TechTree.Instance.IsUnlocked(i, false)) continue; // not researched
            if (!gm.EnemyCanAfford(data.price)) continue;
            if (!spawner.CanSpawnUnit(prefab)) continue; // wrong kind of base
            if (infantryOnly && !IsCapturingInfantry(data)) continue;
            // Unarmed units score 0 as counters, so they'd win every tie where nothing else can hit the player.
            // Capping them stops a base whose only basic unit is a transport from making one every turn
            if (IsUnarmedType(data) && EnemyUnitCount(data) >= maxEachUnarmed) continue;

            float counter = CounterValue(data, playerArmy);
            options.Add((prefab, data, counter));
            bestCounter = Mathf.Max(bestCounter, counter);
        }

        GameObject best = null;
        int bestPrice = -1;
        foreach (var (prefab, data, counter) in options)
        {
            if (counter < bestCounter * (1f - counterTolerance)) continue; // a clearly worse counter
            if (data.price > bestPrice) { bestPrice = data.price; best = prefab; }
        }
        return best;
    }

    // How much damage (in funds, via DamageValue) a fresh unit of this type could deal across the player's army:
    // one hit on each unit it can target. Shows which unit types counter what the player is fielding
    float CounterValue(UnitTypeExtra data, List<UnitController> playerArmy)
    {
        float bonus = TechTree.Instance != null ? TechTree.Instance.GetUpgradeBonus(data.unitCategory, false) : 0f;
        float mult = CommanderManager.Instance != null ? CommanderManager.Instance.GetAttackMult(data.unitCategory, false) : 1f;

        float total = 0f;
        foreach (UnitController target in playerArmy)
        {
            UnitTypeExtra t = target.unitData;
            if (t == null) continue;

            // Whichever weapon is ticked to hit this kind of unit (the primary if both are)
            float power = data.WeaponHits(true, t) ? data.primaryAttackPower
                        : data.WeaponHits(false, t) ? data.secondaryAttackPower : 0f;
            if (power <= 0) continue;
            power = (power + bonus) * mult;

            float damage = Mathf.Max(1f, power - target.GetDefence() - target.GetCurrentTileDefence());
            total += DamageValue(target, damage);
        }
        return total;
    }
    public IEnumerator EnemyTurn()
    {
        Debug.Log("enemy turn");

        TryResearch();
        TryUseAbility(false); // combat, sabotage and economy abilities go off before anyone moves

        claimed.Clear();
        flankers.RemoveWhere(u => u == null);
        turnNumber++;
        TrafficMap.Fade(trafficMemory);
        foreach (UnitController u in FindObjectsByType<UnitController>(FindObjectsSortMode.None))
            if ((u.isPlayer || u.isAlly) && u.unitData != null && u.unitData.canTrap)
                opponentTrapKinds.Add(u.unitData); // remembered even after that unit is gone, its traps may not be

        // Transports act first (so passengers are still on their bases), then capturers (so they get first pick of objectives)
        List<UnitController> allUnits = new List<UnitController>(FindObjectsByType<UnitController>(FindObjectsSortMode.None));
        allUnits.Sort((a, b) => TurnOrder(a).CompareTo(TurnOrder(b)));

        foreach (UnitController unit in allUnits)
        {
            if (unit == null || unit.IsDead) continue; // destroyed or crashed earlier this turn
            if(!unit.isEnemy) continue;
            if (unit.isLoaded) continue; // inside a transport: it acts once it's dropped off
            if(!unit.CanAct()) continue;

            yield return StartCoroutine(ProcessUnit(unit));
            yield return new WaitForSeconds(0.3f);
        }

        // Units have stepped off their bases now, so bases are free for summoning and then for building
        TryUseAbility(true);
        yield return StartCoroutine(TrySpawnUnits());

        Debug.Log("enemy turn done");
        gm.TurnEnd();
    }
    void TryResearch()
    {
        if (TechTree.Instance == null || gm == null) return;

        List<UnitController> playerArmy = PlayerTargets();
        while (TechTree.Instance.EnemyHasResearch())
        {
            // Only units one of its own bases can build. Best counter to the player's army first; near-ties
            // (counterTolerance) go to the cheaper unit, so it researches things it can actually afford
            List<(int index, int price, float counter)> options = new List<(int, int, float)>();
            float bestCounter = 0f;
            for (int i = 0; i < gm.prefabList.Count; i++)
            {
                if (TechTree.Instance.IsUnlocked(i, false)) continue;

                GameObject prefab = gm.GetPrefabAtIndex(i);
                UnitController uc = prefab != null ? prefab.GetComponent<UnitController>() : null;
                if (uc == null || uc.unitData == null || !OwnBaseBuilds(uc.unitData)) continue;

                float counter = CounterValue(uc.unitData, playerArmy);
                options.Add((i, uc.unitData.price, counter));
                bestCounter = Mathf.Max(bestCounter, counter);
            }

            int bestIndex = -1;
            int bestPrice = int.MaxValue;
            foreach (var (index, price, counter) in options)
            {
                if (counter < bestCounter * (1f - counterTolerance)) continue; // a clearly worse counter
                if (price < bestPrice) { bestPrice = price; bestIndex = index; }
            }

            if (bestIndex == -1)
            {
                // Every unit researched: put the rest into the repeatable upgrades
                if (!TryUpgrade()) break;
                continue;
            }
            TechTree.Instance.Unlock(bestIndex, false);
        }
    }

    #region Commander ability
    // Called twice a turn: before units act (combat, sabotage, economy) and after (summoning, once bases are clear)
    void TryUseAbility(bool afterUnitsActed)
    {
        CommanderManager cm = CommanderManager.Instance;
        Class abilities = FindFirstObjectByType<Class>();
        if (cm == null || abilities == null || cm.enemyCommander == null) return;
        if (!cm.EnemyAbilityReady()) return;

        CommanderData commander = cm.enemyCommander;
        string commanderName = commander.commanderName;
        bool use = false;

        switch (commander.abilityCategory)
        {
            case CommanderData.AbilityCategory.Summoning:
                if (!afterUnitsActed) return;
                abilities.GetSummonCount(commanderName, false, out int now, out int best);
                use = ShouldSummon(now, best, summonHeldTurns);
                Debug.Log($"[EnemyAI] Summon check: {now} now, {best} at best, held for {summonHeldTurns} turn(s) -> {(use ? "summon" : "wait")}");
                if (!use) summonHeldTurns++;
                break;

            case CommanderData.AbilityCategory.Combat:
                if (afterUnitsActed) return;
                PredictCombat(out int attackers, out int total, out float damageValue);
                use = (total > 0 && attackers >= total * combatAttackerShare) || damageValue >= combatDamageValue;
                Debug.Log($"[EnemyAI] Combat check: {attackers}/{total} units can attack, {damageValue:0} funds of damage");
                break;

            case CommanderData.AbilityCategory.Sabotage:
                if (afterUnitsActed) return;
                float denial = abilities.EstimateDenial(commanderName, false);
                use = denial >= sabotageDenialValue;
                Debug.Log($"[EnemyAI] Sabotage check: would deny {denial:0} funds");
                break;

            case CommanderData.AbilityCategory.Economy:
                if (afterUnitsActed) return;
                use = true; // as soon as it's ready, to pile up as much as possible
                break;
        }

        if (use && abilities.UseEnemyAbility(commanderName))
        {
            summonHeldTurns = 0;
            Debug.Log($"[EnemyAI] Used {commander.abilityName}");
        }
    }
    bool ShouldSummon(int now, int best, int turnsHeld)
    {
        if (now > 0 && now >= best - turnsHeld)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    // Looks ahead at this turn: how many AI units will be able to attack something,
    // and the funds' worth of damage they'd deal if each hits its most valuable target
    void PredictCombat(out int attackers, out int total, out float damageValue)
    {
        attackers = 0;
        total = 0;
        damageValue = 0f;

        List<UnitController> targets = PlayerTargets();
        foreach (UnitController unit in FindObjectsByType<UnitController>(FindObjectsSortMode.None))
        {
            if (!unit.isEnemy || unit.isLoaded || unit.IsDead) continue;
            total++;
            if (FindBestAttack(unit, targets, true, out _, out _, out float value))
            {
                attackers++;
                damageValue += value;
            }
        }
    }

    // Player units the AI can see and target
    List<UnitController> PlayerTargets()
    {
        List<UnitController> targets = new List<UnitController>();
        foreach (UnitController u in FindObjectsByType<UnitController>(FindObjectsSortMode.None))
            if ((u.isPlayer || u.isAlly) && !u.isLoaded && !u.IsDead && !u.IsInvisibleAndUndetected(false))
                targets.Add(u);
        return targets;
    }

    // The most valuable attack this unit can make: which target, and the closest tile to fire from.
    // allowMove = false only considers where it stands. Ranged units never fire after moving
    bool FindBestAttack(UnitController unit, List<UnitController> targets, bool allowMove,
                        out UnitController bestTarget, out Vector2 bestSpot, out float bestValue)
    {
        bestTarget = null;
        bestSpot = unit.transform.position;
        bestValue = 0f;
        if (!unit.CanAttack()) return false;

        // Tile -> move cost to get there
        Dictionary<Vector2, int> spots = new Dictionary<Vector2, int> { { unit.transform.position, 0 } };
        bool ranged = unit.unitData != null && unit.unitData.isRanged;
        if (allowMove && !ranged && !unit.HasMoved)
            foreach (KeyValuePair<Vector2, int> kv in unit.GetReachableTiles()) spots[kv.Key] = kv.Value;

        foreach (UnitController target in targets)
        {
            if (!unit.CanHitTarget(target)) continue;
            if (!ClosestSpotInRange(unit, spots, target, out Vector2 spot)) continue;

            float value = DamageValue(target, unit.EstimateDamage(target));
            if (bestTarget == null || value > bestValue)
            {
                bestTarget = target;
                bestSpot = spot;
                bestValue = value;
            }
        }
        return bestTarget != null;
    }

    bool ClosestSpotInRange(UnitController unit, Dictionary<Vector2, int> spots, UnitController target, out Vector2 best)
    {
        best = unit.transform.position;
        int bestCost = int.MaxValue;
        foreach (KeyValuePair<Vector2, int> kv in spots)
        {
            float d = GridUtil.Manhattan(kv.Key, target.transform.position);
            if (d < unit.GetAttackRangeMin() || d > unit.GetAttackRangeMax()) continue;
            if (kv.Value < bestCost) { bestCost = kv.Value; best = kv.Key; }
        }
        return bestCost != int.MaxValue;
    }

    // How many funds' worth a hit of this much damage is against this target
    float DamageValue(UnitController target, float damage)
    {
        if (target.unitData == null) return 0f;
        float price = target.unitData.price;

        // Share of the unit's max HP the hit removes (0 to 1). Overkill doesn't count
        float hpLost = Mathf.Min(damage, target.changeHealth) / target.GetHealth();

        // Share of its fighting strength the hit removes (0 to 1). Units fight in 20% steps of health,
        // so this only moves when a step is crossed, and a kill takes everything that's left
        float strengthLost = target.GetDamageMultiplierAt(target.changeHealth)
                           - target.GetDamageMultiplierAt(target.changeHealth - damage);

        // Mostly HP damage, with a bonus for crossing a strength step. The weights add up to 1.1 on purpose,
        // so wiping out a full-health unit counts for a bit more than its price
        return price * (0.7f * hpLost + 0.4f * strengthLost);
    }
    #endregion

    // Whether any of the AI's bases makes this kind of unit (land, air or sea)
    bool OwnBaseBuilds(UnitTypeExtra data)
    {
        foreach (UnitSpawner s in FindObjectsByType<UnitSpawner>(FindObjectsSortMode.None))
            if (s.isEnemy && s.BuildsType(data)) return true;
        return false;
    }

    // Raises whichever open upgrade (Infantry, Land, Air, Naval) has the lowest level
    bool TryUpgrade()
    {
        UnitTypeExtra.UnitCategory[] upgradable =
        {
            UnitTypeExtra.UnitCategory.Trooper, UnitTypeExtra.UnitCategory.Vehicle,
            UnitTypeExtra.UnitCategory.Air, UnitTypeExtra.UnitCategory.Naval
        };

        bool found = false;
        UnitTypeExtra.UnitCategory best = upgradable[0];
        foreach (UnitTypeExtra.UnitCategory category in upgradable)
        {
            if (!TechTree.Instance.CanUpgrade(category, false)) continue;
            if (!found || TechTree.Instance.GetUpgradeLevel(category, false) < TechTree.Instance.GetUpgradeLevel(best, false))
            {
                best = category;
                found = true;
            }
        }
        return found && TechTree.Instance.Upgrade(best, false);
    }

    #region Unit behaviour
    int TurnOrder(UnitController u)
    {
        if (u.unitData == null) return 2;
        if (u.unitData.canTransport && u.IsUnarmed) return 0;
        if (u.unitData.canCapture) return 1;
        return 2;
    }

    IEnumerator ProcessUnit(UnitController unit)
    {
        UnitTypeExtra data = unit.unitData;

        // Already moved this turn (just dropped off by a transport): it can only fire from where it stands
        if (unit.HasMoved)
        {
            AttackFromHere(unit);
            unit.EndTurn();
            yield break;
        }

        // A carrier with refuelled aircraft aboard sends them back out
        bool unarmedTransport = data != null && data.canTransport && unit.IsUnarmed;
        if (!unarmedTransport && unit.cargo.Count > 0 && LaunchAircraft(unit))
        {
            unit.EndTurn();
            yield break;
        }

        // Running low on fuel: head back to refuel before it's stranded (or, for aircraft, crashes)
        if (unit.GetFuelMax() >= 0 && FindSupplyPoint(unit, out Vector2 refuelPoint, out _)
            && ShouldHeadBackForFuel(unit, FuelToReach(unit, refuelPoint)))
        {
            yield return StartCoroutine(GoResupply(unit));
            yield break;
        }

        // Trappers and Mine Layers play their own way: laying, restocking and disarming traps
        if (data != null && data.canTrap)
        {
            yield return StartCoroutine(ProcessTrapUnit(unit));
            yield break;
        }

        if (data != null && data.canTransport && unit.IsUnarmed)
        {
            yield return StartCoroutine(ProcessTransport(unit));
            yield break;
        }

        if (unit.IsOutOfAmmo)
        {
            yield return StartCoroutine(GoResupply(unit));
            yield break;
        }

        // Low on supplies with a supplier beside it (or on its own property): stay put to be refilled, firing from here
        if (NeedsSupply(unit) && IsBeingResupplied(unit))
        {
            AttackFromHere(unit);
            unit.EndTurn();
            yield break;
        }

        // Capture units go for objectives first, and attack whatever's in range once they get there
        if (data != null && data.canCapture)
        {
            TileFunction currentTile = GridUtil.TileAt(unit.transform.position);
            if (currentTile != null && currentTile.isCaptureable && !currentTile.ownedByEnemy)
            {
                Debug.Log($"[EnemyAI] {unit.name} holding position to capture {currentTile.name}");
                AttackFromHere(unit);
                unit.EndTurn();
                yield break;
            }

            // Ranged capturers (the Mortar) shoot first: moving would cost them the shot
            if (data.isRanged && FindBestAttack(unit, PlayerTargets(), false, out UnitController inRange, out _, out _))
            {
                AttackFromHere(unit, inRange);
                unit.EndTurn();
                yield break;
            }

            TileFunction objective = FindObjective(unit, true);
            if (objective != null)
            {
                yield return StartCoroutine(MoveToward(unit, objective.transform.position));
                if (unit.IsDead) yield break; // killed by a trap on the way
                AttackFromHere(unit);
                unit.EndTurn();
                yield break;
            }
            // Nothing left to capture: fight like everyone else
        }

        // Attack the most valuable target it can reach this turn...
        List<UnitController> targets = PlayerTargets();
        if (FindBestAttack(unit, targets, true, out UnitController target, out Vector2 spot, out _))
        {
            if (spot != (Vector2)unit.transform.position) yield return unit.MoveTo(spot);
            if (unit.IsDead) yield break;
            AttackFromHere(unit, target);
            unit.EndTurn();
            yield break;
        }

        // Ranged units can't fire after moving, so set up for next turn instead
        if (data != null && data.isRanged)
        {
            // Wait just outside the reach of nearby player units: anything that comes after it
            // has to end its move inside this unit's fire first.
            // If nowhere is out of reach, at least get a target in range, as far from the player's units as possible
            Vector2 setup;
            if (FindKitingPosition(unit, targets, out setup) || FindFiringPosition(unit, targets, out setup))
            {
                if (setup != (Vector2)unit.transform.position) yield return unit.MoveTo(setup);
                if (!unit.IsDead) unit.EndTurn();
                yield break;
            }
        }

        // ...otherwise go for objectives: producing bases, then HQ, then other properties.
        // With none left, close in on the nearest unit it could hit
        TileFunction goal = FindObjective(unit, false);
        UnitController prey = goal == null ? FindNearestPlayerUnit(unit) : null;
        if (goal != null) yield return StartCoroutine(MoveToward(unit, goal.transform.position));
        else if (prey != null) yield return StartCoroutine(MoveToward(unit, prey.transform.position));
        if (unit.IsDead) yield break;

        // Got nowhere (e.g. a ship with no sea base to take and nothing it can hit nearby): head as close as it can
        // to the player's objectives anyway, and never sit idle on one of its own bases, which would block building
        if (!unit.HasMoved)
        {
            TileFunction approach = NearestObjectiveToApproach(unit);
            if (approach != null) yield return StartCoroutine(MoveToward(unit, approach.transform.position, OwnProducingBases()));
            if (unit.IsDead) yield break;
            if (!unit.HasMoved) yield return StartCoroutine(StepOffOwnBase(unit));
            if (unit.IsDead) yield break;
        }

        AttackFromHere(unit);
        unit.EndTurn();
    }

    // How far a player unit can strike next turn: ranged units only fire from where they stand,
    // others can move first. 0 if it can't hit the victim at all
    float ThreatReach(UnitController enemy, UnitController victim)
    {
        if (!enemy.CanHitTarget(victim)) return 0f;
        bool ranged = enemy.unitData != null && enemy.unitData.isRanged;
        return enemy.GetAttackRangeMax() + (ranged ? 0 : enemy.GetMoveBudget());
    }

    // For a ranged unit with nothing to shoot: a reachable tile out of every player unit's reach, as close as possible
    // to one tile past the reach of a target it can hit (or at its full range, for targets that can't hit back).
    // Only targets it can get into position against this turn count; with none nearby it goes for objectives instead
    bool FindKitingPosition(UnitController unit, List<UnitController> targets, out Vector2 best)
    {
        Vector2 unitPos = unit.transform.position;
        best = unitPos;
        int move = unit.GetMoveBudget();

        List<float> reach = new List<float>();
        foreach (UnitController t in targets) reach.Add(ThreatReach(t, unit));

        float bestGap = float.MaxValue;
        float bestSafety = -1f;
        foreach (Vector2 tile in unit.GetReachableTiles().Keys)
        {
            bool safe = true;
            float gap = float.MaxValue;    // how far off the ideal distance it is from its best target
            float safety = float.MaxValue; // spare tiles between it and the closest reach
            for (int i = 0; i < targets.Count; i++)
            {
                UnitController t = targets[i];
                float d = GridUtil.Manhattan(tile, t.transform.position);
                if (reach[i] > 0 && d <= reach[i]) { safe = false; break; }
                if (reach[i] > 0) safety = Mathf.Min(safety, d - reach[i]);

                if (!unit.CanHitTarget(t)) continue;
                float wanted = reach[i] > 0 ? reach[i] + 1 : unit.GetAttackRangeMax();
                if (Mathf.Abs(GridUtil.Manhattan(unitPos, t.transform.position) - wanted) > move) continue; // too far off to matter yet
                gap = Mathf.Min(gap, Mathf.Abs(d - wanted));
            }
            if (!safe || gap == float.MaxValue) continue;

            if (gap < bestGap || (gap == bestGap && safety > bestSafety))
            {
                bestGap = gap;
                bestSafety = safety;
                best = tile;
            }
        }
        return bestGap != float.MaxValue;
    }

    // A reachable tile from which at least one target is within range (for firing next turn).
    // Of those, the one furthest from the nearest player unit, so it isn't left in harm's way
    bool FindFiringPosition(UnitController unit, List<UnitController> targets, out Vector2 best)
    {
        best = unit.transform.position;
        float bestSafety = -1f;
        foreach (Vector2 tile in unit.GetReachableTiles().Keys)
        {
            bool targetInRange = false;
            float safety = float.MaxValue; // distance to the closest player unit
            foreach (UnitController t in targets)
            {
                float d = GridUtil.Manhattan(tile, t.transform.position);
                safety = Mathf.Min(safety, d);
                if (!targetInRange && unit.CanHitTarget(t) && d >= unit.GetAttackRangeMin() && d <= unit.GetAttackRangeMax())
                    targetInRange = true;
            }
            if (targetInRange && safety > bestSafety)
            {
                bestSafety = safety;
                best = tile;
            }
        }
        return bestSafety >= 0f;
    }

    // Fires at the most valuable target in range of where the unit stands (or at preferred, if it's in range)
    void AttackFromHere(UnitController unit, UnitController preferred = null)
    {
        if (unit.IsDead || !unit.CanAttack()) return;

        if (preferred != null && !preferred.IsDead && unit.CanAttackTarget(preferred))
        {
            float d = GridUtil.Manhattan(unit.transform.position, preferred.transform.position);
            if (d >= unit.GetAttackRangeMin() && d <= unit.GetAttackRangeMax())
            {
                unit.PerformAttack(preferred);
                return;
            }
        }
        if (FindBestAttack(unit, PlayerTargets(), false, out UnitController target, out _, out _))
            unit.PerformAttack(target);
    }

    IEnumerator MoveToward(UnitController unit, Vector2 destination, HashSet<Vector2> avoid = null)
    {
        Vector2 moveTo = FindBestMoveToward(unit, destination, avoid);
        if (moveTo != (Vector2)unit.transform.position)
            yield return unit.MoveTo(moveTo);
    }

    // Where to head when there's nothing to attack. Priority: the player's unit-producing bases, then their HQ,
    // then their other properties (capture units also go for neutral ones). Each objective is claimed by one unit
    TileFunction FindObjective(UnitController unit, bool capturing)
    {
        Vector2 unitPos = unit.transform.position;

        // Dropped off by a flanking transport: go for the HQ
        if (flankers.Contains(unit))
        {
            TileFunction hq = PlayerHQ();
            if (hq != null && hq.CanUnitEnter(unit.unitData)) return hq;
        }

        TileFunction best = null;
        int bestRank = int.MaxValue;
        float bestDist = float.MaxValue;
        foreach (TileFunction tile in FindObjectsByType<TileFunction>(FindObjectsSortMode.None))
        {
            if (!tile.isCaptureable || tile.ownedByEnemy) continue; // already the AI's
            if (!tile.ownedByPlayer && !capturing) continue;        // only capture units bother with neutral ones
            if (!tile.CanUnitEnter(unit.unitData)) continue;

            Vector2 pos = tile.transform.position;
            if (claimed.Contains(pos) || GridUtil.IsOccupied(pos, unit)) continue;

            int rank = tile.GetComponent<UnitSpawner>() != null ? 0 : tile.isHQ ? 1 : 2;
            float dist = GridUtil.Manhattan(unitPos, pos);
            if (rank < bestRank || (rank == bestRank && dist < bestDist))
            {
                best = tile;
                bestRank = rank;
                bestDist = dist;
            }
        }

        if (best != null) claimed.Add(best.transform.position);
        return best;
    }

    // The player's most important nearby property to head toward, even one this unit can't stand on
    // (a ship sails as close to a land base as the water allows). Same priority as FindObjective
    TileFunction NearestObjectiveToApproach(UnitController unit)
    {
        Vector2 unitPos = unit.transform.position;
        TileFunction best = null;
        int bestRank = int.MaxValue;
        float bestDist = float.MaxValue;
        foreach (TileFunction tile in FindObjectsByType<TileFunction>(FindObjectsSortMode.None))
        {
            if (!tile.ownedByPlayer || (!tile.isCaptureable && !tile.isBase && !tile.isHQ)) continue;
            int rank = tile.GetComponent<UnitSpawner>() != null ? 0 : tile.isHQ ? 1 : 2;
            float dist = GridUtil.Manhattan(unitPos, tile.transform.position);
            if (rank < bestRank || (rank == bestRank && dist < bestDist))
            {
                best = tile;
                bestRank = rank;
                bestDist = dist;
            }
        }
        return best;
    }

    // A unit standing on one of the AI's own bases moves to the nearest tile that isn't a base, so the base can build
    IEnumerator StepOffOwnBase(UnitController unit)
    {
        HashSet<Vector2> bases = OwnProducingBases();
        Vector2 pos = unit.transform.position;
        if (!bases.Contains(pos)) yield break;

        Vector2? best = null;
        int bestCost = int.MaxValue;
        foreach (KeyValuePair<Vector2, int> kv in unit.GetReachableTiles())
        {
            if (kv.Key == pos || bases.Contains(kv.Key)) continue;
            if (kv.Value < bestCost) { bestCost = kv.Value; best = kv.Key; }
        }
        if (best != null) yield return unit.MoveTo(best.Value);
    }

    TileFunction PlayerHQ()
    {
        foreach (TileFunction tile in FindObjectsByType<TileFunction>(FindObjectsSortMode.None))
            if (tile.isHQ && tile.isPlayerHQ && !tile.ownedByEnemy) return tile;
        return null;
    }

    // Positions of the AI's own unit-producing bases (transports don't park on them, so building isn't blocked)
    HashSet<Vector2> OwnProducingBases()
    {
        HashSet<Vector2> bases = new HashSet<Vector2>();
        foreach (UnitSpawner s in FindObjectsByType<UnitSpawner>(FindObjectsSortMode.None))
            if (s.isEnemy) bases.Add(s.transform.position);
        return bases;
    }
    #endregion

    #region Supply
    // Out of ammo, traps or fuel: head for the nearest place to be refilled. Already there: stay put, firing if it can
    IEnumerator GoResupply(UnitController unit)
    {
        if (IsBeingResupplied(unit))
        {
            AttackFromHere(unit);
        }
        else if (FindSupplyPoint(unit, out Vector2 point, out UnitController carrier))
        {
            Debug.Log($"[EnemyAI] {unit.name} heading back to resupply");
            if (carrier != null && unit.GetReachableTiles(true).ContainsKey(point))
            {
                // Land on the carrier: refuelled and rearmed straight away
                yield return unit.MoveTo(point);
                if (!unit.IsDead && !unit.WasAmbushed && unit.CanLoadOnto(carrier)) unit.LoadOnto(carrier);
            }
            else
            {
                yield return StartCoroutine(MoveToward(unit, point));
            }
        }
        if (!unit.IsDead) unit.EndTurn();
    }

    // Nearest place this unit can be refilled: beside a supply unit, on one of the AI's own properties,
    // or (aircraft only) on an Air Carrier with room. carrier is set when it's a carrier
    bool FindSupplyPoint(UnitController unit, out Vector2 point, out UnitController carrier)
    {
        Vector2 unitPos = unit.transform.position;
        point = unitPos;
        carrier = null;
        float bestDist = float.MaxValue;

        foreach (UnitController s in FindObjectsByType<UnitController>(FindObjectsSortMode.None))
        {
            if (!s.isEnemy || s == unit || s.isLoaded || s.IsDead) continue;
            bool supplier = s.CanSupplyOthers();
            bool landingSpot = unit.unitData != null && unit.unitData.isAir && unit.CanLoadOnto(s);
            if (!supplier && !landingSpot) continue;

            float d = GridUtil.Manhattan(unitPos, s.transform.position); // moving toward a supplier ends beside it
            if (d < bestDist)
            {
                bestDist = d;
                point = s.transform.position;
                carrier = landingSpot ? s : null;
            }
        }
        foreach (TileFunction tile in FindObjectsByType<TileFunction>(FindObjectsSortMode.None))
        {
            if (!tile.isCaptureable || !tile.ownedByEnemy || !tile.CanUnitEnter(unit.unitData)) continue;
            Vector2 pos = tile.transform.position;
            if (GridUtil.IsOccupied(pos, unit)) continue;
            float d = GridUtil.Manhattan(unitPos, pos);
            if (d < bestDist)
            {
                bestDist = d;
                point = pos;
                carrier = null;
            }
        }
        return bestDist != float.MaxValue;
    }

    // Fuel it would burn getting to a point: 1 per tile, plus 2 for every turn spent in the air for aircraft
    float FuelToReach(UnitController unit, Vector2 point)
    {
        float tiles = GridUtil.Manhattan(unit.transform.position, point);
        if (unit.unitData == null || !unit.unitData.isAir) return tiles;
        float turns = Mathf.Ceil(tiles / Mathf.Max(1, unit.GetMoveRange()));
        return tiles + 2f * turns;
    }

    // Whether a unit should give up what it's doing and head back to refuel, given the fuel it would take to get there
    bool ShouldHeadBackForFuel(UnitController unit, float fuelToReach)
    {
        int fuel = unit.GetFuel();
        if(fuel <= fuelToReach +5) return true;
        return false;
    }

    // Aircraft parked on a carrier take off again once they're full, onto free tiles beside it.
    // Launching is the carrier's action for the turn
    bool LaunchAircraft(UnitController carrier)
    {
        bool launched = false;
        Vector2 pos = carrier.transform.position;
        for (int i = carrier.cargo.Count - 1; i >= 0; i--)
        {
            UnitController plane = carrier.cargo[i];
            if (plane == null) continue;
            bool full = plane.GetFuelMax() < 0 || plane.GetFuel() >= plane.GetFuelMax();
            if (!full) continue;

            foreach (Vector2 dir in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
            {
                Vector2 tilePos = pos + dir;
                TileFunction tile = GridUtil.TileAt(tilePos);
                if (tile == null || !tile.CanUnitEnter(plane.unitData) || GridUtil.IsOccupied(tilePos)) continue;

                carrier.cargo.RemoveAt(i);
                plane.UnloadTo(tilePos);
                Debug.Log($"[EnemyAI] {plane.name} took off from {carrier.name}");
                launched = true;
                break;
            }
        }
        return launched;
    }

    // Standing on one of the AI's own properties, or next to one of its supply units
    bool IsBeingResupplied(UnitController unit)
    {
        TileFunction tile = GridUtil.TileAt(unit.transform.position);
        if (tile != null && tile.isCaptureable && tile.ownedByEnemy) return true;
        return HasSupplierBeside(unit, null);
    }

    bool HasSupplierBeside(UnitController unit, UnitController ignore)
    {
        Vector2 pos = unit.transform.position;
        foreach (Vector2 dir in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
        {
            UnitController other = GridUtil.UnitAt(pos + dir);
            if (other != null && other != ignore && other.isEnemy && other.CanSupplyOthers()) return true;
        }
        return false;
    }

    // Whether a unit is low enough on supplies that a supply unit should come to it
    bool NeedsSupply(UnitController unit)
    {
        int A;
        int unitFuel = unit.GetFuel();
        if (unit.unitData.isAir)
        {
            A = 10; 
        }
        else
        {
            A = 0;
        }
        // ammoCap <= 0 = doesn't use ammo (it still spawns with 1), fuel -1 = infinite
        if ((unit.ammo > 1 || unit.unitData.ammoCap <= 0) && (unitFuel >= 15 + A || unitFuel <= -1))
            return false;
        else return true;
    }
    #endregion

    #region Transports
    // Unarmed transports: supply duty first, then ferry slower units from the AI's producing bases
    IEnumerator ProcessTransport(UnitController t)
    {
        if (t.cargo.Count > 0)
        {
            yield return StartCoroutine(DeliverCargo(t));
            yield break;
        }

        HashSet<Vector2> ownBases = OwnProducingBases();

        // 1. Go beside the nearest unit that needs supplying and has no supplier next to it yet
        if (t.CanSupplyOthers())
        {
            UnitController needy = FindUnitNeedingSupply(t);
            if (needy != null)
            {
                claimed.Add(needy.transform.position);
                Debug.Log($"[EnemyAI] {t.name} going to supply {needy.name}");
                yield return StartCoroutine(MoveToward(t, needy.transform.position, ownBases));
                if (!t.IsDead) t.EndTurn();
                yield break;
            }
        }

        // 2. Pick up a slower unit waiting on one of the AI's producing bases
        UnitController passenger = FindPassenger(t);
        if (passenger != null)
        {
            Vector2 p = passenger.transform.position;
            claimed.Add(p);
            if (t.GetReachableTiles(true).ContainsKey(p))
            {
                yield return t.MoveTo(p);
                if (!t.IsDead && !t.WasAmbushed && passenger.CanLoadOnto(t))
                {
                    passenger.LoadOnto(t);
                    flankPlan[t] = Random.value < flankChance; // decided once per trip
                    Debug.Log($"[EnemyAI] {t.name} picked up {passenger.name}, {(flankPlan[t] ? "flanking to the HQ" : "joining the army")}");
                }
            }
            else
            {
                yield return StartCoroutine(MoveToward(t, p, ownBases));
            }
            if (!t.IsDead) t.EndTurn();
            yield break;
        }

        // 3. Nothing to carry yet: wait beside the nearest producing base (not on it, so it can still build)
        Vector2 tPos = t.transform.position;
        Vector2? nearestBase = null;
        float bestDist = float.MaxValue;
        foreach (Vector2 b in ownBases)
        {
            float d = GridUtil.Manhattan(tPos, b);
            if (d < bestDist) { bestDist = d; nearestBase = b; }
        }
        if (nearestBase != null && bestDist > 1)
            yield return StartCoroutine(MoveToward(t, nearestBase.Value, ownBases));
        else if (bestDist == 0)
            yield return StartCoroutine(StepOffOwnBase(t)); // just built here: make room for the next unit
        if (!t.IsDead) t.EndTurn();
    }

    // Nearest AI unit that needs supplying, with no other supplier beside it, that the transport can get next to
    UnitController FindUnitNeedingSupply(UnitController t)
    {
        UnitController best = null;
        float bestDist = float.MaxValue;
        foreach (UnitController u in FindObjectsByType<UnitController>(FindObjectsSortMode.None))
        {
            if (!u.isEnemy || u == t || u.isLoaded || u.IsDead) continue;
            if (!NeedsSupply(u) || HasSupplierBeside(u, t)) continue;
            if (claimed.Contains(u.transform.position)) continue;

            TileFunction under = GridUtil.TileAt(u.transform.position);
            if (under != null && under.isCaptureable && under.ownedByEnemy) continue; // its property refills it
            if (!CanStandBeside(t, u.transform.position)) continue;

            float d = GridUtil.Manhattan(t.transform.position, u.transform.position);
            if (d < bestDist) { bestDist = d; best = u; }
        }
        return best;
    }

    bool CanStandBeside(UnitController t, Vector2 pos)
    {
        foreach (Vector2 dir in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
        {
            TileFunction tile = GridUtil.TileAt(pos + dir);
            if (tile != null && tile.CanUnitEnter(t.unitData)) return true;
        }
        return false;
    }

    // Nearest unit on one of the AI's producing bases that's slower than the transport and fits in it
    UnitController FindPassenger(UnitController t)
    {
        HashSet<Vector2> ownBases = OwnProducingBases();
        UnitController best = null;
        float bestDist = float.MaxValue;
        foreach (UnitController u in FindObjectsByType<UnitController>(FindObjectsSortMode.None))
        {
            if (!u.isEnemy || u == t || u.isLoaded || u.IsDead || u.HasMoved) continue;
            Vector2 pos = u.transform.position;
            if (!ownBases.Contains(pos) || claimed.Contains(pos)) continue;
            if (u.GetMoveRange() >= t.GetMoveRange() || !u.CanLoadOnto(t)) continue;

            TileFunction tile = GridUtil.TileAt(pos);
            if (tile == null || !tile.CanUnitEnter(t.unitData)) continue; // e.g. a Water Support can't reach a land base

            float d = GridUtil.Manhattan(t.transform.position, pos);
            if (d < bestDist) { bestDist = d; best = u; }
        }
        return best;
    }

    // Carry the passenger toward the main army (or the player's HQ when flanking) and drop it off near there
    IEnumerator DeliverCargo(UnitController t)
    {
        bool flank = flankPlan.TryGetValue(t, out bool f) && f;
        TileFunction hq = flank ? PlayerHQ() : null;
        Vector2? destination = hq != null ? (Vector2)hq.transform.position : ArmyCenter(t);
        if (destination == null) destination = t.transform.position; // no army to join: drop off here

        Vector2 before = t.transform.position;
        yield return StartCoroutine(MoveToward(t, destination.Value));
        if (t.IsDead) yield break;

        bool closeEnough = GridUtil.Manhattan(t.transform.position, destination.Value) <= dropDistance;
        bool cantGetCloser = (Vector2)t.transform.position == before;
        if (!t.WasAmbushed && (closeEnough || cantGetCloser))
            DropCargo(t, destination.Value, flank);

        t.EndTurn();
    }

    // Unloads everything it can onto free tiles next to the transport, closest to the destination first
    void DropCargo(UnitController t, Vector2 destination, bool flank)
    {
        Vector2 tPos = t.transform.position;
        while (t.cargo.Count > 0)
        {
            UnitController cargoUnit = t.cargo[t.cargo.Count - 1];
            Vector2? bestTile = null;
            float bestDist = float.MaxValue;
            foreach (Vector2 dir in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
            {
                Vector2 pos = tPos + dir;
                TileFunction tile = GridUtil.TileAt(pos);
                if (tile == null || !tile.CanUnitEnter(cargoUnit.unitData) || GridUtil.IsOccupied(pos)) continue;
                float d = GridUtil.Manhattan(pos, destination);
                if (d < bestDist) { bestDist = d; bestTile = pos; }
            }
            if (bestTile == null) break; // no room: try again next turn

            t.cargo.RemoveAt(t.cargo.Count - 1);
            cargoUnit.UnloadTo(bestTile.Value);
            if (flank) flankers.Add(cargoUnit);
            Debug.Log($"[EnemyAI] {t.name} dropped off {cargoUnit.name}");
        }
        if (t.cargo.Count == 0) flankPlan.Remove(t);
    }

    // Where most of the AI's fighting units are: the one with the most others within 4 tiles
    Vector2? ArmyCenter(UnitController exclude)
    {
        HashSet<Vector2> ownBases = OwnProducingBases();
        List<UnitController> army = new List<UnitController>();
        foreach (UnitController u in FindObjectsByType<UnitController>(FindObjectsSortMode.None))
            if (u.isEnemy && u != exclude && !u.isLoaded && !u.IsDead && !u.IsUnarmed
                && !ownBases.Contains(u.transform.position))
                army.Add(u);

        UnitController best = null;
        int bestCount = -1;
        foreach (UnitController a in army)
        {
            int count = 0;
            foreach (UnitController b in army)
                if (GridUtil.Manhattan(a.transform.position, b.transform.position) <= 4) count++;
            if (count > bestCount) { bestCount = count; best = a; }
        }
        return best != null ? (Vector2?)best.transform.position : null;
    }
    #endregion

    #region Trappers and Mine Layers
    IEnumerator ProcessTrapUnit(UnitController unit)
    {
        // Standing on one of the player's traps (trap units are immune, so it's still there): disarming gives a trap back
        if (unit.IsOnEnemyTrap() && unit.CanUseTrapHere())
        {
            unit.UseTrapAbility();
            Debug.Log($"[EnemyAI] {unit.name} disarmed a trap it was standing on");
            unit.EndTurn();
            yield break;
        }

        // Out of traps: restock by disarming if the player fields the same kind of unit, otherwise the normal way.
        // Either way it doesn't fight
        if (unit.ammo <= 0 || unit.trapPrefab == null)
        {
            if (opponentTrapKinds.Contains(unit.unitData))
                yield return StartCoroutine(HuntEnemyTraps(unit));
            else
                yield return StartCoroutine(GoResupply(unit));
            yield break;
        }

        // Lay a trap on the best spot it can reach this turn
        if (FindBestTrapSpot(unit, unit.GetReachableTiles().Keys, true, out Vector2 spot))
        {
            if (spot != (Vector2)unit.transform.position) yield return unit.MoveTo(spot);
            if (unit.IsDead) yield break;
            if (!unit.WasAmbushed && unit.CanUseTrapHere())
            {
                unit.UseTrapAbility();
                Debug.Log($"[EnemyAI] {unit.name} laid a trap at {spot}");
            }
            unit.EndTurn();
            yield break;
        }

        // Nothing worth trapping in reach: head for the best spot on the map, to trap it on a later turn
        if (FindBestTrapSpot(unit, AllTilePositions(), true, out Vector2 farSpot))
            yield return StartCoroutine(MoveToward(unit, farSpot));
        if (!unit.IsDead) unit.EndTurn();
    }

    // Best tile out of the candidates to lay a trap on: somewhere a vehicle or ship could set it off, that this
    // unit can stand on, with none of the AI's own traps on or next to it. Scored by TrapSpotScore (above 0 to count)
    // against the player's traffic, since those are the vehicles the trap is for
    bool FindBestTrapSpot(UnitController unit, IEnumerable<Vector2> candidates, bool claim, out Vector2 best)
    {
        List<Vector2> objectives = KeyObjectives();
        List<Vector2> ownTraps = TrapPositions(false);
        Vector2 unitPos = unit.transform.position;

        best = unitPos;
        float bestScore = 0f;
        bool found = false;
        foreach (Vector2 pos in candidates)
        {
            if (!UnitController.CanTrapTile(pos) || claimed.Contains(pos)) continue;
            TileFunction tile = GridUtil.TileAt(pos);
            if (tile == null || !tile.CanUnitEnter(unit.unitData)) continue;
            if (pos != unitPos && GridUtil.IsOccupied(pos, unit)) continue;
            if (NearestDistance(pos, ownTraps) <= 1) continue; // spread traps out

            float score = TrapSpotScore(NearestDistance(pos, objectives), TrafficMap.Heat(pos, true));
            if (score > bestScore)
            {
                bestScore = score;
                best = pos;
                found = true;
            }
        }

        if (found && claim) claimed.Add(best);
        return found;
    }

    // How good a tile is for a trap. objectiveDistance: tiles to the nearest unit-making base or HQ (either side).
    // heat: how much of the opponent's vehicle and ship traffic has passed over it lately (recent trips count most)
    float TrapSpotScore(float objectiveDistance, float heat)
    {
        // Either reason is enough: up to 5 points for being within 4 tiles of a base or HQ,
        // plus 2 points per unit of recent traffic, so a well-used road can beat a quiet spot by a base
        float nearObjective = Mathf.Max(0f, 5f - objectiveDistance);
        return nearObjective + heat * 1f;
    }

    // Out of traps while the player has fielded the same kind of trap unit: find their traps and disarm them.
    // Goes for traps it can see first, then searches where the player would most likely have laid them
    IEnumerator HuntEnemyTraps(UnitController unit)
    {
        Vector2? target = NearestVisibleEnemyTrap(unit);
        if (target == null) target = LikelyEnemyTrapSpot(unit);
        if (target == null)
        {
            // Searched everywhere likely: restock the normal way
            yield return StartCoroutine(GoResupply(unit));
            yield break;
        }

        claimed.Add(target.Value);
        if (unit.GetReachableTiles().ContainsKey(target.Value)) yield return unit.MoveTo(target.Value);
        else yield return StartCoroutine(MoveToward(unit, target.Value));
        if (unit.IsDead) yield break;

        sweptOnTurn[(Vector2)unit.transform.position] = turnNumber;
        if (!unit.WasAmbushed && unit.IsOnEnemyTrap() && unit.CanUseTrapHere())
        {
            unit.UseTrapAbility();
            Debug.Log($"[EnemyAI] {unit.name} found and disarmed a trap");
        }
        unit.EndTurn();
    }

    // Nearest of the player's traps the AI can see (spotted by a unit that sees hidden things) that this unit can reach
    Vector2? NearestVisibleEnemyTrap(UnitController unit)
    {
        Vector2? best = null;
        float bestDist = float.MaxValue;
        foreach (Trap trap in FindObjectsByType<Trap>(FindObjectsSortMode.None))
        {
            if (!(trap.isPlayer || trap.isAlly) || trap.IsHiddenFrom(false)) continue;
            Vector2 pos = trap.transform.position;
            TileFunction tile = GridUtil.TileAt(pos);
            if (tile == null || !tile.CanUnitEnter(unit.unitData) || claimed.Contains(pos)) continue;
            if (GridUtil.IsOccupied(pos, unit)) continue;

            float d = GridUtil.Manhattan(unit.transform.position, pos);
            if (d < bestDist) { bestDist = d; best = pos; }
        }
        return best;
    }

    // Where the player would most likely have laid a trap: the same scoring the AI uses for its own,
    // but against the AI's own traffic (what the player's traps are aimed at). Skips recently searched tiles
    Vector2? LikelyEnemyTrapSpot(UnitController unit)
    {
        List<Vector2> objectives = KeyObjectives();
        Vector2? best = null;
        float bestScore = 0f;
        foreach (Vector2 pos in AllTilePositions())
        {
            if (!UnitController.CanTrapTile(pos) || claimed.Contains(pos)) continue;
            if (sweptOnTurn.TryGetValue(pos, out int turn) && turnNumber - turn < resweepAfterTurns) continue;
            TileFunction tile = GridUtil.TileAt(pos);
            if (tile == null || !tile.CanUnitEnter(unit.unitData) || GridUtil.IsOccupied(pos, unit)) continue;

            float score = TrapSpotScore(NearestDistance(pos, objectives), TrafficMap.Heat(pos, false));
            if (score > bestScore) { bestScore = score; best = pos; }
        }
        return best;
    }

    // Unit-making bases and HQs on both sides
    List<Vector2> KeyObjectives()
    {
        List<Vector2> result = new List<Vector2>();
        foreach (TileFunction tile in FindObjectsByType<TileFunction>(FindObjectsSortMode.None))
            if (tile.isHQ || tile.GetComponent<UnitSpawner>() != null) result.Add(tile.transform.position);
        return result;
    }

    List<Vector2> TrapPositions(bool playerSide)
    {
        List<Vector2> result = new List<Vector2>();
        foreach (Trap trap in FindObjectsByType<Trap>(FindObjectsSortMode.None))
            if (playerSide ? (trap.isPlayer || trap.isAlly) : trap.isEnemy) result.Add(trap.transform.position);
        return result;
    }

    List<Vector2> AllTilePositions()
    {
        List<Vector2> result = new List<Vector2>();
        foreach (TileFunction tile in FindObjectsByType<TileFunction>(FindObjectsSortMode.None))
            result.Add(tile.transform.position);
        return result;
    }

    float NearestDistance(Vector2 pos, List<Vector2> points)
    {
        float best = float.MaxValue;
        foreach (Vector2 p in points) best = Mathf.Min(best, GridUtil.Manhattan(pos, p));
        return best;
    }
    #endregion

    // Nearest player unit the seeker could actually attack (right movement type, a weapon that can fire)
    UnitController FindNearestPlayerUnit(UnitController seeker)
    {
        UnitController[] allUnits = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
        UnitController nearest = null;
        float minDist = float.MaxValue;

        foreach (UnitController u in allUnits)
        {
            if (!u.isPlayer) continue;
            if (u.isLoaded) continue; // inside a transport, so not really on the map
            if (u.IsInvisibleAndUndetected(false)) continue; // AI can't target what it can't detect
            if (!seeker.CanHitTarget(u)) continue; // e.g. a SAM ignores ground units instead of chasing them

            float dist = GridUtil.Manhattan(seeker.transform.position, u.transform.position);
            if (dist < minDist) { minDist = dist; nearest = u; }
        }
        return nearest;
    }
    // Picks the tile closest to the destination out of the tiles this unit can actually walk to this turn,
    // skipping any tiles in avoid
    Vector2 FindBestMoveToward(UnitController unit, Vector2 destination, HashSet<Vector2> avoid = null)
    {
        Vector2 unitPos = unit.transform.position;
        Vector2 bestPos = unitPos;
        float bestDist = GridUtil.Manhattan(unitPos, destination);

        foreach (Vector2 tilePos in unit.GetReachableTiles().Keys)
        {
            if (avoid != null && avoid.Contains(tilePos)) continue;
            float distToDest = GridUtil.Manhattan(tilePos, destination);
            if (distToDest < bestDist)
            {
                bestDist = distToDest;
                bestPos = tilePos;
            }
        }
        return bestPos;
    }
    
}
