using System.IO;
using System.Xml;
using System.Text.RegularExpressions;
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
            ConfigureFileUploadDependencies(path);
        }

        public static void ConfigureFileUploadDependencies(string path)
        {
            // The official Fragment variant references AndroidX FileProvider.
            // Gradle resolves its transitive dependencies; do not bundle duplicate AARs.
            var gradlePath = Path.Combine(path, "build.gradle");
            var gradle = File.ReadAllText(gradlePath);
            if (!gradle.Contains("androidx.core:core:"))
            {
                var dependencies = Regex.Match(gradle, @"(?m)^\s*dependencies\s*\{");
                if (!dependencies.Success) throw new InvalidDataException("Android dependencies block is missing: " + gradlePath);
                gradle = gradle.Insert(dependencies.Index + dependencies.Length, "\n    implementation 'androidx.core:core:1.6.0'");
                File.WriteAllText(gradlePath, gradle);
            }
            var propertiesPath = Path.Combine(Path.GetDirectoryName(path), "gradle.properties");
            var properties = File.Exists(propertiesPath) ? File.ReadAllText(propertiesPath) : "";
            var androidX = new Regex(@"(?m)^\s*android\.useAndroidX\s*=.*$");
            properties = androidX.IsMatch(properties) ? androidX.Replace(properties, "android.useAndroidX=true") : properties + "\nandroid.useAndroidX=true\n";
            File.WriteAllText(propertiesPath, properties);
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
