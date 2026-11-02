using System.Collections.Generic;
using UnityEngine;

// Remembers which tiles each side's vehicles and ships drive through, fading over time so recent routes count most.
// Only units that can set off traps are recorded. The AI uses it to lay traps on busy routes
public static class TrafficMap
{
    private static Dictionary<Vector2, float> playerHeat = new Dictionary<Vector2, float>();
    private static Dictionary<Vector2, float> enemyHeat = new Dictionary<Vector2, float>();

    // Fresh maps for every play session and scene load
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        playerHeat.Clear();
        enemyHeat.Clear();
    }

    public static void Record(Vector2 tile, bool playerSide)
    {
        Dictionary<Vector2, float> heat = playerSide ? playerHeat : enemyHeat;
        heat.TryGetValue(tile, out float value);
        heat[tile] = value + 1f;
    }

    public static float Heat(Vector2 tile, bool playerSide)
    {
        Dictionary<Vector2, float> heat = playerSide ? playerHeat : enemyHeat;
        return heat.TryGetValue(tile, out float value) ? value : 0f;
    }

    // Called once a round: older traffic counts for less
    public static void Fade(float keep)
    {
        FadeMap(playerHeat, keep);
        FadeMap(enemyHeat, keep);
    }

    static void FadeMap(Dictionary<Vector2, float> heat, float keep)
    {
        foreach (Vector2 tile in new List<Vector2>(heat.Keys))
        {
            heat[tile] *= keep;
            if (heat[tile] < 0.05f) heat.Remove(tile);
        }
    }
}
