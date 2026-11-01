#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SolarisSceneBuilder
{
    private const string SCENE_DIR = "Assets/Scenes";
    private const string SCENE_PATH = "Assets/Scenes/MainScene.unity";

    static SolarisSceneBuilder()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;
            string absScenePath = Path.Combine(Application.dataPath, "Scenes", "MainScene.unity");
            if (!File.Exists(absScenePath))
            {
                BuildAndSaveMainScene();
            }
        };
    }

    [MenuItem("Solaris Reclaim/Build & Save MainScene v2.0", false, 1)]
    public static void BuildAndSaveMainScene()
    {
        Debug.Log("[SolarisSceneBuilder] Starting MainScene v2.0 automated construction...");

        // 1. Ensure target directory exists
        string absDir = Path.Combine(Application.dataPath, "Scenes");
        if (!Directory.Exists(absDir))
        {
            Directory.CreateDirectory(absDir);
            AssetDatabase.Refresh();
        }

        // 2. Create clean new empty scene
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 3. Create root GameObject
        GameObject rootGo = new GameObject("[AutonomousStudioRoot]");
        SolarisGameManager gm = rootGo.AddComponent<SolarisGameManager>();

        // 4. Invoke scene build
        gm.BuildScene();

        // 5. Mark scene dirty & Save scene to Assets/Scenes/MainScene.unity
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene, SCENE_PATH);
        if (!saved)
        {
            Debug.LogError("[SolarisSceneBuilder] Failed to save scene to: " + SCENE_PATH);
            return;
        }

        // 6. Register scene in EditorBuildSettings.scenes
        var currentScenes = EditorBuildSettings.scenes.ToList();
        if (!currentScenes.Any(s => s.path == SCENE_PATH))
        {
            currentScenes.Add(new EditorBuildSettingsScene(SCENE_PATH, true));
            EditorBuildSettings.scenes = currentScenes.ToArray();
            Debug.Log("[SolarisSceneBuilder] Registered " + SCENE_PATH + " into EditorBuildSettings.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("<color=#39D0C0><b>[SolarisSceneBuilder] MainScene v2.0 successfully built, saved and registered!</b></color>");
    }

    /// <summary>
    /// Headless batch build method for Unity CLI execution:
    /// -executeMethod SolarisSceneBuilder.BuildMainSceneBatch
    /// </summary>
    public static void BuildMainSceneBatch()
    {
        BuildAndSaveMainScene();
    }
}
#endif
