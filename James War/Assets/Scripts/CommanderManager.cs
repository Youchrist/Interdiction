using UnityEngine;

public class CommanderManager : MonoBehaviour
{
    public static CommanderManager Instance;

    public int playerResource = 0;
    public int enemyResource = 0;

    public int PlayerResourceMax => playerCommander != null ? playerCommander.abilityCost : 100;
    public int EnemyResourceMax => enemyCommander != null ? enemyCommander.abilityCost : 100;

    public CommanderData playerCommander;
    public CommanderData enemyCommander;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            // Commanders picked on the select screen. The Inspector values stay as a fallback
            // so MapScene can still be played directly for testing.
            if (GameSettings.PlayerCommander != null) playerCommander = GameSettings.PlayerCommander;
            if (GameSettings.EnemyCommander != null) enemyCommander = GameSettings.EnemyCommander;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    public float GetAttackMult(UnitTypeExtra.UnitCategory category, bool isPlayerUnit)
    {
        CommanderData data = isPlayerUnit ? playerCommander : enemyCommander;
        if (data == null)
        {
            return 1f;
        }

        return category switch
        {
            UnitTypeExtra.UnitCategory.Trooper => data.trooperAttackModifier,
            UnitTypeExtra.UnitCategory.Vehicle => data.vehicleAttackModifier,
            UnitTypeExtra.UnitCategory.Transport => data.transportAttackModifier,
            UnitTypeExtra.UnitCategory.Air => data.airAttackModifier,
            UnitTypeExtra.UnitCategory.Naval => data.navalAttackModifier,
            _ => 1f
        };
    }

    public void AddPlayerResource(int amount)
    {
        playerResource = Mathf.Min(playerResource + amount, PlayerResourceMax);
        Debug.Log($"Player resrouce: {playerResource}/{PlayerResourceMax}");
    }
    public void AddEnemyResource(int amount)
    {
        enemyResource = Mathf.Min(enemyResource + amount, EnemyResourceMax);
        Debug.Log($"Enemy resrouce: {enemyResource}/{EnemyResourceMax}");
    }
    public bool PlayerAbilityReady() => playerCommander != null && playerResource >= playerCommander.abilityCost;
    public bool EnemyAbilityReady() => enemyCommander != null && enemyResource >= enemyCommander.abilityCost;
    public void ConsumePlayerAbility()
    { 
        if (PlayerAbilityReady()) playerResource = 0; 
    }
    public void ConsumeEnemyAbility() 
    {
        if (EnemyAbilityReady()) enemyResource = 0; 
    }
    public float GetDefenceMult(UnitTypeExtra.UnitCategory category, bool isPlayerUnit)
    {
        CommanderData data = isPlayerUnit ? playerCommander : enemyCommander;
        if (data == null)
        {
            return 1f;
        }

        return category switch
        {
            UnitTypeExtra.UnitCategory.Trooper => data.trooperDefenceModifier,
            UnitTypeExtra.UnitCategory.Vehicle => data.vehicleDefenceModifier,
            UnitTypeExtra.UnitCategory.Transport => data.transportDefenceModifier,
            UnitTypeExtra.UnitCategory.Air => data.airDefenceModifier,
            UnitTypeExtra.UnitCategory.Naval => data.navalDefenceModifier,
            _ => 1f
        };
    }
    public int GetMoveBonus(UnitTypeExtra.UnitCategory category, bool isPlayerUnit)
    {
        CommanderData data = isPlayerUnit ? playerCommander : enemyCommander;
        if (data == null)
        {
            return 0;
        }

        return category switch
        {
            UnitTypeExtra.UnitCategory.Trooper => data.trooperMoveModifier,
            UnitTypeExtra.UnitCategory.Vehicle => data.vehicleMoveModifier,
            UnitTypeExtra.UnitCategory.Transport => data.transportMoveModifier,
            UnitTypeExtra.UnitCategory.Air => data.airMoveModifier,
            UnitTypeExtra.UnitCategory.Naval => data.navalMoveModifier,
            _ => 0
        };
    }
    public int GetMinRangeBonus(UnitTypeExtra.UnitCategory category, bool isPlayerUnit)
    {
        CommanderData data = isPlayerUnit ? playerCommander : enemyCommander;
        if (data == null)
        {
            return 0;
        }

        return category switch
        {
            UnitTypeExtra.UnitCategory.Trooper => data.trooperMinRangeModifier,
            UnitTypeExtra.UnitCategory.Vehicle => data.vehicleMinRangeModifier,
            //UnitTypeExtra.UnitCategory.Transport => data.transportMinRangeModifier,
            UnitTypeExtra.UnitCategory.Air => data.airMinRangeModifier,
            UnitTypeExtra.UnitCategory.Naval => data.navalMinRangeModifier,
            _ => 0
        };
    }
    public int GetMaxRangeBonus(UnitTypeExtra.UnitCategory category, bool isPlayerUnit)
    {
        CommanderData data = isPlayerUnit ? playerCommander : enemyCommander;
        if (data == null)
        {
            return 0;
        }

        return category switch
        {
            UnitTypeExtra.UnitCategory.Trooper => data.trooperMaxRangeModifier,
            UnitTypeExtra.UnitCategory.Vehicle => data.vehicleMaxRangeModifier,
            //UnitTypeExtra.UnitCategory.Transport => data.transportMaxRangeModifier,
            UnitTypeExtra.UnitCategory.Air => data.airMaxRangeModifier,
            UnitTypeExtra.UnitCategory.Naval => data.navalMaxRangeModifier,
            _ => 0
        };
    }
    public int GetMaxAmmoBonus(UnitTypeExtra.UnitCategory category, bool isPlayerUnit)
    {
        CommanderData data = isPlayerUnit ? playerCommander : enemyCommander;
        if (data == null)
        {
            return 0;
        }

        return category switch
        {
            UnitTypeExtra.UnitCategory.Trooper => data.trooperMaxAmmoModifier,
            UnitTypeExtra.UnitCategory.Vehicle => data.vehicleMaxAmmoModifier,
            //UnitTypeExtra.UnitCategory.Transport => data.transportMaxAmmoModifier,
            UnitTypeExtra.UnitCategory.Air => data.airMaxAmmoModifier,
            UnitTypeExtra.UnitCategory.Naval => data.navalMaxAmmoModifier,
            _ => 0
        };
    }

    public int GetMaxFuelBonus(UnitTypeExtra.UnitCategory category, bool isPlayerUnit)
    {
        CommanderData data = isPlayerUnit ? playerCommander : enemyCommander;
        if (data == null)
        {
            return 0;
        }

        return category switch
        {
            UnitTypeExtra.UnitCategory.Trooper => data.trooperMaxFuelModifier,
            UnitTypeExtra.UnitCategory.Vehicle => data.vehicleMaxFuelModifier,
            UnitTypeExtra.UnitCategory.Transport => data.transportMaxFuelModifier,
            UnitTypeExtra.UnitCategory.Air => data.airMaxFuelModifier,
            UnitTypeExtra.UnitCategory.Naval => data.navalMaxFuelModifier,
            _ => 0
        };
    }
    public int GetVisionBonus(UnitTypeExtra.UnitCategory category, bool isPlayerUnit)
    {
        CommanderData data = isPlayerUnit ? playerCommander : enemyCommander;
        if (data == null)
        {
            return 0;
        }

        return category switch
        {
            UnitTypeExtra.UnitCategory.Trooper => data.trooperVisionModifier,
            UnitTypeExtra.UnitCategory.Vehicle => data.vehicleVisionModifier,
            UnitTypeExtra.UnitCategory.Transport => data.transportVisionModifier,
            UnitTypeExtra.UnitCategory.Air => data.airVisionModifier,
            UnitTypeExtra.UnitCategory.Naval => data.navalVisionModifier,
            _ => 0
        };
    }
}
