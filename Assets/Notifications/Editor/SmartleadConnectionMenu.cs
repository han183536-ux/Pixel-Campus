using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WebNotifications.Editor
{
    public static class SmartleadConnectionMenu
    {
        [MenuItem("Tools/Web Notifications/Use SmartLEAD Connection")]
        public static void UseConnection()
        {
            var manager = Object.FindObjectOfType<NotificationManager>();
            if (manager == null)
            {
                UnityEngine.Debug.LogWarning("NotificationDemo 씬을 먼저 열어 주세요.");
                return;
            }
            Undo.RecordObject(manager, "Use SmartLEAD Connection");
            manager.useSampleData = false;
            manager.useWebView = false;
            if (Application.isPlaying) manager.Refresh();
            else { EditorUtility.SetDirty(manager); EditorSceneManager.MarkSceneDirty(manager.gameObject.scene); }
        }

        [MenuItem("Tools/Web Notifications/Start SmartLEAD Server (Login)")]
        public static void StartLogin() { Launch(true); }

        [MenuItem("Tools/Web Notifications/Start SmartLEAD Server (Token)")]
        public static void StartToken() { Launch(false); }

        private static void Launch(bool login)
        {
#if UNITY_EDITOR_WIN
            var root = Directory.GetParent(Application.dataPath).FullName;
            var script = Path.Combine(root, "NotificationServer", "start-smartlead.ps1");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NoExit -File \"" + script + "\"" + (login ? " -Login" : ""),
                WorkingDirectory = root,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            });
#else
            UnityEngine.Debug.LogWarning("이 로그인 실행 메뉴는 Windows용입니다. 서버 환경변수 SMARTLEAD_TOKEN을 설정해 실행하세요.");
#endif
        }
    }
}
