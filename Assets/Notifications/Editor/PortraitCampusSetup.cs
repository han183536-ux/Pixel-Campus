using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WebNotifications.Editor
{
    [InitializeOnLoad]
    public static class PortraitCampusSetup
    {
        private const string PreviewSessionKey = "PixelCampus.PortraitPreviewApplied";

        static PortraitCampusSetup()
        {
            EditorApplication.delayCall += InitializePreview;
        }

        public sealed class PortraitBuildTargetChanged : IActiveBuildTargetChanged
        {
            public int callbackOrder => 0;
            public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget)
            {
                if (!Application.isBatchMode) EditorApplication.delayCall += SelectPortraitPreview;
            }
        }

        private static void InitializePreview()
        {
            if (Application.isBatchMode || SessionState.GetBool(PreviewSessionKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += InitializePreview;
                return;
            }
            SelectPortraitPreview();
            SessionState.SetBool(PreviewSessionKey, true);
        }

        [MenuItem("Tools/Pixel Campus/Use Portrait Screen")]
        public static void Apply()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/NotificationDemo.unity", true) };
            AssetDatabase.SaveAssets();
            SelectPortraitPreview();
        }

        [MenuItem("Tools/Pixel Campus/Open Portrait Demo")]
        public static void OpenDemo()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before opening the demo.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Apply();
            EditorSceneManager.OpenScene("Assets/Scenes/NotificationDemo.unity");
        }

        private static void SelectPortraitPreview()
        {
            var editorAssembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance").GetValue(null);
            // The preview group follows the active build target (including Android).
            var group = sizesType.GetProperty("currentGroup").GetValue(sizes);
            var groupClass = group.GetType();
            var builtinCount = (int)groupClass.GetMethod("GetBuiltinCount").Invoke(group, null);
            var customCount = (int)groupClass.GetMethod("GetCustomCount").Invoke(group, null);
            var sizeClass = editorAssembly.GetType("UnityEditor.GameViewSize");
            var getSize = groupClass.GetMethod("GetGameViewSize");
            int selected = -1;
            for (int index = 0; index < builtinCount + customCount; index++)
            {
                var size = getSize.Invoke(group, new object[] { index });
                if ((int)sizeClass.GetProperty("width").GetValue(size) == 540 &&
                    (int)sizeClass.GetProperty("height").GetValue(size) == 960) { selected = index; break; }
            }
            if (selected < 0)
            {
                var sizeType = editorAssembly.GetType("UnityEditor.GameViewSizeType");
                var size = Activator.CreateInstance(sizeClass, Enum.Parse(sizeType, "FixedResolution"), 540, 960, "Pixel Campus Portrait");
                groupClass.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                selected = builtinCount + customCount;
            }
            var gameViewType = editorAssembly.GetType("UnityEditor.GameView");
            var gameView = EditorWindow.GetWindow(gameViewType);
            gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(gameView, selected);
            gameView.Repaint();
        }
    }
}
