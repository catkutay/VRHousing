using System.Collections.Generic;
using SysDia = System.Diagnostics;
using UnityEditor;

namespace ArkioUnityPlugin
{
    /** Helper class for importing assets */
    public class AssetImporter
    {
        /** Relative path inside unity project to the asset. */
        protected string relativePath;

        /** Event that gets triggered aftere an asset is imported */
        public event AssetImported OnAssetImported;

        protected SysDia.Stopwatch importStopwatch;

        /** Starts the import */
        public void Import(string relativePath)
        {
            this.relativePath = relativePath;

            /* Using PostProcessImportAsset here to get detected when the asset has finished importing. */
            PostProcessImportAsset.OnAssetImported += PostProcessImportAsset_OnAssetImported;


            importStopwatch = new SysDia.Stopwatch();
            importStopwatch.Start();
            /* Starts the import */
            AssetDatabase.ImportAsset(relativePath);
        }

        /** Split strings in array of strings
         * and return everything in one array */
        public static string[] Split(string[] strings, char separator)
        {
            List<string> result = new List<string>();
            foreach (string str in strings)
            {
                string[] split = str.Split(separator);
                foreach (string s in split)
                {
                    result.Add(s);
                }
            }
            return result.ToArray();
        }

        /** Check if two paths are the same even if the string
         * representing them may have different ways of representing
         * folder slashes.
         */
        public static bool ArePathsSame(string path1, string path2)
        {
            string[] path1Split = Split(path1.Split('\\'), '/');
            string[] path2Split = Split(path2.Split('\\'), '/');
            if (path1Split.Length != path2Split.Length)
            {
                return false;
            }
            int n = path1Split.Length;
            for (int i = 0; i < n; i++)
            {
                if (!path1Split[i].Equals(path2Split[i]))
                {
                    return false;
                }
            }
            return true;
        }

        protected void PostProcessImportAsset_OnAssetImported(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var imp in importedAssets)
            {
                if (ArePathsSame(imp, relativePath))
                {
                    PostProcessImportAsset.OnAssetImported -= PostProcessImportAsset_OnAssetImported;
                    if (OnAssetImported != null)
                    {
                        importStopwatch.Stop();
                        OnAssetImported.Invoke(relativePath, importStopwatch.ElapsedMilliseconds);
                    }
                }
            }
        }
    }
}

