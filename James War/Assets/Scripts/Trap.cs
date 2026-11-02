using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Trap : MonoBehaviour
{
    public float trapDamage = 30f;
    public bool destroyOnTrigger = true;

    public bool isPlayer;
    public bool isEnemy;
    public bool isAlly;

    private SpriteRenderer sr;
    // Start is called before the first frame update
    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }
    public void SetVisibility(bool visible)
    {
        if (sr != null) sr.enabled = visible;
    }

    public bool IsHiddenFrom(bool checkingFromPlayerSide)
    {
        bool ownedByCheckingSide = checkingFromPlayerSide ? (isPlayer || isAlly) : isEnemy;
        if (ownedByCheckingSide) return false; // always see your own traps

        UnitController[] allUnits = FindObjectsByType<UnitController>(FindObjectsSortMode.None);
        foreach (UnitController u in allUnits)
        {
            bool onCheckingSide = checkingFromPlayerSide ? (u.isPlayer || u.isAlly) : u.isEnemy;
            if (!onCheckingSide) continue;
            if (u.unitData == null || !u.unitData.canSeeInvis) continue;

            float dist = Mathf.Abs(u.transform.position.x - transform.position.x)
                       + Mathf.Abs(u.transform.position.y - transform.position.y);
            if (dist <= u.GetVisionRange()) return false; // detected
        }
        return true;
    }
    public void TriggerTrap(UnitController unit)
    {
        if (unit == null) return;
        // Don't trigger on same faction
        if (isPlayer && unit.isPlayer) return;
        if (isEnemy && unit.isEnemy) return;
        if (isAlly && unit.isAlly) return;

        bool immune = unit.unitData != null && unit.unitData.isTrapImmune;
        if (immune)
        {
            Debug.Log($"{unit.gameObject.name} is trap immune");
            return;
        }

        unit.TakeRawDamage(trapDamage);
        Debug.Log($"{unit.gameObject.name} triggered trap for {trapDamage} damage");

        if (destroyOnTrigger)
            Destroy(gameObject);
    }
    public void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Unit")) return;
        UnitController unit = other.GetComponent<UnitController>();
        TriggerTrap(unit);
    }
}

