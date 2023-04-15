using System.IO;
using System;

namespace ArkioImportExportCommon
{
    // Class with some methods related to files and folders
    public static class FileUtil
    {
        // Prepare a folder for resource files such as models or images to be placed in for importing
        // Can be used both for arkio and in the unity plugin
        // path Full path to the folder that contains the folder containing the actual files such as .png or .glb
        // resourceName Name of the folder containing the resource files.
        // Returns true if successful and false when there is a failure
        // destinationFullPath : Full path of the destination folder, that is the folder containing the resource files.
        public static bool PrepareFolderForImportedResource(
            string path, string resourceName, out string destinationFullPath, Action<string> onLogError)
        {
            destinationFullPath = Path.Combine(path, resourceName);
            if (Directory.Exists(destinationFullPath))
            {
                try
                {
                    Directory.Delete(destinationFullPath, true);
                }
                catch (Exception e)
                {
                    onLogError.Invoke(String.Format(
                        "Could not delete already existing model directory {0}. {1}",
                        destinationFullPath, e.Message));
                    return false;
                }
            }
            try
            {
                Directory.CreateDirectory(destinationFullPath);
            }
            catch (Exception e)
            {
                onLogError.Invoke(String.Format(
                    "Could not create directory {0}. {1}",
                    destinationFullPath, e.Message));
                return false;
            }
            return true;
        }

        // Creates folder if it is missing
        // directoryFullPath Full path to the folder
        // errorLog : Optional error logging method for showing errors
        // returns true if no failure happened
        public static bool CreateFolderIfMissing(string directoryFullPath, Action<string> onErrorLog)
        {
            if (!Directory.Exists(directoryFullPath))
            {
                try
                {
                    Directory.CreateDirectory(directoryFullPath);
                }
                catch (Exception e)
                {
                    if (onErrorLog != null)
                    {
                        onErrorLog.Invoke(String.Format("Could not create directory {0}. {1}", directoryFullPath, e.Message));
                    }
                    return false;
                }
            }
            return true;
        }
    }
}

