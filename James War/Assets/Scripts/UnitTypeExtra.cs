using UnityEngine;

[CreateAssetMenu(fileName = "UnitType", menuName = "Unit")]
public class UnitTypeExtra : ScriptableObject
{
    public enum UnitCategory
    {
        Trooper,    //Footman(basic), Scout, Mortar, Trapper, AntiVic
        Vehicle,    //Tank, AntiAir, Artillery, Rocket, SAM, 
        Transport,  //Truck(basic), WTransport(basic), ASupport(basic)
        Air,        //Drone, Helicopter, Bomber, Fighter
        Naval,      //Submarine, Cruiser, Frigate, Destroyer, Battleship, AirCarrier, MineLayers?
        Trap        // LTrap, WTrap
    }
    public enum CargoType
    {
        Infantry,   // Trooper category only: Truck, Helicopter
        LandUnits,  // any ground unit: Water Support, Air Support
        AirUnits    // any air unit, refuelled and resupplied on boarding: Air Carrier
    }
    [Header ("Name and Sprite")]
    public GameObject prefab;
    public string unitName;
    [Tooltip("A few letters shown on the unit in its team's colour (placeholder until every unit has its own art)")]
    public string shortName;
    public UnitCategory unitCategory;
    [Tooltip("Basic units are unlocked from the start of the game and never need researching")]
    public bool isBasic;
    [Header ("Stats")]
    public int moveRange;
    public int visionRange;
    public int attackRangeMin;
    public int attackRangeMax;
    public int ammoCap;
    public int fuelCap;
    public int price;
    public float primaryAttackPower;
    public float secondaryAttackPower;
    public float defencePower;
    public float health;
    [Header("Tile Movement")]
    public bool isAir;
    public bool isGround;
    public bool isWater;
    [Tooltip("Wheeled vehicles can't enter forest, hills or mountains")]
    public bool isWheeled;
    [Tooltip("Treaded vehicles can't enter mountains")]
    public bool isTreaded;
    [Header("Abilities")]
    public bool isTrapImmune;
    public bool canCapture;
    public bool isRanged;
    public bool isMelee;
    public bool canSeeInvis;
    public bool isInvis;
    public bool canTrap;
    public bool canSupply;
    public bool canTransport;
    [Header("Weapon Targets (easiest to set in James War > Weapon Targets)")]
    [Tooltip("Kinds of unit the primary weapon can hit")]
    public bool primaryHitsAir;
    public bool primaryHitsGround;
    public bool primaryHitsWater;
    [Tooltip("Kinds of unit the secondary weapon can hit")]
    public bool secondaryHitsAir;
    public bool secondaryHitsGround;
    public bool secondaryHitsWater;
    [Header("Transport")]
    public int cargoCapacity = 1;
    [Tooltip("What this transport can carry. A water transport that carries Land Units also works as a bridge for land units")]
    public CargoType cargoType;

    // Whether that weapon exists and is set to hit this kind of unit. A unit that's more than one kind
    // (the Prototype is ground and water) is only hit by a weapon ticked for all of its kinds
    public bool WeaponHits(bool primary, UnitTypeExtra target)
    {
        if ((primary ? primaryAttackPower : secondaryAttackPower) <= 0) return false; // no such weapon
        if (target == null) return true;

        bool air = primary ? primaryHitsAir : secondaryHitsAir;
        bool ground = primary ? primaryHitsGround : secondaryHitsGround;
        bool water = primary ? primaryHitsWater : secondaryHitsWater;
        if (target.isAir && !air) return false;
        if (target.isGround && !ground) return false;
        if (target.isWater && !water) return false;
        return true;
    }
}


