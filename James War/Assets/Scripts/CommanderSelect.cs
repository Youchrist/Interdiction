using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// Commander select screen: the arrows cycle each side's commander, Start loads the map with them
public class CommanderSelect : MonoBehaviour
{
    [Tooltip("Every commander that can be picked, in the order the arrows cycle through")]
    public CommanderData[] commanders;

    [Header("Player 1 display")]
    public TMP_Text p1Name;
    public SpriteRenderer p1Portrait;
    public TMP_Text p1Passive;
    public TMP_Text p1AbilityName;
    public TMP_Text p1AbilityDescription;

    [Header("Player 2 display (optional, for when P2 gets a folder)")]
    public TMP_Text p2Name;
    public SpriteRenderer p2Portrait;

    private int p1Index;
    private int p2Index;

    void Start()
    {
        // Coming back to this screen shows the previous picks
        p1Index = Mathf.Max(0, System.Array.IndexOf(commanders, GameSettings.PlayerCommander));
        p2Index = Mathf.Max(0, System.Array.IndexOf(commanders, GameSettings.EnemyCommander));
        Refresh();
    }

    // Wire the four arrow buttons to these
    public void NextP1() => Cycle(ref p1Index, 1);
    public void PreviousP1() => Cycle(ref p1Index, -1);
    public void NextP2() => Cycle(ref p2Index, 1);
    public void PreviousP2() => Cycle(ref p2Index, -1);

    void Cycle(ref int index, int step)
    {
        if (commanders.Length == 0) return;
        index = (index + step + commanders.Length) % commanders.Length;
        Refresh();
    }

    void Refresh()
    {
        if (commanders.Length == 0) return;
        Show(commanders[p1Index], p1Name, p1Portrait, p1Passive, p1AbilityName, p1AbilityDescription);
        Show(commanders[p2Index], p2Name, p2Portrait, null, null, null);
    }

    void Show(CommanderData c, TMP_Text nameText, SpriteRenderer portrait, TMP_Text passive, TMP_Text abilityName, TMP_Text abilityDescription)
    {
        if (nameText != null) nameText.text = c.commanderName;
        if (portrait != null && c.portrait != null) portrait.sprite = c.portrait; // keeps the placeholder art until one is set
        if (passive != null) passive.text = c.passiveDescription;
        if (abilityName != null) abilityName.text = $"{c.abilityName} ({c.abilityCost} Fury)";
        if (abilityDescription != null) abilityDescription.text = c.abilityDescription;
    }

    // Wire the Start button to this
    public void StartGame()
    {
        if (commanders.Length == 0)
        {
            Debug.LogWarning("No commanders assigned on CommanderSelect");
            return;
        }
        GameSettings.PlayerCommander = commanders[p1Index];
        GameSettings.EnemyCommander = commanders[p2Index];
        Time.timeScale = 1f;
        SceneManager.LoadScene("MapScene");
    }
}
