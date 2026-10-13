using System;
using System.Collections;
using UnityEngine;

namespace WebNotifications
{
    [RequireComponent(typeof(NotificationManager))]
    public class NotificationLoginUI : MonoBehaviour
    {
        private string username = "", password = "", message = "";
        private bool busy;
        private NotificationManager manager;
        private NotificationLoginOptions options;
        public bool LoginUnavailable => options != null && !options.available && !manager.IsAuthenticated;
        [Serializable] private class Credentials { public string username, password; }
        private void Awake() { manager = GetComponent<NotificationManager>(); }
        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1);
            if (!manager.useSampleData && !manager.useWebView) yield return CheckOptions();
        }
        private IEnumerator CheckOptions()
        {
            busy = true;
            yield return GetComponent<NotificationApiClient>().CheckLoginOptions(result => { options = result; });
            busy = false;
        }
        private void OnDisable() { password = ""; username = ""; busy = false; }
        public void Draw(GUIStyle label)
        {
            GUILayout.Label("한림 SmartLEAD · 학교 계정", label);
            if (manager.IsAuthenticated)
            {
                GUILayout.Label("학교 계정 연결됨", label);
                GUI.enabled = !busy;
                if (GUILayout.Button("로그아웃")) StartCoroutine(Logout());
                GUI.enabled = true;
            }
            else
            {
                if (options != null) GUILayout.Label(options.message, label);
                GUI.enabled = !busy;
                if (GUILayout.Button(busy ? "연결 방식 확인 중…" : "연결 방식 확인")) StartCoroutine(CheckOptions());
                GUI.enabled = true;
                if (options != null && !options.available)
                {
                    if (GUILayout.Button("SmartLEAD 웹사이트 열기")) Application.OpenURL("https://smartlead.hallym.ac.kr/login/index.php");
                    GUILayout.Label("웹사이트 로그인은 원문 확인용이며 Unity 알림 연결이 자동으로 완료되는 것은 아닙니다.", label);
                    return;
                }
                GUI.enabled = !busy && options != null && options.available;
                GUILayout.Label("아이디", label);
                username = GUILayout.TextField(username, 256);
                GUILayout.Label("비밀번호", label);
                password = GUILayout.PasswordField(password, '●', 2048);
                GUI.enabled = !busy && options != null && options.available && !string.IsNullOrWhiteSpace(username) && password.Length > 0;
                if (GUILayout.Button(busy ? "학교 계정 확인 중…" : "학교 계정으로 로그인", GUILayout.Height(30)))
                    StartCoroutine(Login());
                GUI.enabled = true;
                GUILayout.Label("비밀번호는 저장하지 않습니다. 학교의 앱 로그인이 허용된 계정으로 이용하세요.", label);
            }
            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message, label);
            GUILayout.Space(8);
        }
        private IEnumerator Login()
        {
            busy = true; message = "학교 계정을 확인하고 있습니다…";
            manager.BeginAuthentication();
            var data = new Credentials { username = username.Trim(), password = password };
            var request = GetComponent<NotificationApiClient>().Authenticate("login", JsonUtility.ToJson(data), () =>
            {
                message = "로그인했습니다."; username = ""; manager.FinishAuthentication(true);
            }, error => { message = error; manager.FinishAuthentication(false); });
            password = ""; data.password = ""; data = null;
            yield return request;
            busy = false;
        }
        private IEnumerator Logout()
        {
            busy = true; message = "로그아웃 중…";
            manager.BeginAuthentication();
            yield return GetComponent<NotificationApiClient>().Authenticate("logout", "{}", () =>
            {
                message = "로그아웃했습니다."; manager.FinishAuthentication(false);
            }, error => { message = error + " 서버를 종료하면 로그인 정보가 제거됩니다."; manager.FinishAuthentication(false); });
            busy = false;
        }
    }
}
