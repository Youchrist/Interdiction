using UnityEngine;
using UnityEngine.Tilemaps;

[CreateAssetMenu(fileName = "TerrainTile", menuName = "Tiles/Terrain Tile")]
public class TerrainTile : Tile
{
    public enum TerrainType { Road, Grass, Forest, Hill, Mountain, Water, Fog, BasaltPillar }
    public TerrainType terrainType;

    public bool CanGroundEnter()
    {
        return terrainType == TerrainType.Road || terrainType == TerrainType.Grass ||
               terrainType == TerrainType.Forest || terrainType == TerrainType.Hill ||
               terrainType == TerrainType.Mountain;
    }

    public bool CanWaterEnter()
    {
        return terrainType == TerrainType.Water || terrainType == TerrainType.Fog ||
               terrainType == TerrainType.BasaltPillar;
    }

    public int GetDefence()
    {
        switch (terrainType)
        {
            case TerrainType.Road:
            case TerrainType.Water: return 0;
            case TerrainType.Grass:
            case TerrainType.Fog: return 10;
            case TerrainType.Forest: return 20;
            case TerrainType.Hill:
            case TerrainType.BasaltPillar: return 30;
            case TerrainType.Mountain: return 40;
            default: return 0;
        }
    }
}
