using System.Collections.Generic;
using System.IO;
using Amazon.S3.Model;
using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text;
using TinyJson;
using AIECommon = ArkioImportExportCommon;
using SysDia = System.Diagnostics;

// TODO maybe change namespace to Arkio.Cloud
namespace Arkio
{
    // Class representing a single link between
    // arkio and some other software through
    // the cloud.
    public class CloudExchangeLink
    {
        // Default number of parts for uploading a file to the cloud in multiple parts
        public const int DefaultNrOfParts = 10;

        // Expiration time for entries in the cloud system
        public readonly TimeSpan cloudEntryExpirationTime = new TimeSpan(7, 0, 0, 0);

        // Class representing an index file
        // in the cloud. It's a file containing
        // a list of files that have been uploaded
        // to the cloud.
        public class IndexFile
        {
            // Name of the cloud sharing group
            public string GroupName = "";

            // Single entry in the list
            // representing a file that has been
            // uploaded
            public class Entry
            {
                // Class representing a field in an entry that
                // contains data only valid for file entries.
                public class FileInfo
                {
                    // File type info to be used
                    public enum FileType
                    {
                        ArkioImport, ArkioExport, ArkioPhotoExport
                    }

                    // Containing info related to model
                    // export from arkio
                    public class ArkioExportInfo
                    {
                        public string MainFile = null; // main model: file name including file extension 
                    }

                    // Containing info related to resource
                    // import into arkio
                    public class ArkioImportInfo
                    {

                    }

                    // Containing info related to photo
                    // export from Arkio.
                    public class ArkioPhotoExportInfo
                    {

                    }

                    // Number of parts that the file is split into
                    // We may want to split files into parts to
                    // improve the speed of uploads and downloads
                    public int NrOfParts = 1;

                    // Total number of bytes in the file
                    public long TotalNrOfBytes;

                    // Name of file/model/scene
                    // It should be the name of the main file.
                    // E.g. in case of .gltf it should be the .gltf file
                    // not any .bin or texture files that are included with it.
                    // For an .obj, it should be the .obj file, not the .mtl
                    // file or any texture file.
                    public string Filename;

                    // Icon to show in UI for resource.
                    // Currently we use this to know
                    // what Arkio import folder to put files into
                    public string Icon;

                    // Icon string for imports from Unity
                    public static string IconUnity
                    { get { return "Unity"; } }

                    // Icon string for regular Model imports
                    public static string IconModels
                    { get { return "Models"; } }

                    // Icon string for image imports
                    public static string IconImages
                    { get { return "Images"; } }

                    // Icon string for Revit imports
                    public static string IconRevit
                    { get { return "Revit"; } }

                    // Contains info related to model export from Arkio.
                    public ArkioExportInfo ArkioExport = null;

                    // Contains info related to resource import into Arkio.
                    public ArkioImportInfo ArkioImport = null;

                    // Contains info related to photo export from Arkio.
                    public ArkioPhotoExportInfo ArkioPhotoExport = null;
                }

                // Time the entry was created
                public DateTime Created;

                // Time when entry expires
                public DateTime Expires;

                // Checks if the entry is expired
                public bool IsExpired()
                {
                    return DateTime.UtcNow > Expires;
                }

                // Date related to files that are sent
                // to the cloud
                // Only used for file entries
                public FileInfo File;

                public int GetNrOfParts()
                {
                    if (File != null)
                    {
                        // nr of parts can never be less than 1
                        if (File.NrOfParts > 0)
                        {
                            return File.NrOfParts;
                        }
                    }
                    return 1;
                }

                // Check if the entry has the filename field
                public bool HasFilename()
                {
                    if (File != null)
                    {
                        if (File.Filename != null)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }

            // The entries in the index file
            // Keys in dictionary are the hashes for the entries
            public Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();

            // Get entry by it's hash
            public Entry FindEntry(string hash)
            {
                if (Entries.ContainsKey(hash))
                {
                    return Entries[hash];
                }
                return null;
            }

            // Remove all entries
            public void Clear()
            {
                Entries.Clear();
            }

            // Remove expired entries from the index file
            public void RemoveExpiredEntries()
            {
                string[] keys = new string[Entries.Keys.Count];
                Entries.Keys.CopyTo(keys, 0);
                foreach(string key in keys)
                {
                    if (Entries[key].IsExpired())
                    {
                        Entries.Remove(key);
                    }
                }
            }

            // Add entries to index file
            public void Add(Dictionary<string, Entry> entries)
            {
                foreach(string hash in entries.Keys)
                {
                    Entries[hash] = entries[hash];
                }
            }

            // Deserialize from json string read from byte array
            public void DeserializeFromJson(byte[] buffer)
            {
                string json = Encoding.UTF8.GetString(buffer, 0, buffer.Length);
                DeserializeFromJson(json);
            }

            // Deserialize from json string
            public void DeserializeFromJson(string json)
            {
                IndexFile indf = JSONParser.FromJson<IndexFile>(json);
                this.Entries = indf.Entries;
                this.GroupName = indf.GroupName;
            }

            // Serialize index file to json string
            public string SerializeToJson()
            {
                string json = JSONWriter.ToJson(this);
                return json;
            }

            // Serialize index file to json string and return it  s byte array
            public byte[] SerializeToJsonBytes()
            {
                string json = SerializeToJson();
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                return bytes;
            }

            public override string ToString()
            {
                string s = string.Empty;
                s += string.Format("{0} entries :\n", Entries.Count);
                foreach (string hash in Entries.Keys)
                {
                    //TODO add more detailed info here if needed
                    s += string.Format("- created={0} nrOfParts={1} hash={2}\n",
                        Entries[hash].Created, Entries[hash].GetNrOfParts(), hash);
                }

                return s;
            }
        }

        // Class holding data needed for tracking the progress of a
        // multiple part download/upload
        public class MultiPartProgressTracker
        {
            // Minimum time in milliseconds between calls to OnProgress
            public long minInterval = 200;

            // Called on transfer progress
            // int bytesRead, int totalBytes
            public Action<int, int> OnProgress;

            private SysDia.Stopwatch stopwatch = new SysDia.Stopwatch();

            // Nr of bytes already transfered for each part
            private Dictionary<string, int> bytesTransfered = new Dictionary<string, int>();

            // Total nr of bytes of all the combined parts
            private int totalBytes;

            public MultiPartProgressTracker(int totalBytes)
            {
                this.totalBytes = totalBytes;
            }

            public void NotifyProgress(string hash, string userData, int position, int size)
            {
                NotifyProgress(position, size, hash);
            }

            // Notify the tracker of prgress being made for a single part
            // thisPartBytesRead Nr of bytes already transferred for this part
            // thisPartTotalBytes Total nr of bytes in this part
            // objectHash hash of this part
            // This should be called when progress event for download ar upload happens
            public void NotifyProgress(int thisPartBytesRead, int thisPartTotalBytes, string objectHash)
            {
                bytesTransfered[objectHash] = thisPartBytesRead;

                stopwatch.Stop();
                long elapsedMs = stopwatch.ElapsedMilliseconds;
                if (elapsedMs >= minInterval)
                {
                    stopwatch.Reset();

                    if (OnProgress != null && objectHash != null)
                    {
                        // Get sum of bytes read for all parts
                        int totalBytesRead = 0;
                        foreach (string key in bytesTransfered.Keys)
                        {
                            totalBytesRead += bytesTransfered[key];
                        }
                        OnProgress.Invoke(totalBytesRead, this.totalBytes);
                    }
                }
                stopwatch.Start();
            }
        }

        // Linking state of a link
        public enum LinkingState
        {
            NOT_LINKED,
            LINKING,
            LINKED
        }

        public static readonly SHA1Managed SHA1 = new SHA1Managed();

        const string AwsExchangeKeyBucketName = "exchangekey.arkio.is";
        const string AwsExchangeIndexBucketName = "exchangeindex.arkio.is";
        const string AwsExchangeFileBucketName = "exchangefile.arkio.is";

        // Linking state of the link
        private LinkingState linkingState = LinkingState.NOT_LINKED;

        private byte[] currentLinkedEncryptionKey = null;

        // sets currentLinkedEncryptionKey to encryptionKey
        // and puts the link into the LINKED state
        // Use this when creating a link with a known encryption
        // key that was e.g. read from disk
        public void ActivateWithEncryptionKey(byte[] encryptionKey)
        {
            CloudUtil.Assert(encryptionKey != null);
            CloudUtil.Assert(encryptionKey.Length > 0); //TODO maybe also check if length is right
            currentLinkedEncryptionKey = encryptionKey;
            linkingState = LinkingState.LINKED;
        }

        // Called when a download starts for a file in the cloud
        public Action<string, IndexFile.Entry> OnDownloadStarted;

        // Called when there is a failure to download a file from the cloud
        public Action<string, IndexFile.Entry> OnDownloadFailed;

        // Called when a resource has finished downloading and has
        // been extracted to the right folder locally
        public Action<string, IndexFile.Entry> OnDownloadFinished;

        // Invoke OnDownloadFailed if not null
        private void DownloadFailed(string objectHash, IndexFile.Entry entry)
        {
            if (OnDownloadFailed != null)
                OnDownloadFailed.Invoke(objectHash, entry);
        }

        // For writing out log messages
        public Action<string> OnLog;

        // Like OnLog, but for error messages
        public Action<string> OnLogError;

        AwsClient awsClient;
        Random random;

        // If true, data being sent and downloaded
        // from cloud is encrypted and decrypted
        bool encrypt = false;

        // Constructor, passes AWS exceptions through
        public CloudExchangeLink(bool encrypt = false)
        {
            this.encrypt = encrypt;

            awsClient = new AwsClient();
            awsClient.OnLog += Log;
            awsClient.OnLogError += LogError;

            random = new System.Random();
        }

        // Get data needed for storing 
        // the link
        // name Name of the link
        public CloudLinkData GetDataToStore(string name)
        {
            CloudLinkData cld = new CloudLinkData();
            cld.LinkKey = Convert.ToBase64String(currentLinkedEncryptionKey);
            cld.Name = name;
            return cld;
        }

        // Create a CloudExchangeLink from a CloudLinkData object
        public static CloudExchangeLink FromCloudLinkData(CloudLinkData cld)
        {
            CloudExchangeLink cel = new CloudExchangeLink(true);
            try
            {
                byte[] encrKey = Convert.FromBase64String(cld.LinkKey);
                // TODO maybe check if length of key is correct
                cel.ActivateWithEncryptionKey(encrKey);
            }
            catch
            {
                cel.LogError("Could not get encryption key. Setting it to null.");
                cel.currentLinkedEncryptionKey = null;
            }
            return cel;
        }

        void LogFormat(string format, params object[] paramList)
        {
            Log(string.Format(format, paramList));
        }

        void Log(string msg)
        {
            if (OnLog != null)
                OnLog.Invoke(msg);
        }

        void LogError(string msg)
        {
            if (OnLogError != null)
                OnLogError.Invoke(msg);
        }

        public LinkingState GetLinkingState()
        {
            return linkingState;
        }

        // Generates a new short linking code using the existing encryption key
        // Must be already linked to do this
        // Use this for inviting a new device to the share space that this device is already
        // connected to.
        public async Task<(bool success, string code)> GenerateInviteCode()
        {
            CloudUtil.Assert(
            linkingState == LinkingState.LINKED, "Must be linked in order to create an invite code.");
            string code = GenerateRandomCode();
            string objectName = string.Format("KEB/{0}", code);
            Log(string.Format("Generated random code, attempting to post. code={0} objectName={1} key={2}",
                code, objectName, Convert.ToBase64String(currentLinkedEncryptionKey)));

            PutObjectResponse r1 = await awsClient.PutObjectAsync(AwsExchangeKeyBucketName, objectName, "", currentLinkedEncryptionKey);

            if (r1 != null && r1.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                Log(string.Format("Successfully generated new code. code={0} objectName={1} key={2}",
                    code, objectName, Convert.ToBase64String(currentLinkedEncryptionKey)));

                return (true, code);
            }
            else
            {
                LogError("Unable to post object.");
            }

            return (false, null);
        }

        // Generates new code and links
        public async Task<(bool success, string code)> GenerateCodeAndLink()
        {
            CloudUtil.Assert(
            linkingState == LinkingState.NOT_LINKED, "Can't link when already linked");
            string code = GenerateRandomCode();

            string objectName = string.Format("KEB/{0}", code);

            linkingState = LinkingState.LINKING;

            byte[] encryptionKey = GenerateNewAESEncryptionKey();

            Log(encryptionKey.Length.ToString());
            Log(string.Format("Generated random key, attempting to post. code={0} objectName={1} key={2}", code, objectName, Convert.ToBase64String(encryptionKey)));

            PutObjectResponse r1 = await awsClient.PutObjectAsync(AwsExchangeKeyBucketName, objectName, "", encryptionKey);

            if (r1 != null && r1.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                currentLinkedEncryptionKey = encryptionKey;

                linkingState = LinkingState.LINKED;

                Log(string.Format("Successfully Linked. code={0} objectName={1}", code, objectName, Convert.ToBase64String(encryptionKey)));

                return (true, code);
            }
            else
            {
                LogError("Unable to post object, linking failed.");
                linkingState = LinkingState.NOT_LINKED;
            }

            return (false, null);
        }

        // Links using existing code
        public async Task LinkWithExistingCode(string code, Action<bool> onComplete)
        {
            // Convert to uppercase as we always want
            // the code to be in uppercase
            code = code.ToUpper();

            CloudUtil.Assert(
            linkingState == LinkingState.NOT_LINKED, "Can't link when already linked");

            string objectName = string.Format("KEB/{0}", code);

            linkingState = LinkingState.LINKING;

            var res = await awsClient.GetObjectAsync(AwsExchangeKeyBucketName, objectName);

            if (res.response != null && res.response.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {
                    byte[] buffer = await AwsClient.ReadStream(res.response.ResponseStream);
                    currentLinkedEncryptionKey = buffer;
                    Log(string.Format("Found object. code={0} objectName={1} key={2}", code, objectName, Convert.ToBase64String(currentLinkedEncryptionKey)));
                    linkingState = LinkingState.LINKED;
                }
                catch(Exception e)
                {
                    linkingState = LinkingState.NOT_LINKED;
                    LogError(String.Format("Could not read encryption key from cloud. {0}", e.Message));
                }
            }
            else
            {
                Log(string.Format("Unable to find object. code={0} objectName={1}", code, objectName));

                linkingState = LinkingState.NOT_LINKED;
            }

            if (onComplete != null)
                onComplete.Invoke(linkingState == LinkingState.LINKED);
        }

        // Get group name from cloud for this link
        public async Task<string> GetGroupNameFromCloud()
        {
            IndexFile ind = await DownloadIndex();
            if (ind != null)
            {
                return ind.GroupName;
            }
            return null;
        }

        // Downloads an index file from the cloud
        // The index file has a list of objects that are available
        public async Task<IndexFile> DownloadIndex()
        {
            CloudUtil.Assert(linkingState == LinkingState.LINKED,
                "Must be linked to do this operation.");

            string indexObjectName = string.Format("IB/{0}", Convert.ToBase64String(currentLinkedEncryptionKey));

            Log(string.Format("Getting current index for {0}", indexObjectName));

            var res = await awsClient.GetObjectAsync(AwsExchangeIndexBucketName, indexObjectName);

            if (res.response != null && res.response.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {
                    byte[] buffer = await AwsClient.ReadStream(res.response.ResponseStream);
                    Log(string.Format("Index exists ! objectName={0}", indexObjectName));
                    IndexFile indexFile = new IndexFile();
                    indexFile.DeserializeFromJson(buffer);
                    return indexFile;
                }
                catch(Exception e)
                {
                    LogError(String.Format("Could not read or deserialize index file. {0}", e.Message));
                    return null;
                }
            }
            return null;
        }

        // Add entries to index file locally and on cloud
        // Returns true if successfull, else false
        async Task<bool> AddToIndex(Dictionary<string, IndexFile.Entry> entries)
        {
            CloudUtil.Assert(linkingState == LinkingState.LINKED,
                "Must be linked to do this operation.");

            // first get index
            // update
            // post

            IndexFile indexFile = null;

            string indexObjectName = string.Format("IB/{0}", Convert.ToBase64String(currentLinkedEncryptionKey));

            Log(string.Format("Getting current index for {0}", indexObjectName));

            var res = await awsClient.GetObjectAsync(AwsExchangeIndexBucketName, indexObjectName);

            if (res.response != null && res.response.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                try
                {
                    byte[] buffer = await AwsClient.ReadStream(res.response.ResponseStream);
                    Log(string.Format("Index exists ! objectName={0}", indexObjectName));
                    indexFile = new IndexFile();
                    indexFile.DeserializeFromJson(buffer);
                    Log(indexFile.ToString());
                }
                catch
                {
                    LogError("Failed to read or deserialize index file from cloud.");
                    return false;
                }
            }
            else
            {
                if (res.response == null && res.httpStatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Index file was not found in the cloud. Therefore we create a brand new index file.
                    indexFile = new IndexFile();
                }
                else
                {
                    // If we got here then some other error happened such as a network error.
                    LogError("Failed to connect to cloud when trying to get index file.");
                    return false;
                }
            }

            // Removing expired entries if there are any,
            // as we don't want them anymore in the cloud.
            indexFile.RemoveExpiredEntries();

            indexFile.Add(entries);

            byte[] data = indexFile.SerializeToJsonBytes();

            PutObjectResponse r1 = await awsClient.PutObjectAsync(AwsExchangeIndexBucketName, indexObjectName, "", data);

            if (r1 != null && r1.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                Log(string.Format("Successfully updated index objectName={0} index={1}", indexObjectName, indexFile.ToString()));
                return true;
            }
            else
            {
                LogError("Failed to update index file in cloud!");
                return false;
            }
        }

        // Delegate for method that gets directory to place a resource into
        public delegate string CloudResourceFolderProvidingMethod(string icon);

        public async Task<(bool success, string destinationFolderFullPath)> DownloadCloudResource
            (string hash, IndexFile.Entry entry, string directory)
        {
            return await DownloadCloudResource(hash, entry, directory, null);
        }

        public async Task<(bool success, string destinationFolderFullPath)> DownloadCloudResource
            (string hash, IndexFile.Entry entry, CloudResourceFolderProvidingMethod getDirectoryMethod)
        {
            return await DownloadCloudResource(hash, entry, null, getDirectoryMethod);
        }

        // Extract contents of zip file to directory
        private bool ExtractToDirectory(string zipFile, string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                using (StreamReader sr = new StreamReader(zipFile))
                {
                    System.IO.Compression.ZipArchive arch = new System.IO.Compression.ZipArchive(sr.BaseStream);
                    foreach (System.IO.Compression.ZipArchiveEntry zipEntry in arch.Entries)
                    {
                        string fullName = zipEntry.FullName;
                        if (fullName.Equals("/"))// Skip entry with this name as that would not be a valid file
                        {
                            continue;
                        }
                        using (Stream s = zipEntry.Open())
                        {
                            int dataLength = (int)zipEntry.Length;
                            byte[] data = new byte[dataLength];
                            s.Read(data, 0, dataLength);
                            string filename = Path.Combine(directory, fullName);
                            File.WriteAllBytes(filename, data);
                        }
                    }
                }
                return true;
            }
            catch(Exception e)
            {
                Log(String.Format("Failure during extraction of {0}. {1}", zipFile, e.Message));
                return false;
            }
        }

        // Downloads resource from cloud
        // Downloads zip file from cloud, extracts it and places the files in it in the right directory.
        //
        // hash : Hash / Id of resource from the cloud
        // entry : The entry for the resource in the cloud in the index file
        // directory : Local directory to place the downloaded files in, if this is null then the importDirectoryGetter
        // method will be used to get the directory
        // The getDirectory method is a method that takes in a CloudExchangeCreator and bool called isModel
        // that tells if the resouce is a model or image and decides frome these paramters where to place the files
        // locally. It may be needed in cases where it can't be decided where to put the files until we have extracted
        // some info from the zip file from the cloud.
        // If directory is null then getDirectory will be used to get the destination directory.
        //
        // Return value:
        // success : True if the whole operation of downloading, extracting and placing the files
        // in the destination directory was successfull, else false
        // destinationFolderFullPath : Full path of the folder where the files were placed.
        private async Task<(bool success, string destinationFolderFullPath)> DownloadCloudResource
            (string hash, IndexFile.Entry entry, string directory, CloudResourceFolderProvidingMethod getDirectoryMethod)
        {
            string baseDir = directory;
            byte[] data = await GetObjectInMultipleParts(hash, entry);

            if (data == null)
            {
                LogError(String.Format("Could not get object {0} from cloud.", hash));
            }
            else
            {
                try
                {
                    Log(String.Format("Download of object {0} complete.", hash));

                    if (baseDir == null)
                    {
                        CloudUtil.Assert(getDirectoryMethod != null,
                            "If a directory is not supplied there must be a method supplied for getting a directory, in DownloadCloudResource!");
                        string icon = "";
                        if (entry.File != null && entry.File.Icon != null)
                        {
                            icon = entry.File.Icon;
                        }
                        baseDir = getDirectoryMethod(icon);
                    }

                    // Convert a json string to a CloudExchangeJob object
                    // Example of such a string:
                    // {"JobType":"ArkioUnityExport","Filename":"2022-02-02_15-51_0","LastWriteTimeUtc":"2022-02-02T15:57:40.1024207Z"}
                    //TODO find out what happens if json is corrupted
                    // Need to have some failure handling for that

                    bool success = AIECommon.FileUtil.CreateFolderIfMissing(baseDir, LogError);
                    if (!success)
                    {
                        DownloadFailed(hash, entry);
                        return (false, null);
                    }

                    if (!entry.HasFilename())
                    {
                        LogError(String.Format("Entry {0}, missing filename in DownloadCloudResource!", hash));
                        DownloadFailed(hash, entry);
                        return (false, null);
                    }

                    // Prepare folder of the model
                    string basename = Path.GetFileNameWithoutExtension(entry.File.Filename);
                    string destFolder;

                    success = AIECommon.FileUtil.PrepareFolderForImportedResource(
                        baseDir, basename, out destFolder, LogError);
                    if (!success)
                    {
                        DownloadFailed(hash, entry);
                        return (false, null);
                    }

                    string tempFileName = Path.Combine(baseDir, Guid.NewGuid().ToString());
                    File.WriteAllBytes(tempFileName, data);
                    Log(String.Format("Downloaded file to {0}", tempFileName));

                    try
                    {
                        System.IO.Compression.ZipFile.ExtractToDirectory(tempFileName, destFolder);
                    }
                    catch
                    {
                        // If extraction fails try extracting using other method.
                        // System.IO.Compression.ZipFile.ExtractToDirectory can fail
                        // if extracting would have resulted in a file outside
                        // the destination folder.
                        bool extractSuccess = ExtractToDirectory(tempFileName, destFolder);
                        if (!extractSuccess)
                        {
                            DownloadFailed(hash, entry);
                            return (false, null);
                        }
                    }
                    Log(String.Format("Extracted archive contents to {0}", destFolder));

                    // Need to set last write time of the downloaded files so we can know
                    // if a resource in the cloud is newer. This is needed i.e. the cloud
                    // list to know what the state of the cloud items should be.
                    // Setting it to the creation date of the cloud entry as it will
                    // be compared to that.
                    string[] files = Directory.GetFiles(destFolder, "*", SearchOption.AllDirectories);
                    foreach (string file in files)
                    {
                        File.SetLastWriteTimeUtc(file, entry.Created);
                    }

                    // REMARK: Before there used to be code here that modified the name of the .glb from the cloud
                    // to be the same as the name writen in the entry in the cloud, in case it was not the same
                    // name. I do not see why this would be needed so I removed this code. However, I would
                    // suggest keeping an eye on this.

                    // Delete the zip archive
                    File.Delete(tempFileName);
                    Log(String.Format("Deleted temporary file {0}", tempFileName));

                    if(OnDownloadFinished != null)
                    {
                        OnDownloadFinished.Invoke(hash, entry);
                    }

                    return (true, destFolder);
                }
                catch (Exception e)
                {
                    DownloadFailed(hash, entry);
                    LogError(String.Format("Failure when trying to download or uncompress model file. {0}", e.Message));
                    return (false, null);
                }
            }

            DownloadFailed(hash, entry);
            return (false, null);
        }

        public async Task<byte[]> GetObjectRaw(string hash, Action<int, int, string> readProgress = null)
        {
            var res = await awsClient.GetObjectAsync(AwsExchangeFileBucketName, hash);
            if (res.response != null && res.response.HttpStatusCode == System.Net.HttpStatusCode.OK)
            {
                byte[] buffer;
                try
                {
                    buffer = await AwsClient.ReadStream(res.response.ResponseStream, readProgress, hash);
                }
                catch(Exception e)
                {
                    LogError(String.Format("Failed to read stream from cloud. {0}", e.Message));
                    return null;
                }
                res.response.Dispose();
                return buffer;
            }
        
            return null;
        }

        // Downloads object in multiple parts
        // Returns joined bytes of all the parts
        public async Task<byte[]> GetMultiPartObjectBytes(string hash, 
            IndexFile.Entry entry, Action<int, int, string> readProgress = null)
        {
            List<Task<byte[]>> tasks = new List<Task<byte[]>>();
            int nrOfParts = entry.GetNrOfParts();
            for (int i = 0; i < nrOfParts; i++)
            {
                string partName = string.Format("{0}.{1}", hash, i);
                tasks.Add(GetObjectRaw(partName, readProgress));
            }

            byte[][] rawBytes = await Task.WhenAll(tasks);

            // For testing failure to download a part
            //rawBytes[2] = null;

            // Check if any of the byte arrays is null
            // If it is then there was a failure to download it
            // Then everything should fail
            foreach(byte[] bytes in rawBytes)
            {
                if (bytes == null)
                {
                    LogError(string.Format("Could not download one or more parts for file {0}", hash));
                    return null;
                }
            }


            if (rawBytes != null)
            {
                byte[] joinedBytes = JoinBytes(rawBytes);
                return joinedBytes;
            }

            return null;
        }

        public void OnReadProgress(int bytesRead, int totalBytes)
        {
            double percentage = 100 * ((double)bytesRead) / ((double)totalBytes);
            Log(String.Format("Download progressing {0:0.0}%", percentage));
        }

        public void OnUploadProgress(int bytesTransfered, int totalBytes)
        {
            double percentage = 100 * ((double)bytesTransfered) / ((double)totalBytes);
            Log(String.Format("Upload progressing {0:0.0}%", percentage));
        }

        // Join multiple arrays of bytes into one array of bytes
        // Returns null if any of the array is null
        public static byte[] JoinBytes(byte[][] byteArrays)
        {
            // Count total number of bytes
            int n = byteArrays.Length;
            int totalBytes = 0;
            for (int i = 0; i < n; i++)
            {
                if (byteArrays[i] == null)
                {
                    return null;
                }
                totalBytes += byteArrays[i].Length;
            }

            // Get the bytes
            byte[] output = new byte[totalBytes];
            int outputIndex = 0;
            for (int i = 0; i < n; i++)
            {
                int m = byteArrays[i].Length;
                for (int j = 0; j < m; j++)
                {
                    output[outputIndex] = byteArrays[i][j];
                    outputIndex++;
                }
            }

            return output;
        }

        // Similar to GetObject but downloads file in multiple parts in parallel
        public async Task<byte[]> GetObjectInMultipleParts(string hash, IndexFile.Entry entry)
        {
            // Must be linked to do this operation
            CloudUtil.Assert(
            linkingState == LinkingState.LINKED);

            CloudUtil.Assert(entry.File != null, "Entry missing File field in GetObjectInMultipleParts!");

            LogFormat("Getting object {0}", hash);

            if (OnDownloadStarted != null)
            {
                OnDownloadStarted.Invoke(hash, entry);
            }

            MultiPartProgressTracker tracker = new MultiPartProgressTracker((int)entry.File.TotalNrOfBytes);
            tracker.OnProgress += OnReadProgress;
            byte[] bytes = await GetMultiPartObjectBytes(hash, entry, tracker.NotifyProgress);
            tracker.OnProgress -= OnReadProgress;

            if (bytes != null)
            {
                byte[] clearBytes;

                if (!encrypt)
                {
                    clearBytes = bytes;
                }
                else
                {
                    using (Aes aes = CreateAesObject(currentLinkedEncryptionKey, hash))
                    {
                        LogFormat("Key={0} IV={1}", Convert.ToBase64String(aes.Key), Convert.ToBase64String(aes.IV));

                        using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                        {
                            clearBytes = Arkio.CryptographyUtils.PerformCryptography(bytes, decryptor);
                        }
                    }
                }

                return clearBytes;
            }

            DownloadFailed(hash, entry);

            return null;
        }

        // Upload resource to cloud using default nr of parts
        public async Task<bool> UploadResourceToCloud(
            string resourceFolder, string filename,
            CloudExchangeLink.IndexFile.Entry.FileInfo.FileType fileType, string icon)
        {
            return await UploadResourceToCloud(resourceFolder, filename, fileType, icon, DefaultNrOfParts);
        }

        // Uploads a resource to the cloud
        // Creates a zip archive containing everything in the resource folder and uploads it to the cloud
        // finally deletes the zip file.
        // resourceFolder : A folder containing the resource files and other related files
        // filename : Name of the main file in the resource
        // fileType : Type of upload
        // icon: Icon field in cloud entry
        // nrOfParts : Number of parts for multipart upload
        public async Task<bool> UploadResourceToCloud(
            string resourceFolder, string filename,
            CloudExchangeLink.IndexFile.Entry.FileInfo.FileType fileType,
            string icon,
            int nrOfParts)
        {
            string zipFile = resourceFolder + ".zip";
            if (File.Exists(zipFile))
            {
                // Delete zip file if it already exists
                try
                {
                    File.Delete(zipFile);
                }
                catch (Exception e)
                {
                    LogError(String.Format("Could not delete zip file {0}. {1}", zipFile, e.Message));
                    return false;
                }
            }

            // zip the file
            try
            {
                //Debug.Log(String.Format("Attempting to zip {0}", zipFile));
                System.IO.Compression.ZipFile.CreateFromDirectory(resourceFolder, zipFile);
            }
            catch (Exception e)
            {
                LogError(String.Format("Could not create zip file {0}. {1}", zipFile, e.Message));
                return false;
            }

            byte[] buffer;
            //Debug.Log(String.Format("Reading file {0} into buffer.", zipFile));

            DateTime lastWriteTimeUTC = new DateTime();
            try
            {
                lastWriteTimeUTC = File.GetLastWriteTimeUtc(zipFile);
            }
            catch (Exception e)
            {
                LogError(String.Format("Failed to get last write time of {0}. {1}", zipFile, e.Message));
                return false;
            }
            try
            {
                buffer = File.ReadAllBytes(zipFile);
            }
            catch (Exception e)
            {
                LogError(String.Format("Failed to read file {0} into buffer {1}", zipFile, e.Message));
                return false;
            }

            // create a hash from the buffer
            string hash = CreateHash(buffer);
            //Debug.Log("hash: " + hash);

            //Debug.Log("info: " + info);

            try
            {
                Log("Uploading file.");
                bool success = await PutObject(hash, buffer, nrOfParts, filename, lastWriteTimeUTC, fileType, icon);

                if (!success)
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                LogError(String.Format("Could not upload file. {0}", e.Message));
                return false;
            }

            try
            {
                File.Delete(zipFile);
            }
            catch(Exception e)
            {
                LogError(String.Format("Failed to delete file {0}. {1}", zipFile, e.Message));
            }

            return true;
        }

        // Send file to cloud
        // objectName : Name/hash of object in cloud
        // bytes : Content of file
        // nrOfParts : Number of parts to upload, if more than one
        // then it will be uploaded and stored in multiple parts in the cloud
        // filename : Name of file/scene
        // created : date time of export/entry creation
        // arkioExport : True if this is an arkio export
        // fileType : Cloud file entry type
        // icon : Icon field in cloud entry
        // onProgress : Progress callback method, for uploading progress
        // Returns true if successfull, else false
        public async Task<bool> PutObject(
            string objectName, byte[] bytes, int nrOfParts, string filename, DateTime created,
            CloudExchangeLink.IndexFile.Entry.FileInfo.FileType fileType,
            string icon,
            AwsClient.FileUploadProgress onProgress = null)
        {
            // Must be linked to do this operation
            CloudUtil.Assert(
            linkingState == LinkingState.LINKED);

            byte[] dataToSend = null;

            if (!encrypt)
            {
                dataToSend = bytes;
            }
            else
            {
                using (Aes aes = CreateAesObject(currentLinkedEncryptionKey, objectName))
                {
                    LogFormat("KEY={0} IV={1}", Convert.ToBase64String(aes.Key), Convert.ToBase64String(aes.IV));

                    using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                    {
                        dataToSend = CryptographyUtils.PerformCryptography(bytes, encryptor);
                    }
                }
            }

            //LogFormat("Putting object info={0} objectName={1} size={2} key={3} ", info, objectName, dataToSend.Length, Convert.ToBase64String(currentLinkedEncryptionKey));
            List<Task<PutObjectResponse>> putObjTasks = new List<Task<PutObjectResponse>>();
            int partSize = CloudUtil.GetBytePartSize(dataToSend.Length, nrOfParts); // TODO experiment a bit with different part counts

            MultiPartProgressTracker tracker = new MultiPartProgressTracker(dataToSend.Length);
            tracker.OnProgress += OnUploadProgress;
            for (int i = 0; i < nrOfParts; i++)
            {
                byte[] partBytes = CloudUtil.GetSubByteArray(dataToSend, i * partSize, partSize);
                string partObjectName = string.Format("{0}.{1}", objectName, i);

                putObjTasks.Add(awsClient.PutObjectAsync(
                    AwsExchangeFileBucketName, partObjectName, "", partBytes,
                    (name, userData, position, size) => {
                        onProgress?.Invoke(name, userData, position, size);
                        tracker.NotifyProgress(name, userData, position, size);
                    }));
            }

            PutObjectResponse[] r0 = await Task.WhenAll(putObjTasks);

            //For testing
            //r0[2] = null;

            tracker.OnProgress -= OnUploadProgress;

            // TODO Maybe later we can add retry for
            // failed tasks if only some fail
            if (r0 != null)
            {
                // Checking if all the responses ar OK
                // If any of them fail then the whole multi
                // part upload should fail
                bool failure = false;
                foreach (PutObjectResponse res in r0)
                {
                    if (res == null)
                    {
                        failure = true;
                        break;
                    }
                    if (res.HttpStatusCode != System.Net.HttpStatusCode.OK)
                    {
                        failure = true;
                        break;
                    }
                }
                if (failure)
                {
                    LogError(String.Format("Failed to upload one or more parts of file {0}.", objectName));
                    return false;
                }

                Dictionary<string, IndexFile.Entry> entries = new Dictionary<string, IndexFile.Entry>();
                entries[objectName] = new IndexFile.Entry()
                {
                    Created = created,
                    Expires = created + cloudEntryExpirationTime,
                    File = new IndexFile.Entry.FileInfo
                    {
                        Filename = filename,
                        NrOfParts = nrOfParts,
                        TotalNrOfBytes = dataToSend.Length,
                        Icon = icon
                    }
                };
                if (fileType == IndexFile.Entry.FileInfo.FileType.ArkioExport)
                {
                    entries[objectName].File.ArkioExport = new IndexFile.Entry.FileInfo.ArkioExportInfo();
                }
                else if (fileType == IndexFile.Entry.FileInfo.FileType.ArkioImport)
                {
                    entries[objectName].File.ArkioImport = new IndexFile.Entry.FileInfo.ArkioImportInfo();
                }
                else if (fileType == IndexFile.Entry.FileInfo.FileType.ArkioPhotoExport)
                {
                    entries[objectName].File.ArkioPhotoExport = new IndexFile.Entry.FileInfo.ArkioPhotoExportInfo();
                }
                

                bool success = await AddToIndex(entries);
                if (!success)
                {
                    return false;
                }
            }

            return true;
        }

        public static Aes CreateAesObject(Byte[] key, string hash)
        {
            Aes aes = AesManaged.Create();

            byte[] iv = CalculateAesIV(hash);

            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;

            return aes;
        }

        public static byte[] CalculateAesIV(string hash)
        {
            byte[] iv = new byte[16];
            Array.Copy(Convert.FromBase64String(hash), iv, 16);
            return iv;
        }
        string GenerateRandomCode()
        {
            // numbers 0-9 and english uppercase alphabet
            // without the letter O, because it looks similar to 0
            string letters = "0123456789ABCDEFGHIJKLMNPQRSTUVWXYZ";
            int n = 6;
            int maxIndex = letters.Length - 1;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                sb.Append(letters[random.Next(0, maxIndex)]);
            }
            return sb.ToString();
        }

        static byte[] GenerateNewAESEncryptionKey()
        {
            using (AesManaged aes = new AesManaged())
            {
                aes.GenerateKey();

                return aes.Key;
            }
        }

        public static string CreateHash(byte[] buffer)
        {
            using (MemoryStream s = new MemoryStream(buffer))
            {
                s.Position = 0;
                return BitConverter.ToString(SHA1.ComputeHash(s)).Replace("-", string.Empty);
            }
        }
    }
}

