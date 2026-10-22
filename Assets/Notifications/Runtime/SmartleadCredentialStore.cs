using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace WebNotifications
{
    // Passwords never go into PlayerPrefs in plaintext. Only OS-protected data is persisted.
    internal static class SmartleadCredentialStore
    {
        private const string Target = "PixelCampus/Smartlead";
        [Serializable] private class Account { public string username, password; }
        public static bool IsSupported => Application.platform == RuntimePlatform.WindowsEditor ||
            Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.Android;

        public static bool Save(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return false;
            try
            {
                var json = JsonUtility.ToJson(new Account { username = username, password = password });
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                var bytes = Encoding.Unicode.GetBytes(json);
                var pointer = Marshal.AllocHGlobal(bytes.Length);
                try
                {
                    Marshal.Copy(bytes, 0, pointer, bytes.Length);
                    var credential = new Credential { Type = 1, TargetName = Target, UserName = username,
                        CredentialBlobSize = (uint)bytes.Length, CredentialBlob = pointer, Persist = 2 };
                    return CredWrite(ref credential, 0);
                }
                finally
                {
                    for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(pointer, i, 0);
                    Marshal.FreeHGlobal(pointer); Array.Clear(bytes, 0, bytes.Length);
                }
#elif UNITY_ANDROID && !UNITY_EDITOR
                using (var cipher = AndroidCipher(1, null))
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    try
                    {
                        var encrypted = cipher.Call<sbyte[]>("doFinal", Signed(bytes));
                        var iv = cipher.Call<sbyte[]>("getIV");
                        PlayerPrefs.SetString(Target, Convert.ToBase64String(Unsigned(iv)) + "." + Convert.ToBase64String(Unsigned(encrypted)));
                        PlayerPrefs.Save(); return true;
                    }
                    finally { Array.Clear(bytes, 0, bytes.Length); }
                }
#else
                return false;
#endif
            }
            catch { return false; }
        }

        public static bool TryLoad(out string username, out string password)
        {
            username = null; password = null;
            try
            {
                string json;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                if (!CredRead(Target, 1, 0, out var pointer)) return false;
                try
                {
                    var credential = Marshal.PtrToStructure<Credential>(pointer);
                    if (credential.CredentialBlobSize == 0 || credential.CredentialBlobSize > 24000) return false;
                    json = Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
                }
                finally { CredFree(pointer); }
#elif UNITY_ANDROID && !UNITY_EDITOR
                var parts = PlayerPrefs.GetString(Target, "").Split('.');
                if (parts.Length != 2) return false;
                using (var cipher = AndroidCipher(2, Signed(Convert.FromBase64String(parts[0]))))
                {
                    var decrypted = cipher.Call<sbyte[]>("doFinal", Signed(Convert.FromBase64String(parts[1])));
                    var bytes = Unsigned(decrypted);
                    try { json = Encoding.UTF8.GetString(bytes); }
                    finally { Array.Clear(bytes, 0, bytes.Length); Array.Clear(decrypted, 0, decrypted.Length); }
                }
#else
                return false;
#endif
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || (UNITY_ANDROID && !UNITY_EDITOR)
                var account = JsonUtility.FromJson<Account>(json);
                if (account == null || string.IsNullOrWhiteSpace(account.username) || string.IsNullOrEmpty(account.password)) return false;
                username = account.username; password = account.password; return true;
#endif
            }
            catch { return false; }
        }

        public static void Delete()
        {
            try
            {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                CredDelete(Target, 1, 0);
#elif UNITY_ANDROID && !UNITY_EDITOR
                using (var store = AndroidKeyStore()) store.Call("deleteEntry", Target);
#endif
            }
            catch { /* The local encrypted record is still removed if the OS key is unavailable. */ }
            PlayerPrefs.DeleteKey(Target); PlayerPrefs.Save();
        }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags, Type;
            public string TargetName, Comment;
            public long LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist, AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias, UserName;
        }
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
        [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
#elif UNITY_ANDROID && !UNITY_EDITOR
        private static sbyte[] Signed(byte[] bytes) { var result = new sbyte[bytes.Length]; Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length); return result; }
        private static byte[] Unsigned(sbyte[] bytes) { var result = new byte[bytes.Length]; Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length); return result; }
        private static AndroidJavaObject AndroidKeyStore()
        {
            using (var type = new AndroidJavaClass("java.security.KeyStore"))
            {
                var store = type.CallStatic<AndroidJavaObject>("getInstance", "AndroidKeyStore");
                store.Call("load", new object[] { null }); return store;
            }
        }
        private static AndroidJavaObject AndroidCipher(int mode, sbyte[] iv)
        {
            using (var store = AndroidKeyStore())
            {
                if (!store.Call<bool>("containsAlias", Target))
                {
                    if (mode != 1) throw new InvalidOperationException();
                    using (var generatorType = new AndroidJavaClass("javax.crypto.KeyGenerator"))
                    using (var generator = generatorType.CallStatic<AndroidJavaObject>("getInstance", "AES", "AndroidKeyStore"))
                    using (var builder = new AndroidJavaObject("android.security.keystore.KeyGenParameterSpec$Builder", Target, 3))
                    {
                        using (builder.Call<AndroidJavaObject>("setBlockModes", new object[] { new string[] { "GCM" } })) { }
                        using (builder.Call<AndroidJavaObject>("setEncryptionPaddings", new object[] { new string[] { "NoPadding" } })) { }
                        using (var specification = builder.Call<AndroidJavaObject>("build")) generator.Call("init", specification);
                        using (generator.Call<AndroidJavaObject>("generateKey")) { }
                    }
                }
                using (var key = store.Call<AndroidJavaObject>("getKey", Target, null))
                using (var cipherType = new AndroidJavaClass("javax.crypto.Cipher"))
                {
                    var cipher = cipherType.CallStatic<AndroidJavaObject>("getInstance", "AES/GCM/NoPadding");
                    try
                    {
                        if (mode == 1) cipher.Call("init", mode, key);
                        else using (var parameters = new AndroidJavaObject("javax.crypto.spec.GCMParameterSpec", 128, iv)) cipher.Call("init", mode, key, parameters);
                        return cipher;
                    }
                    catch { cipher.Dispose(); throw; }
                }
            }
        }
#endif
    }
}
