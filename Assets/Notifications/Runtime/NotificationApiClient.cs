using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;

namespace WebNotifications
{
    public class NotificationApiClient : MonoBehaviour
    {
        [Tooltip("Local prototype server. Use HTTPS for a deployed server.")]
        public string baseUrl = "http://127.0.0.1:8787";
        private readonly NotificationRequestScope requests = new NotificationRequestScope();
        public void CancelPendingRequests() { requests.CancelAll(); }
        private void OnDisable() { CancelPendingRequests(); }

        public IEnumerator CheckLoginOptions(Action<NotificationLoginOptions> completed)
        {
            using (var lease = requests.Track(UnityWebRequest.Get(baseUrl.TrimEnd('/') + "/api/auth/capabilities")))
            {
                var request = lease.Request;
                request.timeout = 8;
                yield return lease.Send();
                if (lease.Cancelled) yield break;
                NotificationLoginOptions options = null;
                try { options = JsonUtility.FromJson<NotificationLoginOptions>(request.downloadHandler.text); } catch (Exception) { }
                if (options == null || string.IsNullOrEmpty(options.code))
                    options = new NotificationLoginOptions { available = false, code = "config_unavailable", message = "로그인 서버가 준비되지 않았습니다. 잠시 후 연결 방식 확인을 눌러 주세요." };
                completed(options);
            }
        }

        public IEnumerator Authenticate(string action, string json, Action success, Action<string> failure)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address) ||
                !(address.Host == "127.0.0.1" || address.Host == "localhost"))
            {
                failure("학교 계정 로그인은 이 PC의 로컬 알림 서버에서만 지원합니다.");
                yield break;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var lease = requests.Track(new UnityWebRequest(baseUrl.TrimEnd('/') + "/api/auth/" + action, "POST")))
            {
                var request = lease.Request;
                request.uploadHandler = new UploadHandlerRaw(bytes);
                Array.Clear(bytes, 0, bytes.Length);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-Notification-Client", "unity");
                request.timeout = 25;
                json = null;
                yield return lease.Send();
                if (lease.Cancelled) yield break;
                if (request.result == UnityWebRequest.Result.Success) success();
                else
                {
                    string message = null;
                    try { message = JsonUtility.FromJson<NotificationErrorResponse>(request.downloadHandler.text)?.message; } catch (Exception) { }
                    failure(string.IsNullOrEmpty(message) ? "로컬 로그인 서버에 연결하지 못했습니다. Node.js 설치와 서버 실행 상태를 확인하세요." : message);
                }
            }
        }

        public IEnumerator Fetch(Action<NotificationResponse> success, Action<string> failure)
        {
            using (var lease = requests.Track(UnityWebRequest.Get(baseUrl.TrimEnd('/') + "/api/notifications?today=true")))
            {
                var request = lease.Request;
                request.timeout = 10;
                yield return lease.Send();
                if (lease.Cancelled) yield break;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string message = null;
                    try { message = JsonUtility.FromJson<NotificationErrorResponse>(request.downloadHandler.text)?.message; }
                    catch (Exception) { }
                    failure(string.IsNullOrEmpty(message) ? "서버 연결 실패: " + request.error : message);
                    yield break;
                }
                NotificationResponse response;
                try
                {
                    response = JsonUtility.FromJson<NotificationResponse>(request.downloadHandler.text);
                    if (response == null || response.notifications == null)
                        throw new FormatException();
                }
                catch (Exception)
                {
                    failure("알림 JSON 형식을 확인하세요.");
                    yield break;
                }
                success(response);
            }
        }
    }
}
