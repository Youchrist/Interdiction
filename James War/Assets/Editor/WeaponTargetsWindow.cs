using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// James War > Weapon Targets: one table of every unit's two weapons and what each can hit, so they can be set
// side by side instead of opening each Unit Data asset. Ticks are saved straight into the Unit Data assets.
// Lives in an Editor folder, so it's left out of builds
public class WeaponTargetsWindow : EditorWindow
{
    private List<UnitTypeExtra> units = new List<UnitTypeExtra>();
    private Vector2 scroll;

    const float NameWidth = 110f, PowerWidth = 45f, TickWidth = 50f, GapWidth = 20f;

    [MenuItem("James War/Weapon Targets")]
    static void Open() => GetWindow<WeaponTargetsWindow>("Weapon Targets");

    void OnEnable()
    {
        LoadUnits();
        Undo.undoRedoPerformed += Repaint;
    }
    void OnDisable() => Undo.undoRedoPerformed -= Repaint;
    void OnProjectChange() => LoadUnits(); // unit data added, renamed or deleted

    // Every Unit Data asset in the project, grouped by category, then by name
    void LoadUnits()
    {
        units.Clear();
        foreach (string guid in AssetDatabase.FindAssets("t:UnitTypeExtra"))
        {
            UnitTypeExtra data = AssetDatabase.LoadAssetAtPath<UnitTypeExtra>(AssetDatabase.GUIDToAssetPath(guid));
            if (data != null) units.Add(data);
        }
        units.Sort((a, b) => a.unitCategory != b.unitCategory
            ? a.unitCategory.CompareTo(b.unitCategory)
            : string.Compare(a.name, b.name, System.StringComparison.Ordinal));
        Repaint();
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Tick what each weapon can hit. Weapons with 0 or less power don't exist, so they're greyed out. " +
            "A unit that's more than one kind (the Prototype is ground and water) is only hit by a weapon ticked for all of its kinds. " +
            "Click a unit's name to find its Unit Data. Changes save straight away.",
            MessageType.None);

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(NameWidth));
        GUILayout.Label("Primary", EditorStyles.boldLabel, GUILayout.Width(PowerWidth + TickWidth * 3 + 12f));
        GUILayout.Space(GapWidth);
        GUILayout.Label("Secondary", EditorStyles.boldLabel, GUILayout.Width(PowerWidth + TickWidth * 3 + 12f));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Unit", EditorStyles.boldLabel, GUILayout.Width(NameWidth));
        WeaponHeader();
        GUILayout.Space(GapWidth);
        WeaponHeader();
        EditorGUILayout.EndHorizontal();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        bool first = true;
        UnitTypeExtra.UnitCategory lastCategory = default;
        foreach (UnitTypeExtra data in units)
        {
            if (data == null) continue;
            if (first || data.unitCategory != lastCategory)
            {
                GUILayout.Space(first ? 2f : 8f);
                GUILayout.Label(data.unitCategory.ToString(), EditorStyles.miniBoldLabel);
                lastCategory = data.unitCategory;
                first = false;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(data.name, EditorStyles.label, GUILayout.Width(NameWidth)))
                EditorGUIUtility.PingObject(data);
            WeaponColumns(data, true);
            GUILayout.Space(GapWidth);
            WeaponColumns(data, false);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    static void WeaponHeader()
    {
        GUILayout.Label("Power", GUILayout.Width(PowerWidth));
        GUILayout.Label("Air", GUILayout.Width(TickWidth));
        GUILayout.Label("Ground", GUILayout.Width(TickWidth));
        GUILayout.Label("Water", GUILayout.Width(TickWidth));
    }

    // One weapon's power and its three ticks. Changes can be undone (Ctrl+Z) and are saved to the asset
    static void WeaponColumns(UnitTypeExtra data, bool primary)
    {
        float power = primary ? data.primaryAttackPower : data.secondaryAttackPower;
        bool hasWeapon = power > 0;
        GUILayout.Label(hasWeapon ? power.ToString() : "none", GUILayout.Width(PowerWidth));

        using (new EditorGUI.DisabledScope(!hasWeapon))
        {
            EditorGUI.BeginChangeCheck();
            bool air = Tick(primary ? data.primaryHitsAir : data.secondaryHitsAir);
            bool ground = Tick(primary ? data.primaryHitsGround : data.secondaryHitsGround);
            bool water = Tick(primary ? data.primaryHitsWater : data.secondaryHitsWater);
            if (!EditorGUI.EndChangeCheck()) return;

            Undo.RecordObject(data, "Change weapon targets");
            if (primary)
            {
                data.primaryHitsAir = air;
                data.primaryHitsGround = ground;
                data.primaryHitsWater = water;
            }
            else
            {
                data.secondaryHitsAir = air;
                data.secondaryHitsGround = ground;
                data.secondaryHitsWater = water;
            }
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
        }
    }

    static bool Tick(bool value) => EditorGUILayout.Toggle(value, GUILayout.Width(TickWidth));
}
