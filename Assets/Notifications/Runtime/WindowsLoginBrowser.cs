#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WebNotifications
{
    internal static class WindowsLoginBrowser
    {
        private const string RuntimeVariable = "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER";
        private const string ProfileVariable = "WEBVIEW2_USER_DATA_FOLDER";
        private const string FallbackParentVariable = "PIXELCAMPUS_WEBVIEW2_FALLBACK_PARENT";

        private static readonly string[] BrowserFiles =
        {
            "msedgewebview2.exe", "msedge.dll", @"EBWebView\x64\EmbeddedBrowserWebView.dll", "icudtl.dat", "resources.pak"
        };
        private static readonly string[] CrtFiles = { "vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll" };
        private static string playerDirectory, profile, profileError, systemLaunchError, missingRuntimeFile;
        private static IntPtr recoveredPlugin;
        private static int pluginLoadError;
        private static bool externalRuntime, externalProfile, fallbackProcess, fallbackStarted;
        internal static bool UsingBundled { get; private set; }
        internal static string RuntimeDescription => UsingBundled ? "앱에 포함된 WebView2" :
            externalRuntime ? "사용자가 지정한 WebView2" :
            fallbackProcess ? "Windows WebView2 (자동 전환)" : "Windows WebView2";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Configure()
        {
            playerDirectory = Path.GetDirectoryName(Application.dataPath);
            string bundled = Path.Combine(playerDirectory, "WebView2Runtime");
            bool hasRuntimeFolder = Directory.Exists(bundled);
            bool portablePackage = hasRuntimeFolder || File.Exists(Path.Combine(playerDirectory, "WebView2-Fixed-License.en.txt"));
            string parent = Environment.GetEnvironmentVariable(FallbackParentVariable);
            fallbackProcess = int.TryParse(parent, out int parentId) && parentId > 0;
            externalRuntime = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(RuntimeVariable));
            if (!externalRuntime && !fallbackProcess && portablePackage)
            {
                missingRuntimeFile = hasRuntimeFolder
                    ? Array.Find(BrowserFiles, file => !File.Exists(Path.Combine(bundled, file))) : "WebView2Runtime";
                if (missingRuntimeFile == null)
                {
                    Environment.SetEnvironmentVariable(RuntimeVariable, bundled);
                    UsingBundled = true;
                }
                else Debug.LogWarning("Pixel Campus: bundled WebView2 file unavailable (" + missingRuntimeFile + "); using Windows WebView2.");
            }

            profile = Environment.GetEnvironmentVariable(ProfileVariable);
            externalProfile = !string.IsNullOrEmpty(profile);
            if (externalProfile && fallbackProcess)
            {
                // Explicitly shared profiles need the previous browser host to release its lock.
                try { using (var previous = Process.GetProcessById(parentId)) previous.WaitForExit(5000); }
                catch (ArgumentException) { } // The original player already exited.
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
            if (!externalProfile)
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(local))
                {
                    profile = portablePackage || fallbackProcess
                        ? Path.Combine(local, "PixelCampus", (UsingBundled || externalRuntime) ? "WebView2" : "WebView2-System")
                        : Path.Combine(local, "UnityWebView2");
                    Environment.SetEnvironmentVariable(ProfileVariable, profile);
                }
            }
        }

        internal static bool PrepareProfile()
        {
            profileError = null;
            if (string.IsNullOrEmpty(profile))
            {
                profileError = "LocalAppData";
                return false;
            }
            try
            {
                Directory.CreateDirectory(profile);
                string probe = Path.Combine(profile, ".pixelcampus-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch (Exception ex)
            {
                profileError = ex.GetType().Name + " 0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture);
                return false;
            }
        }

        internal static bool TryStartSystemFallback()
        {
            if (!UsingBundled || fallbackStarted || fallbackProcess) return false;
            try
            {
                using (var current = Process.GetCurrentProcess())
                {
                    var start = new ProcessStartInfo(current.MainModule.FileName)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = Environment.CurrentDirectory,
                        Arguments = PlayerArguments()
                    };
                    start.EnvironmentVariables.Remove(RuntimeVariable);
                    if (!externalProfile) start.EnvironmentVariables.Remove(ProfileVariable);
                    start.EnvironmentVariables[FallbackParentVariable] = current.Id.ToString(CultureInfo.InvariantCulture);
                    using (var child = Process.Start(start))
                    {
                        fallbackStarted = child != null;
                    }
                }
                return fallbackStarted;
            }
            catch (Exception ex)
            {
                systemLaunchError = ex.GetType().Name + " 0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture);
                return false;
            }
        }

        // Preserve the original arguments (including quoted paths) when switching runtimes.
        [DllImport("kernel32.dll", ExactSpelling = true)] private static extern IntPtr GetCommandLineW();
        private static string PlayerArguments()
        {
            string command = Marshal.PtrToStringUni(GetCommandLineW()).TrimStart();
            bool quoted = false;
            for (int i = 0; i < command.Length; i++)
            {
                if (command[i] == '"') quoted = !quoted;
                else if (!quoted && char.IsWhiteSpace(command[i])) return command.Substring(i).TrimStart();
            }
            return "";
        }

        internal static string DescribeFailure(string errorType)
        {
            string prefix = RuntimeDescription + "\n";
            if (profileError != null)
                return prefix + "로그인 데이터를 저장할 사용자 폴더에 쓸 수 없습니다. 폴더의 쓰기 권한과 디스크 여유 공간을 확인한 뒤 재접속해 주세요.\n" + profileError;
            if (errorType == nameof(DllNotFoundException) || errorType == nameof(BadImageFormatException) || errorType == nameof(EntryPointNotFoundException))
            {
                string plugin = Path.Combine(Application.dataPath, "Plugins", "x86_64", "WebView.dll");
                if (!File.Exists(plugin)) return prefix + "WebView.dll을 찾을 수 없습니다. 배포 ZIP 전체를 풀어 실행해 주세요.\n" + errorType;
                string crt = Array.Find(CrtFiles, file => !File.Exists(Path.Combine(playerDirectory, file)));
                if (crt != null) return prefix + crt + " 파일을 앱 폴더에서 확인할 수 없습니다. 무설치 ZIP의 실행 파일과 DLL을 함께 풀어 주세요.\n" + errorType;
                string loader = pluginLoadError != 0 ? " / Win32 " + pluginLoadError : "";
                return prefix + "Windows 로그인 플러그인을 불러올 수 없습니다. 아래 오류 정보를 복사해 전달해 주세요.\n" + errorType + loader;
            }
            if (systemLaunchError != null) return prefix + "Windows WebView2로 앱을 다시 열 수 없습니다. 앱 실행 권한을 확인한 뒤 다시 실행해 주세요.\n" + systemLaunchError;
            string details = string.IsNullOrEmpty(errorType) ? "WebView2 초기화 미완료" : errorType;
            if (missingRuntimeFile != null) details += " / 배포 파일 확인 불가: " + missingRuntimeFile;
            string guidance = externalRuntime ? "지정한 WebView2 경로를 확인하고 다시 실행해 주세요." :
                "Windows의 Microsoft Edge WebView2 Runtime 설치 상태를 확인한 뒤 재접속해 주세요.";
            return prefix + "로그인용 브라우저가 시작되지 않았습니다. " + guidance + "\n" + details;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string fileName, IntPtr reserved, uint flags);
        internal static bool TryPreloadPlugin()
        {
            if (recoveredPlugin != IntPtr.Zero) return false;
            // Search only the plugin directory, player directory, and the Windows system directory.
            const uint trustedSearchDirectories = 0x100 | 0x200 | 0x800;
            string plugin = Path.Combine(Application.dataPath, "Plugins", "x86_64", "WebView.dll");
            // Keep this module loaded for the lifetime of the player, including later WebView calls.
            recoveredPlugin = LoadLibraryExW(plugin, IntPtr.Zero, trustedSearchDirectories);
            pluginLoadError = recoveredPlugin == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
            return recoveredPlugin != IntPtr.Zero;
        }
    }
}
#endif
