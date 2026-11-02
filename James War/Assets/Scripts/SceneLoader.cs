using UnityEngine;
using UnityEngine.SceneManagement;

// Button hooks for moving between scenes. Scenes must be listed in File > Build Profiles > Scene List.
public class SceneLoader : MonoBehaviour
{
    public void LoadTitle() => Load("Title");
    public void LoadCommanderSelect() => Load("SelectScene");
    public void LoadMap() => Load("MapScene");

    void Load(string sceneName)
    {
        Time.timeScale = 1f; // the win/lose check pauses the game
        SceneManager.LoadScene(sceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // Application.Quit does nothing in the editor
#endif
    }
}
