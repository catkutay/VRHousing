using UnityEditor;

namespace ArkioUnityPlugin
{
    //Storage provider for storing values in editor prefs in unity
    public class EditorStorageProvider : Arkio.IStorageProvider
    {
        public string GetString(string key)
        {
            return EditorPrefs.GetString(key);
        }

        public void SetString(string key, string value)
        {
            EditorPrefs.SetString(key, value);
        }

        public bool HasKey(string key)
        {
            return EditorPrefs.HasKey(key);
        }
    }
}
