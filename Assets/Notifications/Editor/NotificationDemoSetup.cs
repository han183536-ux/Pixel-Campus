using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WebNotifications.Editor
{
    public static class NotificationDemoSetup
    {
        [MenuItem("Tools/Web Notifications/Create Demo Scene")]
        public static void CreateScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const string scenePath = "Assets/Scenes/NotificationDemo.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
            {
                EditorSceneManager.OpenScene(scenePath);
                return;
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var root = new GameObject("SmartLEAD Notification Check");
            var manager = root.AddComponent<NotificationManager>();
            manager.useSampleData = false;
            manager.useWebView = true;
            root.AddComponent<SmartleadWebView>();
            var ui = root.AddComponent<NotificationDetailUI>();
            ui.manager = manager;
            ui.mapCamera = Camera.main;
            EditorSceneManager.SaveScene(scene, scenePath);
            Selection.activeGameObject = root;
        }
    }
}
