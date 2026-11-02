using UnityEngine;

[CreateAssetMenu(fileName = "CommanderData", menuName = "Scriptable Objects/CommanderData")]
public class CommanderData : ScriptableObject
{
    public string commanderName;
    public Sprite portrait;
    [TextArea] public string passiveDescription;

    [Header("Trooper Passive")]
    public float trooperAttackModifier = 1f;
    public float trooperDefenceModifier = 1f;
    public int trooperMoveModifier = 0;
    public int trooperMinRangeModifier = 0;
    public int trooperMaxRangeModifier = 0;
    public int trooperMaxAmmoModifier = 0;
    public int trooperMaxFuelModifier = 0;
    public int trooperVisionModifier = 0;

    [Header("Vehicle Passive")]
    public float vehicleAttackModifier = 1f;
    public float vehicleDefenceModifier = 1f;
    public int vehicleMoveModifier = 0;
    public int vehicleMinRangeModifier = 0;
    public int vehicleMaxRangeModifier = 0;
    public int vehicleMaxAmmoModifier = 0;
    public int vehicleMaxFuelModifier = 0;
    public int vehicleVisionModifier = 0;

    [Header("Transport Passive")]
    public float transportAttackModifier = 1f;
    public float transportDefenceModifier = 1f;
    public int transportMoveModifier = 0;
    //public int transportlMinRangeModifier = 0;
    //public int transportMaxRangeModifier = 0;
    //public int transportMaxAmmoModifier = 0;
    public int transportMaxFuelModifier = 0;
    public int transportVisionModifier = 0;

    [Header("Air Passive")]
    public float airAttackModifier = 1f;
    public float airDefenceModifier = 1f;
    public int airMoveModifier = 0;
    public int airMinRangeModifier = 0;
    public int airMaxRangeModifier = 0;
    public int airMaxAmmoModifier = 0;
    public int airMaxFuelModifier = 0;
    public int airVisionModifier = 0;

    [Header("Naval Passive")]
    public float navalAttackModifier = 1f;
    public float navalDefenceModifier = 1f;
    public int navalMoveModifier = 0;
    public int navalMinRangeModifier = 0;
    public int navalMaxRangeModifier = 0;
    public int navalMaxAmmoModifier = 0;
    public int navalMaxFuelModifier = 0;
    public int navalVisionModifier = 0;

    [Header("Ability")]
    public string abilityName;
    [TextArea] public string abilityDescription;
    public int abilityCost;
    [Tooltip("Tells the enemy AI when to use this ability")]
    public AbilityCategory abilityCategory;

    public enum AbilityCategory
    {
        None,       // no ability
        Summoning,  // AI waits until it can summon the most units
        Combat,     // AI uses it when lots of its units will fight, or it can deal heavy damage
        Sabotage,   // AI uses it when it can deny the opponent a lot
        Economy     // AI uses it as soon as it's ready
    }
}
