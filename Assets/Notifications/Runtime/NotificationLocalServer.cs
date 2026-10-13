using UnityEngine;

namespace WebNotifications
{
    // Editor convenience only. A desktop build runs the local server separately.
    [DefaultExecutionOrder(-100)]
    public class NotificationLocalServer : MonoBehaviour
    {
#if UNITY_EDITOR_WIN
        private System.Diagnostics.Process server;
        private void OnEnable()
        {
            var manager = GetComponent<NotificationManager>();
            var api = GetComponent<NotificationApiClient>();
            if (manager == null || manager.useWebView || manager.useSampleData || api == null || api.baseUrl.TrimEnd('/') != "http://127.0.0.1:8787") return;
            try
            {
                using (var socket = new System.Net.Sockets.TcpClient())
                {
                    try { if (socket.ConnectAsync("127.0.0.1", 8787).Wait(250) && socket.Connected) return; } catch { }
                }
                var root = System.IO.Directory.GetParent(Application.dataPath).FullName;
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "node.exe", Arguments = "\"" + System.IO.Path.Combine(root, "NotificationServer/server.mjs") + "\"",
                    WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };
                start.EnvironmentVariables["NOTIFICATION_MODE"] = "smartlead";
                start.EnvironmentVariables.Remove("SMARTLEAD_TOKEN");
                start.EnvironmentVariables["PORT"] = "8787";
                server = System.Diagnostics.Process.Start(start);
            }
            catch (System.Exception) { Debug.LogWarning("알림 서버를 실행하지 못했습니다. Node.js 설치를 확인하세요."); }
        }
        private void OnDisable()
        {
            if (server == null) return;
            try { if (!server.HasExited) server.Kill(); } catch { }
            server.Dispose(); server = null;
        }
#endif
    }
}
