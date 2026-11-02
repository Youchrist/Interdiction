using UnityEngine;

public static class GridUtil
{
    public static float Manhattan(Vector2 a, Vector2 b) =>
        Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    public static TileFunction TileAt(Vector2 pos)
    {
        foreach (Collider2D hit in Physics2D.OverlapPointAll(pos))
        {
            TileFunction tile = hit.GetComponent<TileFunction>();
            if (tile != null) return tile;
        }
        return null;
    }

    public static UnitController UnitAt(Vector2 pos, UnitController ignore = null)
    {
        foreach (Collider2D hit in Physics2D.OverlapPointAll(pos))
        {
            UnitController u = hit.GetComponent<UnitController>();
            if (u != null && u != ignore) return u;
        }
        return null;
    }

    public static bool IsOccupied(Vector2 pos, UnitController ignore = null) => UnitAt(pos, ignore) != null;
}
