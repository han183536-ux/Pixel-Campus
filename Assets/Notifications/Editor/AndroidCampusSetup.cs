using System.IO;
using System.Xml;
using UnityEditor.Android;

namespace WebNotifications.Editor
{
    // Keep Unity's generated activity/launcher configuration and add only the
    // networking and WebView presentation settings required by this application.
    public sealed class AndroidCampusSetup : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";
        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            ConfigureManifest(Path.Combine(path, "src", "main", "AndroidManifest.xml"));
        }

        public static void ConfigureManifest(string path)
        {
            var document = new XmlDocument { PreserveWhitespace = true };
            document.Load(path);
            var manifest = document.DocumentElement;
            if (manifest == null || manifest.Name != "manifest")
                throw new InvalidDataException("Android manifest root is missing: " + path);
            AddPermission(document, manifest, "android.permission.INTERNET");
            AddPermission(document, manifest, "android.permission.ACCESS_NETWORK_STATE");
            var application = manifest.SelectSingleNode("application") as XmlElement;
            if (application != null)
            {
                application.SetAttribute("hardwareAccelerated", AndroidNamespace, "true");
                foreach (XmlElement activity in application.SelectNodes("activity"))
                {
                    activity.SetAttribute("hardwareAccelerated", AndroidNamespace, "true");
                    // Match upstream's keyboard handling; WebViewObject adjusts its
                    // bottom margin while the Android IME is visible in portrait mode.
                    activity.SetAttribute("windowSoftInputMode", AndroidNamespace, "adjustPan");
                }
            }
            document.Save(path);
        }

        private static void AddPermission(XmlDocument document, XmlElement manifest, string name)
        {
            foreach (XmlElement existing in manifest.SelectNodes("uses-permission"))
                if (existing.GetAttribute("name", AndroidNamespace) == name) return;
            var permission = document.CreateElement("uses-permission");
            permission.SetAttribute("name", AndroidNamespace, name);
            manifest.AppendChild(permission);
        }
    }
}
