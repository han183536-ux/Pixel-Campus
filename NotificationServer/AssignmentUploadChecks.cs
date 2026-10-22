using System;
using System.IO;
using System.Xml;
using WebNotifications.Editor;

namespace UnityEditor.Android
{
    // The manifest/dependency editor is independent of a running Unity editor.
    public interface IPostGenerateGradleAndroidProject
    {
        int callbackOrder { get; }
        void OnPostGenerateGradleAndroidProject(string path);
    }
}

internal static class AssignmentUploadChecks
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    public static void Main(string[] args)
    {
        var root = args[0];
        var library = Path.Combine(root, "unityLibrary");
        var main = Path.Combine(library, "src", "main");
        Directory.CreateDirectory(main);
        var manifest = Path.Combine(main, "AndroidManifest.xml");
        File.WriteAllText(manifest, "<manifest xmlns:android='http://schemas.android.com/apk/res/android'><application><activity android:name='com.unity3d.player.UnityPlayerActivity' android:exported='true'/></application></manifest>");
        File.WriteAllText(Path.Combine(library, "build.gradle"), "plugins { id 'com.android.library' }\ndependencies {\n    implementation fileTree(dir: 'libs', include: ['*.jar'])\n}\n");
        File.WriteAllText(Path.Combine(root, "gradle.properties"), "org.gradle.jvmargs=-Xmx4096m\nandroid.useAndroidX=false\n");
        var setup = new AndroidCampusSetup();
        setup.OnPostGenerateGradleAndroidProject(library);
        var gradle = File.ReadAllText(Path.Combine(library, "build.gradle"));
        var properties = File.ReadAllText(Path.Combine(root, "gradle.properties"));
        var firstManifest = File.ReadAllText(manifest);
        setup.OnPostGenerateGradleAndroidProject(library);
        Check(gradle == File.ReadAllText(Path.Combine(library, "build.gradle")), "Repeated builds preserve one AndroidX dependency");
        Check(properties == File.ReadAllText(Path.Combine(root, "gradle.properties")), "AndroidX property is enabled idempotently");
        Check(firstManifest == File.ReadAllText(manifest), "Repeated manifest setup does not duplicate permissions");
        Check(gradle.Contains("implementation 'androidx.core:core:1.6.0'") && gradle.Contains("implementation fileTree"), "File picker dependency preserves existing Gradle dependencies");
        Check(properties.Contains("org.gradle.jvmargs=-Xmx4096m") && properties.Contains("android.useAndroidX=true") && !properties.Contains("android.useAndroidX=false"), "Existing Gradle properties are preserved");
        var document = new XmlDocument(); document.Load(manifest);
        var ns = new XmlNamespaceManager(document.NameTable); ns.AddNamespace("android", "http://schemas.android.com/apk/res/android");
        Check(document.SelectNodes("/manifest/uses-permission").Count == 2, "Document chooser adds no broad storage, camera or microphone permissions");
        Check(document.SelectSingleNode("/manifest/application/activity[@android:exported='true']", ns) != null, "Unity launcher activity configuration is preserved");
        Check(document.SelectSingleNode("/manifest/application[@android:hardwareAccelerated='true']", ns) != null, "WebView hardware acceleration remains enabled");
    }
}
