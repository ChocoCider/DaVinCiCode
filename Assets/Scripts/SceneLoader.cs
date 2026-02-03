using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneLoader
{
    public static void LoadIfNotCurrent(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogError("[SceneLoader] sceneName is empty");
            return;
        }

        var cur = SceneManager.GetActiveScene().name;
        if (cur == sceneName) return;

        SceneManager.LoadScene(sceneName);
    }
}

public static class SceneNames
{
    public const string Login = "LoginScene";
    public const string Lobby = "LobbyScene";
    public const string Room = "RoomScene";
    public const string InGame = "InGameScene";
}