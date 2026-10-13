using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WebNotifications.Editor
{
    public static class SmartleadWebViewMenu
    {
        [MenuItem("Tools/Web Notifications/Use SmartLEAD WebView")]
        public static void EnableWebView()
        {
            if (Application.isPlaying) { Debug.LogWarning("Play를 종료한 후 WebView 모드로 전환하세요."); return; }
            var manager = Object.FindObjectOfType<NotificationManager>();
            if (manager == null) return;
            Undo.RecordObject(manager, "Use WebView");
            manager.useWebView = true; manager.useSampleData = false;
            if (manager.GetComponent<SmartleadWebView>() == null) Undo.AddComponent<SmartleadWebView>(manager.gameObject);
            EditorUtility.SetDirty(manager); EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
}
