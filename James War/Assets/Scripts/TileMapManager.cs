using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

// Runs before other scripts, so the painted terrain is turned into tiles before anything looks for tiles
[DefaultExecutionOrder(-100)]
public class TilemapManager : MonoBehaviour
{
    public static TilemapManager Instance;
    public Tilemap groundTilemap;
    [Tooltip("The Tile prefab. When the map starts, one is made for every painted cell, set to that cell's terrain")]
    public GameObject tilePrefab;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
        ConvertPaintedTiles();
    }

    // Painted terrain becomes regular tile objects (TileFunction), which is what movement, combat, fog, captures
    // and the AI all read. Painting wins over hand-placed plain terrain tiles (those are replaced), but bases, HQs
    // and other capturable properties are kept, so you can paint terrain under them.
    // The Tilemap is hidden afterwards, since the new tiles draw the map
    void ConvertPaintedTiles()
    {
        if (groundTilemap == null) return;
        if (tilePrefab == null)
        {
            Debug.LogWarning("[Tilemap] No Tile Prefab assigned on TilemapManager, so painted terrain won't be playable");
            return;
        }

        // Cells holding a property, which painting never replaces, and plain tiles painting can replace
        HashSet<Vector2> taken = new HashSet<Vector2>();
        Dictionary<Vector2, TileFunction> plainTiles = new Dictionary<Vector2, TileFunction>();
        foreach (TileFunction existing in FindObjectsByType<TileFunction>(FindObjectsSortMode.None))
        {
            Vector2 pos = Snap(existing.transform.position);
            bool property = existing.isBase || existing.isHQ || existing.isCaptureable || existing.GetComponent<UnitSpawner>() != null;
            if (property) taken.Add(pos);
            else plainTiles[pos] = existing;
        }

        Transform parent = new GameObject("Generated Tiles").transform;
        int made = 0;
        int replaced = 0;
        bool offGrid = false;
        foreach (Vector3Int cell in groundTilemap.cellBounds.allPositionsWithin)
        {
            TerrainTile terrain = groundTilemap.GetTile<TerrainTile>(cell);
            if (terrain == null) continue;

            Vector3 center = groundTilemap.GetCellCenterWorld(cell);
            Vector2 pos = Snap(center);
            if (Mathf.Abs(center.x - pos.x) > 0.01f || Mathf.Abs(center.y - pos.y) > 0.01f) offGrid = true;
            if (!taken.Add(pos)) continue; // a base, HQ or property is here

            // Remove the hand-placed plain tile under the paint. Deactivating first takes it out of the game
            // straight away (Destroy only happens at the end of the frame)
            if (plainTiles.TryGetValue(pos, out TileFunction old))
            {
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
                replaced++;
            }

            GameObject tileObject = Instantiate(tilePrefab, pos, Quaternion.identity, parent);
            tileObject.name = $"{terrain.terrainType} ({pos.x}, {pos.y})";
            ApplyTerrain(tileObject.GetComponent<TileFunction>(), terrain.terrainType);
            made++;
        }

        if (offGrid)
            Debug.LogWarning("[Tilemap] Painted cells aren't centred on whole numbers, so tiles were snapped to the nearest one. " +
                             "Set the Grid object's Position to (-0.5, -0.5, 0) so painting lines up with units and bases");

        TilemapRenderer tilemapRenderer = groundTilemap.GetComponent<TilemapRenderer>();
        if (tilemapRenderer != null) tilemapRenderer.enabled = false;
        Debug.Log($"[Tilemap] Made {made} tiles from painted terrain ({replaced} of them replaced hand-placed tiles)");
    }

    static Vector2 Snap(Vector3 p) => new Vector2(Mathf.Round(p.x), Mathf.Round(p.y));

    // Turns a Tile prefab copy into the painted terrain: exactly one terrain flag, and not a property
    static void ApplyTerrain(TileFunction tile, TerrainTile.TerrainType type)
    {
        if (tile == null) return;
        tile.isRoad = type == TerrainTile.TerrainType.Road;
        tile.isGrass = type == TerrainTile.TerrainType.Grass;
        tile.isForest = type == TerrainTile.TerrainType.Forest;
        tile.isHill = type == TerrainTile.TerrainType.Hill;
        tile.isMountain = type == TerrainTile.TerrainType.Mountain;
        tile.isWater = type == TerrainTile.TerrainType.Water;
        tile.isFog = type == TerrainTile.TerrainType.Fog;
        tile.isBasaltPillar = type == TerrainTile.TerrainType.BasaltPillar;
        tile.isBase = false;
        tile.isHQ = false;
        tile.isCaptureable = false;
        tile.RefreshLook(); // its colour was set from the prefab's flags when it was created
    }

    public Vector3Int WorldToCell(Vector2 worldPos) => groundTilemap.WorldToCell(worldPos);

    public TerrainTile GetTerrainAt(Vector2 worldPos)
    {
        return groundTilemap.GetTile<TerrainTile>(WorldToCell(worldPos));
    }

    public bool CanUnitEnter(Vector2 worldPos, UnitTypeExtra unitData)
    {
        TerrainTile tile = GetTerrainAt(worldPos);
        if (tile == null || unitData == null) return true;
        if (unitData.isAir) return true;
        if (unitData.isGround && tile.CanGroundEnter()) return true;
        if (unitData.isWater && tile.CanWaterEnter()) return true;
        return false;
    }

    public int GetTileDefence(Vector2 worldPos, UnitTypeExtra unitData)
    {
        if (unitData != null && unitData.isAir) return 0;
        TerrainTile tile = GetTerrainAt(worldPos);
        return tile != null ? tile.GetDefence() : 0;
    }

    public void SetCellColor(Vector2 worldPos, Color color)
    {
        Vector3Int cell = WorldToCell(worldPos);
        groundTilemap.SetTileFlags(cell, TileFlags.None); // required to allow per-cell tinting
        groundTilemap.SetColor(cell, color);
    }
}
