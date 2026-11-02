using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using TMPro;
using UnityEngine;

public class TileFunction : MonoBehaviour
{
    #region Ints/Floats/Bools/etc
    [Header("Capture")]
    public bool isCaptureable;
    public bool ownedByPlayer;
    public bool ownedByEnemy;
    public bool ownedByNeutral = true;

    public bool isBase;
    public bool isHQ;
    public bool isPlayerHQ;
    public bool isEnemyHQ;

    [Header("Land")]
    public bool isRoad;
    public bool isGrass;
    public bool isForest;
    public bool isHill;
    public bool isMountain;

    [Header("Water")]
    public bool isWater;
    public bool isFog;
    public bool isBasaltPillar;

    [Header("Look")]
    [Tooltip("Colour the tile from its terrain (and owner, for bases and HQs) when the map starts. Placeholder until tile art exists")]
    public bool autoColor = true;

    private bool isFogged = false;

    private Color fogColor = new Color(0.05f, 0.05f, 0.1f, 0.5f);
    public int GetCapturePoints() => capturePoints;

    private int capturePoints = 20;
    private const int maxCapturePoints = 20;
    private UnitController capturingUnit = null;
    public TextMeshPro captureText;

    private int tileDefence;

    private SpriteRenderer sr;
    private Collider2D col;
    private Color originalColor;
    private UnitSpawner spawner;
    #endregion
    // Set up the look in Awake, so other scripts' Start (like the fog refresh) can colour this tile safely
    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        col = GetComponent<Collider2D>();
        spawner = GetComponent<UnitSpawner>();
        FillInPropertyFlags();
        originalColor = sr.color;
        RefreshLook();
    }

    // Fills in property boxes that follow from the others, so a base or HQ set up with only some of its boxes
    // ticked still works everywhere (income, captures, the head start, the AI, its icon):
    //   a tile with a UnitSpawner is a base; ticking Is Player HQ or Is Enemy HQ makes it an HQ; both can be captured;
    //   a property with no owner ticked takes its owner from its spawner's side or its HQ side.
    // The tile's owner is then copied to its spawner, since captures change the tile and the AI builds from the spawner
    void FillInPropertyFlags()
    {
        if (spawner != null) isBase = true;
        if (isPlayerHQ || isEnemyHQ) isHQ = true;
        if (isBase || isHQ) isCaptureable = true;

        if (!ownedByPlayer && !ownedByEnemy)
        {
            if (isPlayerHQ || (spawner != null && spawner.isPlayer)) ownedByPlayer = true;
            else if (isEnemyHQ || (spawner != null && spawner.isEnemy)) ownedByEnemy = true;
        }
        if (ownedByPlayer || ownedByEnemy) ownedByNeutral = false;

        if (spawner != null && !spawner.isAlly)
        {
            spawner.isPlayer = ownedByPlayer;
            spawner.isEnemy = ownedByEnemy;
            spawner.isNeutral = !ownedByPlayer && !ownedByEnemy;
        }
    }

    // Start is called before the first frame update
    void Start()
    {
        if (isRoad || isWater)
        {
            tileDefence = 0;
        }
        else if (isGrass || isFog)
        {
            tileDefence = 10;
        }
        else if (isForest)
        {
            tileDefence = 20;
        }
        else if (isHill || isBasaltPillar)
        {
            tileDefence = 30;
        }
        else if (isMountain || isBase)
        {
            tileDefence = 40;
        }
        UpdateCaptureDisplay();
    }
    #region property functions
    public bool AttemptCapture(UnitController unit)
    {
        if (!isCaptureable) return false;
        if (unit == null || unit.unitData == null || !unit.unitData.canCapture) return false;

        // Can't capture what you already own
        if (ownedByPlayer && (unit.isPlayer || unit.isAlly)) return false;
        if (ownedByEnemy && unit.isEnemy) return false;

        // A different unit starting a capture begins from scratch
        if (capturingUnit != unit)
        {
            capturePoints = maxCapturePoints;
            capturingUnit = unit;
        }

        // Advance Wars style: floor(HP% * 10), minimum 1
        int captureReduction = Mathf.Max(1, Mathf.FloorToInt(unit.GetHealthPercent() * 10));
        capturePoints -= captureReduction;
        Debug.Log($"{unit.name} capturing tile | capture points left: {capturePoints}");
        UpdateCaptureDisplay();

        if (capturePoints <= 0)
        {
            CompleteCapture(unit);
            return true;
        }
        return false;
    }

    // Capture progress is lost if the capturing unit moved off the tile or died
    public void ResetIfCapturerLeft()
    {
        if (capturePoints == maxCapturePoints) return;
        bool stillHere = capturingUnit != null && !capturingUnit.isLoaded
                         && col != null && col.OverlapPoint(capturingUnit.transform.position);
        if (!stillHere) ResetCaptureProgress();
    }

    public void ResetCaptureProgress()
    {
        capturePoints = maxCapturePoints;
        capturingUnit = null;
        Debug.Log("Capture progress reset");
        UpdateCaptureDisplay();
    }

    // Placeholder look: one colour per terrain type, and owner colours for properties
    // (blue = player, red = enemy, grey = neutral; HQs are a stronger shade)
    public void RefreshLook()
    {
        if (sr == null) return;
        Sprite baseSprite = BaseSprite();
        if (baseSprite != null) sr.sprite = baseSprite;

        if (!autoColor) return;
        originalColor = LookColor();
        sr.color = isFogged ? fogColor : originalColor;
    }

    // HQs show a star, bases a building for the kind of unit they make (factory, airfield, port), and other
    // capturable tiles (cities: earn income, build nothing) houses. All are tinted with the owner's colour by
    // LookColor. The sprites are in Assets/Resources/BaseSprites, so no setup is needed
    private static Sprite groundBaseSprite, airBaseSprite, seaBaseSprite, hqSprite, citySprite;
    Sprite BaseSprite()
    {
        if (!isHQ && spawner == null && !isCaptureable) return null;
        if (groundBaseSprite == null)
        {
            groundBaseSprite = Resources.Load<Sprite>("BaseSprites/Base_Ground");
            airBaseSprite = Resources.Load<Sprite>("BaseSprites/Base_Air");
            seaBaseSprite = Resources.Load<Sprite>("BaseSprites/Base_Sea");
            hqSprite = Resources.Load<Sprite>("BaseSprites/Base_HQ");
            citySprite = Resources.Load<Sprite>("BaseSprites/Base_City");
        }
        if (isHQ) return hqSprite; // the capture target stands out even if it also builds units
        if (spawner == null) return citySprite;
        if (spawner.isSeaSpawner) return seaBaseSprite;
        if (spawner.isAirSpawner) return airBaseSprite;
        return groundBaseSprite;
    }

    Color LookColor()
    {
        if (isCaptureable || isBase || isHQ)
        {
            if (ownedByPlayer) return isHQ ? new Color(0.15f, 0.35f, 0.95f) : new Color(0.45f, 0.62f, 1f);
            if (ownedByEnemy) return isHQ ? new Color(0.85f, 0.12f, 0.12f) : new Color(1f, 0.5f, 0.5f);
            return new Color(0.78f, 0.78f, 0.78f);
        }
        if (isRoad) return new Color(0.72f, 0.68f, 0.56f);
        if (isMountain) return new Color(0.48f, 0.45f, 0.44f);
        if (isHill) return new Color(0.6f, 0.48f, 0.28f);
        if (isForest) return new Color(0.12f, 0.36f, 0.15f);
        if (isGrass) return new Color(0.4f, 0.66f, 0.3f);
        if (isBasaltPillar) return new Color(0.22f, 0.22f, 0.28f);
        if (isFog) return new Color(0.55f, 0.63f, 0.72f);
        if (isWater) return new Color(0.18f, 0.42f, 0.78f);
        return originalColor; // no terrain flag set: keep whatever colour it has
    }

    private void CompleteCapture(UnitController unit)
    {
        capturePoints = maxCapturePoints;
        capturingUnit = null;
        UpdateCaptureDisplay();

        GameManager gm = FindFirstObjectByType<GameManager>();

        if (unit.isPlayer || unit.isAlly)
        {
            if (ownedByEnemy) gm?.EnemyLoseBase();
            ownedByPlayer = true;
            ownedByEnemy = false;
            ownedByNeutral = false;
            gm?.GetBase();
            if (isHQ && isEnemyHQ) gm?.EnemyLostHQ();
            Debug.Log("Player captured tile!");
            if (spawner != null)
            {
                spawner.isPlayer = true;
                spawner.isEnemy = false;
                spawner.isNeutral = false;
            }
        }
        else if (unit.isEnemy)
        {
            if (ownedByPlayer) gm?.LoseBase();
            ownedByEnemy = true;
            ownedByPlayer = false;
            ownedByNeutral = false;
            gm?.EnemyGetBase();
            if (isHQ && isPlayerHQ) gm?.PlayerLostHQ();
            Debug.Log("Enemy captured tile!");

            if (spawner != null)
            {
                spawner.isEnemy = true;
                spawner.isPlayer = false;
                spawner.isNeutral = false;
            }
        }
        RefreshLook(); // show the new owner's colour
    }
    #endregion
    public void SetFog(bool fogged)
    {
        isFogged = fogged;
        sr.color = isFogged ? fogColor : originalColor;
    }
    void UpdateCaptureDisplay()
    {
        if (captureText == null) return;

        if (capturePoints < maxCapturePoints)
        {
            captureText.text = $"{capturePoints}/{maxCapturePoints}";
            captureText.gameObject.SetActive(true);
        }
        else
        {
            captureText.gameObject.SetActive(false);
        }
    }
    public void SetHighlight(bool highlighted)
    {
        if (highlighted)
        {
            sr.color = new Color(0.5f, 0.8f, 1f, 0.5f);
        }
        else
        {
            sr.color = isFogged ? fogColor : originalColor;
        }
    }

    public void SetPathHighlight(bool highlighted)
    {
        sr.color = highlighted ? new Color(1f, 0.85f, 0.2f, 0.7f) : (isFogged ? fogColor : originalColor);
    }

    // Infantry and aircraft are trap immune, so a trap only ever goes off where a ground vehicle or ship can go.
    // Checked with stand-in unit types through CanUnitEnter, so it follows any change to the terrain rules
    public bool VehiclesCanEnter()
    {
        if (treadedVehicle == null)
        {
            treadedVehicle = ScriptableObject.CreateInstance<UnitTypeExtra>();
            treadedVehicle.isGround = true;
            treadedVehicle.isTreaded = true; // treaded vehicles reach everything wheeled ones can, and more
            ship = ScriptableObject.CreateInstance<UnitTypeExtra>();
            ship.isWater = true;
        }
        return CanUnitEnter(treadedVehicle) || CanUnitEnter(ship);
    }
    private static UnitTypeExtra treadedVehicle;
    private static UnitTypeExtra ship;

    public bool CanUnitEnter(UnitTypeExtra unitData)
    {
        if (unitData == null) return true;

        if (unitData.isAir) return true;

        bool isSeaBase = isBase && spawner != null && spawner.isSeaSpawner;

        bool isGroundTile = isRoad || isGrass || isForest || isHill || isMountain || isBase;
        bool isWaterTile = isWater || isFog || isBasaltPillar || isSeaBase;

        // Rough terrain keeps vehicles out (bases and roads are always fine)
        if (!isBase && !isRoad)
        {
            if (isMountain && (unitData.isWheeled || unitData.isTreaded)) return false;
            if ((isForest || isHill) && unitData.isWheeled) return false;
        }

        if (unitData.isGround && isGroundTile) return true;
        if (unitData.isWater && isWaterTile) return true;

        return false;
    }    
    public int GetTileDefence(UnitTypeExtra unitData)
    {
        if (unitData != null && unitData.isAir) return 0;
        return tileDefence;
    }

    public void SetAttackHighlight(bool highlighted)
    {
        if (highlighted)
        {
            sr.color = highlighted ? new Color(1f, 0.3f, 0.3f, 0.5f) : originalColor;
        }
        else
        {
            sr.color = isFogged ? fogColor : originalColor;
        }

    }
}
