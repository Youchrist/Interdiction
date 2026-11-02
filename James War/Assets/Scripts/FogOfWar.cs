using JetBrains.Annotations;
using System.Collections.Generic;
using UnityEngine;

public class FogOfWar : MonoBehaviour
{
    public static FogOfWar Instance;

    private bool isNight = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void UpdateFog(bool night)
    {
        isNight = night;
        RefreshVisibility();
    }

    public void RefreshIfNight()
    {
        RefreshVisibility();
    }

    public void RefreshVisibility()
    {
        TileFunction[] allTiles = FindObjectsByType<TileFunction>(FindObjectsSortMode.None);
        UnitController[] allUnits = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
        Trap[] allTraps = FindObjectsByType<Trap>(FindObjectsSortMode.None);

        HashSet<Vector2> visibleTiles = null;

        if (isNight)
        {
            visibleTiles = new HashSet<Vector2>();
            foreach (UnitController unit in allUnits)
            {
                if (!unit.isPlayer && !unit.isAlly) continue;
                if (unit.isLoaded) continue; // cargo sees nothing; its position is where it boarded

                Vector2 unitPos = unit.transform.position;
                int vision = unit.GetVisionRange();

                foreach (TileFunction tile in allTiles)
                {
                    Vector2 tilePos = tile.transform.position;
                    float dist = Mathf.Abs(tilePos.x - unitPos.x) + Mathf.Abs(tilePos.y - unitPos.y);
                    if (dist <= vision) visibleTiles.Add(tilePos);
                }
            }

            // The player's own bases always see their own tile, so they can be found to build from at night
            foreach (TileFunction tile in allTiles)
                if (tile.ownedByPlayer && tile.GetComponent<UnitSpawner>() != null)
                    visibleTiles.Add(tile.transform.position);

            foreach (TileFunction tile in allTiles)
                tile.SetFog(!visibleTiles.Contains((Vector2)tile.transform.position));
        }
        else
        {
            foreach (TileFunction tile in allTiles)
                tile.SetFog(false);
        }

        // Enemy units: hidden by night fog OR unbroken invisibility
        foreach (UnitController unit in allUnits)
        {
            if (!unit.isEnemy) continue;
            if (unit.isLoaded) continue; // stays hidden inside its transport

            bool inFog = isNight && (visibleTiles == null || !visibleTiles.Contains((Vector2)unit.transform.position));
            bool hiddenByInvis = unit.IsInvisibleAndUndetected(true);

            unit.SetVisibility(!inFog && !hiddenByInvis);
        }

        // Enemy traps: hidden purely by invisibility, unaffected by day/night
        foreach (Trap trap in allTraps)
        {
            trap.SetVisibility(!trap.IsHiddenFrom(true));
        }
    }
}

