using UnityEditor;

namespace ArkioUnityPlugin
{
    /** An asset post processor for listening to some asset importing events */
    public class PostProcessImportAsset : AssetPostprocessor
    {
        /** Triggered when OnPostprocessAllAssets gets called */
        public static event AssetsImported OnAssetImported;

        public static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (OnAssetImported != null)
            {
                OnAssetImported.Invoke(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
            }
        }
    }
}