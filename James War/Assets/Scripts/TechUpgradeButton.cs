using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Goes on an Upgrade button in the Tech List. The button stays greyed out until every unit in its category
// is researched, then each press spends a research point on another upgrade level.
// It hooks up its own click, so leave the button's OnClick list empty
[RequireComponent(typeof(Button))]
public class TechUpgradeButton : MonoBehaviour
{
    public UnitTypeExtra.UnitCategory category;
    [Tooltip("Optional: a text that shows the current upgrade level")]
    public TMP_Text levelText;

    private Button button;

    void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(Research);
    }

    void Update()
    {
        TechTree tree = TechTree.Instance;
        if (tree == null) return;

        button.interactable = tree.CanUpgrade(category, true);
        if (levelText != null) levelText.text = $"Lv {tree.GetUpgradeLevel(category, true)}";
    }

    void Research()
    {
        TechTree tree = TechTree.Instance;
        if (tree == null || !tree.Upgrade(category, true)) return;

        // Same as unlocking a unit: the menu closes once research points run out
        if (!tree.PlayerHasResearch())
        {
            Cursor cursor = FindFirstObjectByType<Cursor>();
            if (cursor != null) cursor.CloseResearchMenu();
        }
    }
}
