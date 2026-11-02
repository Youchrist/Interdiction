using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UnitSpawner : MonoBehaviour
{
#region Ints/Floats/Bools/etc
    [Header("Type")]
    public bool isGroundSpawner;
    public bool isAirSpawner;
    public bool isSeaSpawner;

    [Header("Faction")]
    public bool isPlayer;
    public bool isEnemy;
    public bool isNeutral;
    public bool isAlly;

    private GameManager gm;

    public bool CanSpawnUnit(GameObject prefab)
    {
        if (prefab == null) return false;
        UnitController unit = prefab.GetComponent<UnitController>();
        if (unit == null || unit.unitData == null) return false;

        Collider2D[] hits = Physics2D.OverlapPointAll(transform.position);
        foreach (Collider2D hit in hits)
        {
            if (hit.CompareTag("Unit"))
            {
                Debug.Log("Spawner is occupied ?cannot spawn here");
                return false;
            }
        }

        return BuildsType(unit.unitData);
    }

    // Whether this kind of base makes this kind of unit (ignores anything standing on it)
    public bool BuildsType(UnitTypeExtra data)
    {
        if (data == null) return false;
        return (isGroundSpawner && data.isGround) || (isAirSpawner && data.isAir) || (isSeaSpawner && data.isWater);
    }
    #endregion
    // Start is called before the first frame update
    void Start()
    {
        GameObject obj = GameObject.Find("GameManager");
        gm = obj.GetComponent<GameManager>();
    }
    public void SpawnUnit(GameObject prefab)
    {
        if (!CanSpawnUnit(prefab))
        {
            Debug.Log($"{prefab?.name} cannot be spawned from this base type");
            return;
        }
        if (prefab == null)
        {
            Debug.Log("no prefab");
            return;
        }
        GameObject instance = Instantiate(prefab, transform.position, Quaternion.identity);
        UnitController unit = instance.GetComponent<UnitController>();
        if (unit == null)
        {
            Debug.Log("prefab no unitcontroller");
            return;
        }
        unit.SetSide(isPlayer, isAlly, isEnemy);

        unit.SpawnIn();
        GameManager gm = FindFirstObjectByType<GameManager>();

        if (gm != null) gm.RegisterUnit(unit);
        Debug.Log($"{prefab.name} spawned at {gameObject.name}");
    }
}
