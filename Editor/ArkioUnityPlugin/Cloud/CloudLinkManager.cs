using System.Collections.Generic;
using TinyJson;
using System;
using System.IO;

namespace Arkio
{
    // Class for managing a collection
    // of multiple cloud links
    public class CloudLinkManager
    {
        // Enum for state of cloud files
        public enum CloudFileState
        {
            // The file has not been imported or there is
            // a newer version, available on the cloud.
            NewVersionAvailable,

            // The file is in the process of being downloaded
            // or imported.
            Importing,

            // The file has been downloaded and is up to date.
            UpToDate,

            // This means that the file is not available
            Unavailable,
        }

        public CloudLinkManager()
        {

        }

        public CloudLinkManager(Action<string> onLog)
        {
            OnLog = onLog;
        }

        // The cloud links
        protected Dictionary<string, CloudExchangeLink> links =
            new Dictionary<string, CloudExchangeLink>();

        // HashSet with files that are currently being downloaded
        private HashSet<string> filesDownloading = new HashSet<string>();

        // For checking is something is downloading
        public bool IsAnyFileDownloading()
        {
            return filesDownloading.Count > 0;
        }

        // Add entry to files being downloaded
        private void AddToFilesDownloading(CloudExchangeLink.IndexFile.Entry entry)
        {
            string path = GetEntryLocalPath(entry);
            filesDownloading.Add(path);
        }

        private void RemoveFromFilesDownloading(CloudExchangeLink.IndexFile.Entry entry)
        {
            string path = GetEntryLocalPath(entry);
            filesDownloading.Remove(path);
        }

        // Cloud entries that are in the process of being downloaded or imported
        // When a download starts, the hash of the downloading entry in this
        // collection
        private HashSet<string> entriesImporting = new HashSet<string>();

        // Number of download attempts for a particular entry
        // Keys are entry hashes/ids and values are number of download attempts for that entry
        private Dictionary<string, int> entryDownloadAttempts = new Dictionary<string, int>();

        // Get number of download attempts for entry
        public int GetNrOfDownloadAttemptsForEntry(string entryHash)
        {
            if (entryDownloadAttempts.ContainsKey(entryHash))
            {
                return entryDownloadAttempts[entryHash];
            }
            return 0;
        }

        public delegate DateTime? StrToNullableDate(string str);

        // Get name of folder where resource files for an entry should be put under.
        private string GetEntryFolder(CloudExchangeLink.IndexFile.Entry entry)
        {
            if (entry.File != null && entry.File.Icon != null)
            {
                return entry.File.Icon;
            }
            return "";
        }

        private string GetEntryLocalPath(CloudExchangeLink.IndexFile.Entry entry)
        {
            string subFolder = GetEntryFolder(entry);
            string filePath = Path.Combine(subFolder, Path.GetFileNameWithoutExtension(entry.File.Filename));
            return filePath;
        }

        // Check if the main file of an entry has already been downloaded
        //TODO we maybe also need to make sure that other files in the resource
        // like e.g. .mtl file have also been downloaded
        // REMARK: Returns true if a a newer entry with the same main file name has
        // already been downloaded.
        public bool IsEntryAlreadyDownloaded(
            CloudExchangeLink.IndexFile.Entry entry,
            string importFolder)
        {
            string subFolder = GetEntryFolder(entry);

            string fullFileName = Path.Combine(importFolder, subFolder);
            fullFileName = Path.Combine(fullFileName, Path.GetFileNameWithoutExtension(entry.File.Filename));
            fullFileName = Path.Combine(fullFileName, entry.File.Filename);

            if (File.Exists(fullFileName))
            {
                DateTime lastWriteTime = File.GetLastWriteTimeUtc(fullFileName);
                if (entry.Created > lastWriteTime)
                {
                    // Entry is newer than local file
                    return false;
                }
                return true;
            }
            return false;
        }

        // Check if the main file in an cloud entry is already being downloaded
        public bool IsEntryFileBeingDownloaded(CloudExchangeLink.IndexFile.Entry entry)
        {
            string path = GetEntryLocalPath(entry);
            bool downloading = filesDownloading.Contains(path);
            return downloading;
        }

        // Check the state of a model file
        // That is, if it is up to date, if there is a version
        // available on the cloud or if it is in the process of being imported
        // Also has the side effect that entriesImporting is updated in the case where
        // the newest version of a file from the cloud has been imported
        // filename : The filename to check
        // indexFile : Cloud index file
        // cloudDirectory : The directory of imported models from the cloud, example : @"Arkio/Import/Unity/"
        // GetURILastImportAttemptLastWriteTimeMethod A method that is used to get date of last import of a file
        // such as this method
        // ResourceManager.Instance.GetURILastImportAttemptLastWriteTime
        // Returns the state
        //
        // REMARK : This is very specific to Arkio. Not sure if this will ever be used in other software, so
        // it may be better to move this method from the cloud system code and into some place in the Arkio
        // project
        public CloudFileState GetFileState(
            string filename,
            string cloudDirectory,
            CloudExchangeLink.IndexFile indexFile,
            StrToNullableDate GetURILastImportAttemptLastWriteTimeMethod)
        {
            // Find newest entry with a particular name
            DateTime maxDate = DateTime.MinValue;
            string newestEntryHash = null;

            bool currentlyImporting = false;

            foreach(string hash in indexFile.Entries.Keys)
            {
                CloudExchangeLink.IndexFile.Entry entry = indexFile.Entries[hash];
                if (entry.HasFilename())
                {
                    if (entry.File.Filename.Equals(filename))
                    {
                        if (entriesImporting.Contains(hash))
                        {
                            currentlyImporting = true;
                        }

                        if (entry.Created > maxDate)
                        {
                            maxDate = entry.Created;
                            newestEntryHash = hash;
                        }
                    }
                }

            }

            if (newestEntryHash == null && !currentlyImporting)
            {
                // The file is not found on the cloud
                return CloudFileState.Unavailable;
            }

            // Check if it has been imported
            string fullURI = cloudDirectory + filename + @"/" + filename + ".glb";
            DateTime? lastWriteTime = GetURILastImportAttemptLastWriteTimeMethod(fullURI);
            if (lastWriteTime > maxDate)
            {
                // The local file is newer
                // It is then up to date

                // Remove it from entriesImporting, since it is imported
                // if it is still there
                if (entriesImporting.Contains(newestEntryHash))
                {
                    entriesImporting.Remove(newestEntryHash);
                }

                return CloudFileState.UpToDate;
            }
            else
            {
                // File on server is newer
                if (currentlyImporting)
                {
                    // If we are in the process of importing, then return that
                    return CloudFileState.Importing;
                }
                else
                {
                    // There is a newer version available on the cloud
                    return CloudFileState.NewVersionAvailable;
                }
            }

        }

        // Key used for storing the cloud link manager data
        public const string storageKey = "ArkioCloudManager";

        // Storage provider for saving the cloud link manager data
        public IStorageProvider storageProvider;

        // For writing out log messages
        public Action<string> OnLog;

        // Like OnLog, but for error messages
        public Action<string> OnLogError;

        // Event that happens when a download starts
        // in one of the links
        public Action<string, CloudExchangeLink.IndexFile.Entry> OnDownloadStarted;

        // Called when download of an object starts
        private void onDownloadStarted(string hash, CloudExchangeLink.IndexFile.Entry entry)
        {
            //REMARK : Using this method and OnDownloadFailed to
            // add/remove items in entriesImporting. This may not
            // be the best way to do that. It would probably be
            // better if whatever class maintains the collection
            // of entries that are being imported does not depend
            // on other classes to notify it when it needs to 
            // modify this collection
            // TODO try to find a better solution

            if (entryDownloadAttempts.ContainsKey(hash))
            {
                entryDownloadAttempts[hash] += 1;
            }
            else
            {
                entryDownloadAttempts[hash] = 1;
            }

            entriesImporting.Add(hash);

            AddToFilesDownloading(entry);

            // Important that this is called after adding to entriesImporting
            // Because one may want to check the state of a file
            // in a callback of this event, and the state may be different
            // if something was added to entriesImporting
            if (OnDownloadStarted != null)
            {
                OnDownloadStarted.Invoke(hash, entry);
            }
        }

        // Called when a download of an object fails
        private void OnDownloadFailed(string objectHash, CloudExchangeLink.IndexFile.Entry entry)
        {
            // Remove it from the importing entries collection
            // because it is not longer being downloaded if it failed.
            entriesImporting.Remove(objectHash);

            RemoveFromFilesDownloading(entry);
        }

        // Called when a resource finishes downloading and extracting to the right folder
        private void OnDownloadFinished(string objectHash, CloudExchangeLink.IndexFile.Entry entry)
        {
            // Only remove from files downloading
            // We don't want to remove from entriesImporting
            // Because that should contain entries that are also importing
            // and the importing starts after the download hash finished.
            RemoveFromFilesDownloading(entry);
        }

        // For logging
        void Log(string msg)
        {
            if (OnLog != null)
                OnLog.Invoke(msg);
        }

        // For logging error messages
        void LogError(string msg)
        {
            if (OnLogError != null)
                OnLogError.Invoke(msg);
        }

        // Check if we are linked with the default link
        public bool IsLinkedWithDefaultLink()
        {
            if (HasDefaultLink())
            {
                CloudExchangeLink cloudLink = GetDefaultLink();
                if (cloudLink.GetLinkingState() == CloudExchangeLink.LinkingState.LINKED)
                {
                    return true;
                }
            }
            return false;
        }

        // Add a new link to the collection
        public void AddLink(CloudExchangeLink link, string name)
        {
            // Checking if name already exists
            CloudUtil.Assert(!links.ContainsKey(name), "There already exists a link with this name.");
            link.OnLog = Log;
            link.OnLogError = LogError;

            link.OnDownloadStarted += onDownloadStarted;
            link.OnDownloadFailed += OnDownloadFailed;
            link.OnDownloadFinished += OnDownloadFinished;

            links[name] = link;
        }

        // Adds a link with a name
        // If link with same name exists it is overwritten
        public void SetLink(CloudExchangeLink link, string name)
        {
            links[name] = link;
        }

        // Name for default link
        public const string DefaultLinkName = "defaultlink";

        // Creates a default link
        // Handy if UI does not support multiple links
        public CloudExchangeLink GetOrCreateDefaultLink()
        {
            if (HasDefaultLink())
            {
                return GetDefaultLink();
            }
            else
            {
                return CreateDefaultLink();
            }
        }

        // Creates a default link
        public CloudExchangeLink CreateDefaultLink()
        {
            CloudUtil.Assert(!HasDefaultLink(), "Defaul link already exists.");
            CloudExchangeLink link = new CloudExchangeLink(true);
            AddLink(link, DefaultLinkName);
            return link;
        }

        // Get the default link
        public CloudExchangeLink GetDefaultLink()
        {
            CloudUtil.Assert(HasDefaultLink(), "Cloud manager does not have a default link.");
            return links[DefaultLinkName];
        }

        // Check if it has a default link
        public bool HasDefaultLink()
        {
            return links.ContainsKey(DefaultLinkName);
        }

        // Get a link by it's name
        // Returns true if it is found
        public bool FindLinkByName(string name, out CloudExchangeLink link)
        {
            link = null;
            if (links.ContainsKey(name))
            {
                link = links[name];
                return true;
            }
            return false;
        }

        // Check if it has a link with this name
        public bool HasLinkWithName(string name)
        {
            return links.ContainsKey(name);
        }

        // Deletes a link
        public void RemoveLink(string name)
        {
            if (links.ContainsKey(name))
            {
                links.Remove(name);
            }
        }

        // Serialize the manager and its links to json
        public string SerializeToJSON()
        {
            List<CloudLinkData> data = new List<CloudLinkData>();
            foreach(string name in links.Keys)
            {
                CloudLinkData cld = links[name].GetDataToStore(name);
                data.Add(cld);
            }
            string json = data.ToJson();
            return json;
        }

        // Create a manager with links from json code
        public static CloudLinkManager FromJSON(string json)
        {
            CloudLinkManager manager = new CloudLinkManager();
            manager.ClearLinksAndCreateFromJSON(json);
            return manager;
        }

        // Clear links and create new links from json
        public void ClearLinksAndCreateFromJSON(string json)
        {
            Clear();
            //TODO add some failure handling here
            List<CloudLinkData> data = json.FromJson<List<CloudLinkData>>();

            foreach (CloudLinkData cld in data)
            {
                if (!HasLinkWithName(cld.Name))
                {
                    CloudExchangeLink li = CloudExchangeLink.FromCloudLinkData(cld);
                    AddLink(li, cld.Name);
                }
            }
        }

        // Clears everything in the list
        public void Clear()
        {
            links.Clear();
        }

        // Saves the cloud link manager in persistent storage
        // using the storage provider
        // storageProvider must be set before calling this
        public void SaveToStorage()
        {
            CloudUtil.Assert(storageProvider != null);

            string json = this.SerializeToJSON();
            storageProvider.SetString(storageKey, json);
        }

        // Clear links and loads them from storage
        // Returns true if successfull
        public bool ClearLinksAndLoadFromStorage()
        {
            Clear();

            if (storageProvider.HasKey(storageKey))
            {
                //TODO need better failure handling here because parsing json code
                string json = storageProvider.GetString(storageKey);
                if (json != null)
                {
                    ClearLinksAndCreateFromJSON(json);
                    return true;
                }
                else
                {
                    Log("Could not get cloud manager data from storage.");
                }
            }
            else
            {
                Log("Did not find cloud manager data in storage.");
            }
            return false;
        }
    }
}

