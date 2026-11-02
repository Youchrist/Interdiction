using UnityEngine;

// Carries choices between scenes (static fields survive scene loads)
public static class GameSettings
{
    public static CommanderData PlayerCommander;
    public static CommanderData EnemyCommander;

    // Start every play session fresh, even if the editor skips reloading scripts on Play
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        PlayerCommander = null;
        EnemyCommander = null;
    }
}
