using System;
using System.Reflection;
using System.Text.Json;
using WebNotifications;

// Runs the real Windows Credential Manager implementation with Unity-independent JSON adapters.
// The PowerShell runner replaces only the credential target with a unique test target.
namespace UnityEngine
{
    public enum RuntimePlatform { WindowsEditor, WindowsPlayer, Android }
    public static class Application { public static RuntimePlatform platform => RuntimePlatform.WindowsEditor; }
    public static class PlayerPrefs
    {
        public static void SetString(string key, string value) => throw new Exception("Plaintext preferences must not be used on Windows.");
        public static void DeleteKey(string key) { }
        public static void Save() { }
    }
    public static class JsonUtility
    {
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), new JsonSerializerOptions { IncludeFields = true });
        public static T FromJson<T>(string json)
        {
            var result = (T)Activator.CreateInstance(typeof(T), true);
            using var document = JsonDocument.Parse(json);
            foreach (var field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (document.RootElement.TryGetProperty(field.Name, out var property)) field.SetValue(result, property.GetString());
            return result;
        }
    }
}

internal static class CredentialStoreChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void Main()
    {
        var target = (string)typeof(SmartleadCredentialStore).GetField("Target", BindingFlags.NonPublic | BindingFlags.Static).GetRawConstantValue();
        Check(target.StartsWith("PixelCampus/Test/", StringComparison.Ordinal), "Refuse to touch real saved credentials.");
        try
        {
            Check(!SmartleadCredentialStore.TryLoad(out _, out _), "Missing credentials should return false.");
            Check(!SmartleadCredentialStore.Save("", "fake-password"), "An empty ID must not be saved.");
            const string username = "test-student-한글";
            const string password = "fake-only-암호-\"-\\-\n";
            Check(SmartleadCredentialStore.Save(username, password), "Windows credential write failed: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            Check(SmartleadCredentialStore.TryLoad(out var id, out var secret) && id == username && secret == password, "Credential round-trip failed.");
            Check(SmartleadCredentialStore.Save(username, "updated-fake-password"), "Credential replacement failed.");
            Check(SmartleadCredentialStore.TryLoad(out _, out secret) && secret == "updated-fake-password", "Replacement was not restored.");
            SmartleadCredentialStore.Delete();
            Check(!SmartleadCredentialStore.TryLoad(out _, out _), "Deleted credentials must not be restored.");
            Console.WriteLine("PASS: Windows protected credential save, restore, replacement and deletion.");
        }
        finally { SmartleadCredentialStore.Delete(); }
    }
}
