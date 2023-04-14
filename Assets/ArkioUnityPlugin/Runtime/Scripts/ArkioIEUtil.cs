using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using UnityEngine.SceneManagement;
using System;
using System.Text;

namespace ArkioUnityPlugin
{
    /** Utility class with methods for exporting
     * importing to and from Arkio */
    public static class ArkioIEUtil
    {
        // Creates name for an exported node
        public static string CreateNameForObject(ArkioEntityIDTable.ArkioObjectInfo objInfo)
        {
            // Let prefix tell the origin of the object
            // That is if it was created originally in Arkio or Unity or somewhere else
            StringBuilder name = new StringBuilder();
            name.Append(objInfo.origin.ToString());
            name.Append("_");

            // Create id string from id number
            string idStr = ArkioImportExportCommon.EntityIDUtil.EntityIdToString(objInfo.id);

            name.Append(idStr);
            string nameStr = name.ToString();
            return nameStr;
        }

        // Get name of scene to export
        public static string GetExportedSceneName(Scene scene)
        {
            if (scene.name != null)
            {
                if (!scene.name.Equals(""))
                {
                    return scene.name;
                }
            }
            return "Untitled";
        }

        public static ArkioEntityIDTable AccessOrCreateEntityIDTable(Scene scene)
        {
            ArkioEntityIDTable idTable = null;
            List<ArkioEntityIDTable> arkInfos = SceneUtil.FindComponents<ArkioEntityIDTable>(scene);
            if (arkInfos.Count > 0)
            {
                idTable = arkInfos[0];
            }
            else
            {
                GameObject go = new GameObject();
                idTable = go.AddComponent<ArkioEntityIDTable>();
                go.name = "ArkioEntityIDs";
            }
            idTable.CleanModelsArray();

            // Setting entity id table as dirty.
            // Causes scene to become dirty.
            // This will allow the user to save the scene.
            // Which is needed for the round tripping to work.
            EditorUtility.SetDirty(idTable);

            return idTable;
        }

        /** Get export forlder for arkio. Here can be found files that could
         * possibly be imported. */
        public static string GetArkioUnityExportFolder()
        {
            string arkioFolder = GetArkioDocumentsFolder();
            string exportFolder = Path.Combine(arkioFolder, @"Export\Unity");
            return exportFolder;
        }

        /** Get folder where the models are exported to, that can then
         * be imported into arkio. */
        public static string GetArkioUnityImportFolder()
        {
            string arkioFolder = GetArkioDocumentsFolder();
            string importFolder = Path.Combine(arkioFolder, @"Import\Unity");
            return importFolder;
        }

        public static string GetArkioImportTempFolder()
        {
            string arkioFolder = GetArkioDocumentsFolder();
            string importFolder = Path.Combine(arkioFolder, @"Import\Temp");
            return importFolder;
        }

        /** Get the arkio documents folder */
        public static string GetArkioDocumentsFolder()
        {
            string documentsFolder =
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string arkioFolder = Path.Combine(documentsFolder, "Arkio");
            return arkioFolder;
        }

        // Get initialized random generator
        public static System.Random GetRandom()
        {
            System.Random rand = new System.Random(System.Guid.NewGuid().GetHashCode());
            return rand;
        }

        // Create a random unsigned 64 bit integer
        private static ulong RandomU64(System.Random rand)
        {
            uint int1 = (uint)rand.Next();
            uint int2 = (uint)rand.Next();
            ulong l1 = int1;
            ulong l2 = int2;
            ulong rand64 = l1 | (l2 << 32);
            return rand64;
        }

        // Create a random unsigned 60 bit integer
        // Returns a 64 bit integer with the highest 4 bits
        // always set to 0
        public static ulong RandomEntityId(System.Random rand)
        {
            ulong u60 = RandomU64(rand);

            // an entity ID is 60 bits so that we get 10-character in its base64 representation.
            // the highest bit is always set to 1 to give us the option of using shorter IDs in the future.
            u60 = (u60 & 0x0FFFFFFFFFFFFFFFU) | 0x0800000000000000;

            return u60;
        }
    }
}

