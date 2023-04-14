using UnityEditor;

public delegate void AssetImported(string path, long importMilliseconds);
public delegate void AssetsImported(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths);

namespace ArkioUnityPlugin
{
    public class ArkioMenu
    {
        public void OnGUI()
        {
            EditorGUILayout.LabelField("Arkio", EditorStyles.boldLabel);
        }

        [MenuItem("Tools/Arkio/Open Arkio Cloud panel", false, 0)]
        public static void OpenArkioCloudPanel()
        {
            ArkioCloudWindow window = (ArkioCloudWindow)EditorWindow.GetWindow(typeof(ArkioCloudWindow), false, "Arkio Cloud");
            window.Refresh();
            window.Show();
        }

        // Method to turn menu item on or off
        // make it disabled or not
        [MenuItem("Tools/Arkio/Open Arkio Cloud panel", true)]
        public static bool OpenArkioCloudPanel_Validate()
        {
            return true;
        }

        [MenuItem("Tools/Arkio/Export to Arkio PC", false, 20)]
        public static void ExportToPC()
        {
            ArkioOperations.ExportToPC();
        }

        [MenuItem("Tools/Arkio/Import from Arkio PC", false, 21)]
        public static void ImportFromPC()
        {
            ArkioOperations.ImportFromPC();
        }

        [MenuItem("Tools/Arkio/About", false, 40)]
        public static void AboutArkio()
        {
            ArkioAbout window = (ArkioAbout)EditorWindow.GetWindow(typeof(ArkioAbout), false, "About Arkio Unity Plugin");
            window.Show();
        }
    }
}

