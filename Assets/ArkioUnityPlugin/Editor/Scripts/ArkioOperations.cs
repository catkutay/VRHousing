using UnityEngine;
using UnityEditor;
using System;
using System.Threading.Tasks;
using System.IO;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using SysDia = System.Diagnostics;
using System.Text;
using TinyJson;
using AIECommon = ArkioImportExportCommon;
using Amazon.S3.Model;

namespace ArkioUnityPlugin
{
    // Class with main methods used for importing and
    // exporting to Arkio
    public static class ArkioOperations
    {
        public static async void CloudUploadTest()
        {
            const string AwsExchangeFileBucketName = "exchangefile.arkio.is";
            string objectName = Guid.NewGuid().ToString();

            AwsClient awsClient = new AwsClient();
            awsClient.OnLog += Log;

            int maxByteCount = 10000000;
            Debug.Log(String.Format("Going up to {0} bytes.", maxByteCount));
            int currentByteCount = 1;
            while (currentByteCount <= maxByteCount)
            {
                byte[] bytes = new byte[currentByteCount];
                for (int i = 0; i < currentByteCount; i++)
                {
                    bytes[i] = (byte)UnityEngine.Random.Range(0, 255);
                }
                Debug.Log(String.Format("Attempting to upload {0} bytes.", bytes.Length));
                SysDia.Stopwatch sw = new SysDia.Stopwatch();
                sw.Start();
                PutObjectResponse r1 = await awsClient.PutObjectAsync(AwsExchangeFileBucketName, objectName, "", bytes);
                sw.Stop();
                long ms = sw.ElapsedMilliseconds;

                if (r1 != null)
                {
                    Debug.Log(String.Format("Successfully uploaded {0} bytes in {1} milliseconds.", bytes.Length, ms));
                }
                else
                {
                    Debug.Log(String.Format("Failed to upload. {0} milliseconds passed.", ms));
                }
                currentByteCount *= 10;
            }
            Debug.Log("Done with cloud upload test");
        }

        public static async void UploadRandomBytesToRandomBucket(AwsClient awsClient, int byteCount)
        {
            const string AwsExchangeFileBucketName = "exchangefile.arkio.is";
            string objectName = Guid.NewGuid().ToString();

            byte[] bytes = new byte[byteCount];
            for (int i = 0; i < byteCount; i++)
            {
                bytes[i] = (byte)UnityEngine.Random.Range(0, 255);
            }
            Debug.Log(String.Format("Attempting to upload {0} bytes.", bytes.Length));
            SysDia.Stopwatch sw = new SysDia.Stopwatch();
            sw.Start();
            PutObjectResponse r1 = await awsClient.PutObjectAsync(AwsExchangeFileBucketName, objectName, "", bytes);
            sw.Stop();
            long ms = sw.ElapsedMilliseconds;
            if (r1 != null)
            {
                Debug.Log(String.Format("Successfully uploaded {0} bytes in {1} milliseconds.", bytes.Length, ms));
            }
            else
            {
                Debug.Log(String.Format("Failed to upload. {0} milliseconds passed.", ms));
            }
        }

        // Test sending bytes of data to multiple objects in the cloud
        // all asyncronously at the same time
        public static void UploadToMultipleObjects()
        {
            AwsClient awsClient = new AwsClient();
            awsClient.OnLog += Log;

            int byteCount = 1000000;
            int nrOfUploadJobs = 10;
            for(int i =0; i < nrOfUploadJobs; i++)
            {
                UploadRandomBytesToRandomBucket(awsClient, byteCount);
            }
        }

        // A logging method to call
        public static void Log(string msg)
        {
            Debug.Log(msg);
        }

        public static void LogError(string msg)
        {
            Debug.LogError(msg);
        }

        // Cloud manager for interacting with the cloud
        private static Arkio.CloudLinkManager cloudManager = null;

        // Store cloud manager with its links in editor prefs
        public static void StoreInEditorPrefs(Arkio.CloudLinkManager cloudManager)
        {
            cloudManager.storageProvider = new EditorStorageProvider();
            cloudManager.SaveToStorage();
        }

        // Construct new cloud manager using stored data in editor prefs
        // Returns true if successfull, else false
        public static bool GetCloudManagerFromEditorPrefs(out Arkio.CloudLinkManager cloudManager)
        {
            cloudManager = new Arkio.CloudLinkManager();
            cloudManager.storageProvider = new EditorStorageProvider();
            bool success = cloudManager.ClearLinksAndLoadFromStorage();
            return success;
        }

        // Initializes cloud manager with data from persistent storage
        // If it is not found in persistent storage then a new CloudManager
        // is created with a default link
        public static void InitCloudManager()
        {
            Arkio.CloudExchangeLink cloudLink;
            try
            {
                Arkio.CloudLinkManager cloudman;
                bool gotcloudman = GetCloudManagerFromEditorPrefs(out cloudman);
                if (gotcloudman)
                {
                    cloudManager = cloudman;
                }
                else
                {
                    cloudManager = new Arkio.CloudLinkManager();
                }
                cloudManager.OnLog += Log;
                cloudManager.OnLogError += LogError;
                cloudLink = cloudManager.GetOrCreateDefaultLink();
            }
            catch (Exception e)
            {
                Debug.LogError(String.Format("Failed to initialize cloud manager! {0}", e.Message));
            }
        }

        // Unlink from cloud
        public static void Unlink()
        {
            //TODO put this into a file
            // that can be used both in unity plugin
            // and in arkio, so we don't have
            // the same code in two places
            try
            {
                if (cloudManager != null)
                {
                    // Clear all data and then store that
                    // Later when multiple links are supported
                    // we will probably only be unlinking one
                    // link at a time
                    cloudManager.Clear();
                    StoreInEditorPrefs(cloudManager);
                    bool linked = InitAndCheckLinkedState();
                    Arkio.CloudUtil.Assert(!linked, "Still linked after an attempt to unlink.");
                    Debug.Log("Unlinked from cloud.");
                }
            }
            catch(Exception e)
            {
                Debug.LogError(String.Format("Failure when trying to unlink from cloud. {0}", e.Message));
            }
        }

        public static async Task GetInviteCode()
        {
            LinkAssert();
            // Using default link for now
            // TODO in future use multiple links, when that is ready in UI
            Arkio.CloudExchangeLink cloudLink = cloudManager.GetDefaultLink();

            var result = await cloudLink.GenerateInviteCode();
            if (result.success)
            {
                Debug.Log("Invite code: " + result.code);
            }
            else
            {
                Debug.LogError("Failed to get invite code!");
            }
        }

        // Initializes if needed and creates a new link code
        // If code is supplied then it links using that code.
        // If it's not supplied then a new group is created
        // and a invite code for that group is shown
        public static async Task LinkToCloud(string inviteCode, Action<bool> onFinish)
        {
            // Try initializing cloud system.
            // It needs to be initialized before the linking can be done.
            if (cloudManager == null)
            {
                InitCloudManager();
            }
            Arkio.CloudExchangeLink cloudLink = cloudManager.GetOrCreateDefaultLink();

            bool success = false;

            try
            {
                if (inviteCode == null)
                {
                    var result = await cloudLink.GenerateCodeAndLink();
                    if (result.success)
                    {
                        success = true;
                        string code = result.code;
                        EditorUtility.DisplayDialog("Link plugin to Arkio cloud",
                            String.Format("Enter this code in Arkio to connect your devices:\n\n{0}\n\n(see console to copy-paste code)", code), "Close");
                        Debug.Log(String.Format("Keycode: {0}", code));
                    }
                }
                else
                {
                    await cloudLink.LinkWithExistingCode(inviteCode, null);
                    if (cloudLink.GetLinkingState() == Arkio.CloudExchangeLink.LinkingState.LINKED)
                    {
                        success = true;
                    }
                }

                if (success)
                {
                    try
                    {
                        // Store the data in editor prefs
                        StoreInEditorPrefs(cloudManager);
                    }
                    catch(Exception e)
                    {
                        Debug.LogError(String.Format("Failed to store cloud linking data in Editor Prefs. {0}", e.Message));
                    }
                }
                else
                {
                    Debug.LogError("Failed to join or create group in cloud!");
                }
            }
            catch (Exception e)
            {
                Debug.LogError(String.Format("Failed to join or create group in cloud. {0}", e.Message));
            }
            onFinish?.Invoke(success);
        }

        // Initialize cloud manager if needed and checks if
        // default link is linked
        // return true if linked
        public static bool InitAndCheckLinkedState()
        {
            if (cloudManager == null)
            {
                InitCloudManager();
            }
            return IsLinked(cloudManager);
        }

        // Check if we are linked with the default link
        // on a cloud link manager
        public static bool IsLinked(Arkio.CloudLinkManager cloudMngr)
        {
            if (cloudManager != null)
            {
                return cloudManager.IsLinkedWithDefaultLink();
            }
            return false;
        }

        // Creates a random string
        private static string RandomString()
        {
            return Guid.NewGuid().ToString();
        }

        // Finds the latest entry from an cloud index file
        // Returns the index of the latest entry in entries
        // based on LastWriteTimeUtc in the metadata
        public static int FindLatestEntry(List<Arkio.CloudExchangeLink.IndexFile.Entry> entries)
        {
            int entryIndex = -1;
            DateTime greatestDate = DateTime.MinValue;
            int n = entries.Count;
            for (int i = 0; i < n; i++)
            {
                if (IsArkioToUnity(entries[i]))
                { 
                    if (entries[i].Created != null)
                    {
                        if (entries[i].Created > greatestDate) // We want to get the newest model
                        {
                            greatestDate = (DateTime)(entries[i].Created);
                            entryIndex = i;
                        }
                    }
                }
            }
            return entryIndex;
        }

        // checks if a cloud entry is an arkio export for the unity plugin
        public static bool IsArkioToUnity(Arkio.CloudExchangeLink.IndexFile.Entry entry)
        {
            if (entry.File != null)
            {
                // Is it an arkio export
                if (entry.File.ArkioExport != null) 
                {
                    if (entry.File.Filename != null)
                    {
                        // The unity plugin can only import glb files so if
                        // it's something else then it's not for the unity plugin
                        if (entry.File.Filename.EndsWith(".glb")) 
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // Finds the right entry to download from the cloud list
        // It is the entry that has the same name as name
        // and if there are more than one with that name, it is the most recent one
        // Only looks for entries with job type "ArkioUnityExport" as we are only
        // interested in models being exported from Arkio to Unity.
        // REMARK: Later we might be showing a list in the UI for the user to choose
        // a model to download
        // Returns index of the right entry if found, else -1
        public static int FindRightEntryToDownload(List<Arkio.CloudExchangeLink.IndexFile.Entry> entries, string name)
        {
            int entryIndex = -1;
            DateTime greatestDate = DateTime.MinValue;
            int n = entries.Count;
            for (int i = 0; i < n; i++)
            {
                Arkio.CloudExchangeLink.IndexFile.Entry entry = entries[i];
                if (IsArkioToUnity(entry)) // We are only insterested in Jobs of this type
                {
                    if (entry.HasFilename())
                    {
                        if (entry.File.Filename.Equals(name)) // See if its the same name as the supplied name
                        {
                            if (entry.Created != null)
                            {
                                if (entry.Created > greatestDate) // We want to get the newest model
                                {
                                    greatestDate = (DateTime)(entry.Created);
                                    entryIndex = i;
                                }
                            }
                        }
                    }
                }
            }
            return entryIndex;
        }

        // Assertions for cloud link
        // things that need to be in place
        // when doing cloud operations
        // Use this when needed
        public static void LinkAssert()
        {
            Arkio.CloudUtil.Assert(cloudManager != null, "Cloud manager is not initialized!");

            //TODO remove this when we start using multiple links
            Arkio.CloudUtil.Assert(cloudManager.HasDefaultLink(), "Default link missing.");

            Arkio.CloudExchangeLink defaultLink = cloudManager.GetDefaultLink();

            Arkio.CloudUtil.Assert(defaultLink != null, "Link not ready.");
            Arkio.CloudUtil.Assert(defaultLink.GetLinkingState()
                == Arkio.CloudExchangeLink.LinkingState.LINKED, "Can't do cloud operations if not linked.");
        }

        public static async Task GetIndexFromCloud(Action<Arkio.CloudExchangeLink.IndexFile> onFinish)
        {
            LinkAssert();

            Arkio.CloudExchangeLink cloudLink = cloudManager.GetDefaultLink();

            // TODO maybe we need some timout here
            Arkio.CloudExchangeLink.IndexFile indexFile =
                await cloudLink.DownloadIndex();

            if (indexFile != null && onFinish != null)
            {
                onFinish(indexFile);
            }

        }

        // Downloads a model from the cloud and unzips it and then imports the model
        // entry: The model to download
        public static async Task UpdateFromCloud(string hash, Arkio.CloudExchangeLink.IndexFile.Entry entry)
        {
            LinkAssert();

            Arkio.CloudExchangeLink cloudLink = cloudManager.GetDefaultLink();
            string arkioImportFolderFullPath = Path.Combine(Application.dataPath, GetImportFolder());
            var result = await cloudLink.DownloadCloudResource(hash, entry, arkioImportFolderFullPath);
            if (result.success)
            {
                // Find the .glb file
                string[] glbFiles = Directory.GetFiles(result.destinationFolderFullPath, "*.glb");
                if (glbFiles.Length > 0)
                {
                    string fullFileName = glbFiles[0];
                    string filename = Path.GetFileName(fullFileName);
                    string basename = Path.GetFileNameWithoutExtension(fullFileName);

                    // Start the import process
                    string relativePath = Path.Combine("Assets", Path.Combine(GetImportFolder(), basename));
                    relativePath = Path.Combine(relativePath, filename);
                    StartModelImport(relativePath, true);
                }
                else
                {
                    Debug.LogError(String.Format(
                        "Could not find .glb file in {0}.",
                        result.destinationFolderFullPath));
                }
            }
        }

        // Export the current unity scene to arkio
        // folder : The folder where the model will be saved in
        // filename: The name of the model file to be saved
        // without the .glb ending
        public static void ExportToArkio(string folder, string filename)
        {
            SysDia.Stopwatch sw = new SysDia.Stopwatch();
            sw.Start();
            Debug.Log(String.Format("Export to Arkio started at {0}", DateTime.Now.ToString()));

            Scene scene = SceneManager.GetActiveScene();
            var gameObjects = scene.GetRootGameObjects();
            var transforms = Array.ConvertAll(gameObjects, gameObject => gameObject.transform);

            var exportOptions = new ExportOptions { TexturePathRetriever = ArkioEditorUtil.RetrieveTexturePath };

            // Gets entity id table or creates it if it is not already existing
            ArkioEntityIDTable idTable = ArkioIEUtil.AccessOrCreateEntityIDTable(scene);

            // Need to update the entity table before exporting
            Dictionary<GameObject, ArkioEntityIDTable.ArkioObjectInfo> goIds = idTable.UpdateFromScene(scene);


            var exporter = new GLTFSceneExporter(transforms, goIds, exportOptions);

            // Prepare the directory for putting something in it
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
            Directory.CreateDirectory(folder);

            // Objects to exclude from prefab combining
            exporter.prefabCombineExcludeList = idTable.ArkioModelsInScene;

            // Export the glb file
            exporter.SaveGLB(folder, filename);

            // Shows some info in console
            sw.Stop();
            Debug.Log(String.Format("Model exported to {0} in {1} milliseconds", folder, sw.ElapsedMilliseconds.ToString()));
            int nrOfNodes = exporter.NrOfNodesExported;
            int nrOfTriangles = exporter.TotalTriangleCount;
            int ktris = nrOfTriangles / 1000;
            Debug.Log(String.Format("Model has {0} nodes and {1} K triangles", nrOfNodes, ktris));
            if (nrOfNodes > 500)
            {
                Debug.LogWarning("WARNING, this model has a large number of game objects, it might not load completely in Arkio");
            }
            if (ktris > 250)
            {
                Debug.LogWarning("WARNING, this model has too many polygons to load on mobile devices.");
            }

            if (exporter.MaxNodeCountExceeded)
            {
                string msg = String.Format("Warning: Scene has more than {0} GameObjects/Prefabs. Parts of the scene have not been exported.", exporter.maxNodes);
                Debug.LogWarning(msg);
                EditorUtility.DisplayDialog("Maximum node count exceeded", msg, "OK");
            }

            string dialogTitle = "Exported to Arkio";
            StringBuilder dialogText = new StringBuilder();
            dialogText.Append(String.Format(
                "File{0} exported to Arkio with {1} nodes and {2} K triangles.",
                exporter.MaxNodeCountExceeded ? "" : " successfully", nrOfNodes, ktris));
            if (scene.isDirty)
            {
                // A dialog asking user if the scene should be saved.
                dialogText.Append(" Do you want to save your scene now so that it can later be updated from Arkio?");
                bool saveScene = EditorUtility.DisplayDialog(
                    dialogTitle,
                    dialogText.ToString(),
                    "Yes", "No");
                if (saveScene)
                {
                    EditorSceneManager.SaveScene(scene);
                }
            }
            else
            {
                EditorUtility.DisplayDialog(dialogTitle, dialogText.ToString(), "OK");
            }
        }

        // Get the name of the folder in the unity project
        // under the Assets folder where models from Arkio
        // Are placed
        public static string GetImportFolder()
        {
            return "ArkioImport";
        }

        // Prepares the Arkio import folder in the unity project
        // It creates the directory if it does not exist
        // If the folder exist, nothing is done.
        // Returns true if successful
        // Returns false it a failure happened while trying to create the folder
        public static bool PrepareImportFolder(out string arkioImportFolderFullPath)
        {
            arkioImportFolderFullPath = Path.Combine(Application.dataPath, GetImportFolder());
            return AIECommon.FileUtil.CreateFolderIfMissing(arkioImportFolderFullPath, LogError);
        }

        /** Let user choose .glb file to import
         * @param placeInScene If true, the model will be placed
         * in the currently open scene */
        public static void Import(bool placeInScene)
        {
            // Get the folder where arkio exports files for unity
            string arkioExportFolderInArkio = ArkioIEUtil.GetArkioUnityExportFolder();

            // Ask user to choose file for import
            string path = EditorUtility.OpenFilePanel(
                "Select arkio model to import", arkioExportFolderInArkio, "glb");

            if (path.Length > 0)
            {
                // User has selected a file

                // Prepare the import folder
                string arkioImportFolderFullPath;
                bool success = PrepareImportFolder(out arkioImportFolderFullPath);
                if (!success)
                {
                    return;
                }

                // Prepare folder of the model
                string filename = Path.GetFileName(path);
                string basename = Path.GetFileNameWithoutExtension(filename);

                // Full folder path
                string destFolderFullPath;
                success = AIECommon.FileUtil.PrepareFolderForImportedResource(
                    arkioImportFolderFullPath, basename, out destFolderFullPath, LogError);
                if (!success)
                {
                    return;
                }

                string destFileFullPath = Path.Combine(destFolderFullPath, filename);

                // Copy the model file to the Unity project
                success = CopyFile(path, destFileFullPath);
                if (!success)
                {
                    return;
                }

                // Start the import process
                string relativePath = Path.Combine("Assets", Path.Combine(GetImportFolder(), basename));
                relativePath = Path.Combine(relativePath, filename);
                StartModelImport(relativePath, placeInScene);
            }
            else
            {
                Debug.Log("No file selected!");
            }
        }

        // Copy file from src to dest
        public static bool CopyFile(string src, string dest)
        {
            string filename = Path.GetFileName(src);
            try
            {
                File.Copy(src, dest);
                Debug.Log("File " + filename + " copied to " + dest);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(String.Format(
                    "Could not copy file {0} to {1}. {2}",
                    src, dest, e.Message));
                return false;
            }
        }

        // Start importing process of a file in the Unity project
        // relativePath Relative path to the file
        // such as Assets/ArkioImport/model2/model2.glb
        // placeInScene If true, the model will be placed in currently
        // open unity scene
        public static bool StartModelImport(string relativePath, bool placeInScene)
        {
            try
            {
                AssetImporter assetImp = new AssetImporter();
                if (placeInScene)
                {
                    /* Register this method to be called
                     * after import in order to place the model
                     * in the scene. */
                    assetImp.OnAssetImported += AssetImp_OnAssetImported;
                }
                assetImp.Import(relativePath);
                Debug.Log(String.Format("Import of {0} started at {1}", relativePath, DateTime.Now.ToString()));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(String.Format("Failed to start import process for {0}, {1}", relativePath, e.Message));
                return false;
            }
        }

        // Path of the last arkio model that was imported
        // into unity
        private static string currentImportPath = null;

        // Called after an asset is imported
        private static void AssetImp_OnAssetImported(string path, long importMilliseconds)
        {
            Debug.Log(String.Format("Asset imported, path : {0}, elapsed milliseconds: {1}",
                path, importMilliseconds));

            try
            {
                // Get or create entity table of scene
                Scene scene = SceneManager.GetActiveScene();
                ArkioEntityIDTable entityTable = ArkioIEUtil.AccessOrCreateEntityIDTable(scene);

                GameObject arkioModelInstance = null;
                if (entityTable.ArkioModelsInScene != null)
                {
                    if (entityTable.ArkioModelsInScene.Length > 0)
                    {
                        // There is already an arkio model in the scene
                        //Check if it is the same as the one that was imported
                        foreach (GameObject model in entityTable.ArkioModelsInScene)
                        {

                            string filenameWithoutExt = Path.GetFileNameWithoutExtension(path);
                            if (filenameWithoutExt.EndsWith(model.name))
                            {
                                // The model has been imported before
                                arkioModelInstance = model;
                                break;
                            }
                        }
                    }
                }

                // If it's not already there, place the model in the scene
                if (arkioModelInstance == null)
                {
                    // Add arkio model instance
                    GameObject go = (GameObject)AssetDatabase.LoadAssetAtPath(path, typeof(GameObject));
                    int objCount = SceneUtil.GetObjectCountRecursive(go.transform);
                    Debug.Log("Placing newly imported model into current scene.");
                    arkioModelInstance = PrefabUtility.InstantiatePrefab(go) as GameObject;

                    Debug.Log(String.Format("Added {0} objects to scene.", objCount));

                    // Add new model to ArkioModelsInScene array of entityTable
                    GameObject[] models = entityTable.ArkioModelsInScene;
                    GameObject[] modelsNew;
                    int modelsLen = 0;
                    if (models != null)
                    {
                        modelsLen = models.Length;
                    }
                    modelsNew = new GameObject[modelsLen + 1];
                    if (modelsLen > 0)
                    {
                        models.CopyTo(modelsNew, 0);
                    }
                    modelsNew[modelsNew.Length - 1] = arkioModelInstance;
                    entityTable.ArkioModelsInScene = modelsNew;
                }
                else
                {
                    Debug.Log(String.Format("Found model {0} in scene.", arkioModelInstance.name));
                }

                currentImportPath = path;

                // We want to update some things after prefab instances in the current scene have
                // been updated
                PrefabUtility.prefabInstanceUpdated += OnPrefabInstanceUpdated;
            }
            catch (Exception e)
            {
                Debug.LogError(String.Format("Failed to process model after importing. {0}", e.Message));
            }
        }

        // Called after prefab instance updates
        private static void OnPrefabInstanceUpdated(GameObject instance)
        {
            Debug.Log("Prefab instance updated");
            try
            {
                PrefabUtility.prefabInstanceUpdated -= OnPrefabInstanceUpdated;

                if (currentImportPath != null)
                {
                    string filenameWithoutExt = Path.GetFileNameWithoutExtension(currentImportPath);
                    if (filenameWithoutExt.EndsWith(instance.name))
                    {
                        // It is the last model that was imported

                        // Get or create entity table of scene
                        Scene scene = SceneManager.GetActiveScene();
                        ArkioEntityIDTable entityTable = ArkioIEUtil.AccessOrCreateEntityIDTable(scene);

                        // Update the scene
                        ArkioImportSceneUpdater sceneUpdater =
                            new ArkioImportSceneUpdater(
                                entityTable, instance, scene);
                        Debug.Log("Updating the scene based on the newly imported model.");
                        sceneUpdater.UpdateScene();
                    }
                    currentImportPath = null;
                }
            }
            catch (Exception e)
            {
                Debug.LogError(String.Format("Failed when trying to update scene! {0}", e.Message));
            }
        }

        // Exports the scene to a glb and uploads the file to the cloud
        // Need to be linked for this to work
        public static async Task SyncToCloud()
        {
            LinkAssert();

            try
            {
                Debug.Log("Exporting model to Arkio cloud.");
                string outerFolder;
                string innerFolder;
                string sceneName;
                try
                {
                    Scene scene = SceneManager.GetActiveScene();
                    sceneName = ArkioIEUtil.GetExportedSceneName(scene);
                    string tempFolder = ArkioIEUtil.GetArkioImportTempFolder();

                    if (!Directory.Exists(tempFolder))
                    {
                        Directory.CreateDirectory(tempFolder);
                    }

                    // Create random folder name
                    string randStr = RandomString();
                    outerFolder = Path.Combine(tempFolder, randStr);
                    if (Directory.Exists(outerFolder))
                    {
                        Directory.Delete(outerFolder, true);
                    }
                    Directory.CreateDirectory(outerFolder);
                    innerFolder = Path.Combine(outerFolder, sceneName);
                    ExportToArkio(innerFolder, sceneName);
                }
                catch (Exception e)
                {
                    Debug.LogError(String.Format("Failed to export model to glb. {0}", e.Message));
                    return;
                }

                // Using default link for now
                // TODO in future use multiple links, when that is ready in UI
                Arkio.CloudExchangeLink cloudLink = cloudManager.GetDefaultLink();

                // Puts model folder in zip file and sends it to the cloud
                bool success = await cloudLink.UploadResourceToCloud(
                    innerFolder, sceneName + ".glb",
                    Arkio.CloudExchangeLink.IndexFile.Entry.FileInfo.FileType.ArkioImport,
                    Arkio.CloudExchangeLink.IndexFile.Entry.FileInfo.IconUnity);

                if (success)
                {
                    Debug.Log("Uploaded file to cloud.");
                }
                else
                {
                    Debug.Log("Failed to upload file to cloud.");
                }

                // Delete the files that were exported on the local computer
                // because we don't need them anymore after uploading them
                // to the cloud
                if (Directory.Exists(outerFolder))
                {
                    Directory.Delete(outerFolder, true);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(String.Format("A failure occured when trying to sync to cloud. {0}", e.Message));
            }
        }

        // Export the model to local Arkio importing folder
        public static void ExportToPC()
        {
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                string sceneName = ArkioIEUtil.GetExportedSceneName(scene);
                string arkioImportFolder = ArkioIEUtil.GetArkioUnityImportFolder();
                string folder = Path.Combine(arkioImportFolder, sceneName);
                ArkioOperations.ExportToArkio(folder, sceneName);
            }
            catch (Exception e)
            {
                Debug.LogError(String.Format("Failed exporting to Arkio! {0}", e.Message));
            }
        }

        // Import a model that was exported from Arkio
        // on the local computer
        public static void ImportFromPC()
        {
            try
            {
                ArkioOperations.Import(true);
            }
            catch (Exception e)
            {
                Debug.LogError(String.Format("Failed to import glb file! {0}", e.Message));
            }
        }
    }
}

