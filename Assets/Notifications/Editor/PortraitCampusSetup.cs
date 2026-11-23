using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WebNotifications.Editor
{
    [InitializeOnLoad]
    public static class PortraitCampusSetup
    {
        private const string PreviewSessionKey = "PixelCampus.PortraitPreviewApplied";
        private const string DemoScenePath = "Assets/Scenes/NotificationDemo.unity";
        private const int PortraitWidth = 540;
        private const int PortraitHeight = 960;
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static PortraitCampusSetup()
        {
            EditorApplication.delayCall += InitializePreview;
            EditorApplication.playModeStateChanged += PreparePlayPreview;
        }

        private static void PreparePlayPreview(PlayModeStateChange state)
        {
            if (Application.isBatchMode || state != PlayModeStateChange.ExitingEditMode) return;

            var startScene = EditorSceneManager.playModeStartScene;
            var scenePath = startScene == null ? SceneManager.GetActiveScene().path : AssetDatabase.GetAssetPath(startScene);
            if (scenePath == DemoScenePath) SelectPortraitPreview();
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
            PlayerSettings.defaultScreenWidth = PortraitWidth;
            PlayerSettings.defaultScreenHeight = PortraitHeight;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(DemoScenePath, true) };
            AssetDatabase.SaveAssets();
            SelectPortraitPreview();
        }

        [MenuItem("Tools/Pixel Campus/Open Portrait Demo")]
        public static void OpenDemo()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before opening the demo.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Apply();
            EditorSceneManager.OpenScene(DemoScenePath);
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
            var sizeType = editorAssembly.GetType("UnityEditor.GameViewSizeType");
            var fixedResolution = Enum.Parse(sizeType, "FixedResolution");
            var getSize = groupClass.GetMethod("GetGameViewSize");
            var getWidth = sizeClass.GetProperty("width");
            var getHeight = sizeClass.GetProperty("height");
            var getSizeType = sizeClass.GetProperty("sizeType");
            int selected = -1;
            for (int index = 0; index < builtinCount + customCount; index++)
            {
                var size = getSize.Invoke(group, new object[] { index });
                if ((int)getWidth.GetValue(size) == PortraitWidth &&
                    (int)getHeight.GetValue(size) == PortraitHeight && Equals(getSizeType.GetValue(size), fixedResolution))
                {
                    selected = index;
                    break;
                }
            }
            if (selected < 0)
            {
                var size = Activator.CreateInstance(sizeClass, fixedResolution, PortraitWidth, PortraitHeight, "Pixel Campus Portrait");
                groupClass.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                selected = builtinCount + customCount;
            }
            var gameViewType = editorAssembly.GetType("UnityEditor.GameView");
            var gameView = EditorWindow.GetWindow(gameViewType);
            // Match a toolbar selection so the render target and zoom are recalculated together.
            var current = (int)gameViewType.GetProperty("selectedSizeIndex", InstanceMembers).GetValue(gameView);
            if (current == selected)
                gameViewType.GetMethod("UpdateZoomAreaAndParent", InstanceMembers).Invoke(gameView, null);
            else
                gameViewType.GetMethod("SizeSelectionCallback", InstanceMembers).Invoke(gameView, new object[] { selected, null });
            var fitScale = (float)gameViewType.GetProperty("minScale", InstanceMembers).GetValue(gameView);
            gameViewType.GetMethod("SnapZoom", InstanceMembers).Invoke(gameView, new object[] { fitScale });
            gameView.Repaint();
        }
    }
}
