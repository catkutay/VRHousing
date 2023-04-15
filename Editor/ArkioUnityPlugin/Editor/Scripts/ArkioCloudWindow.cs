using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace ArkioUnityPlugin
{
    // Window allowing user to connect to a group in the cloud
    // and do various operations with the cloud
    public class ArkioCloudWindow : EditorWindow
    {
        // Set to true while in the process of connecting to a group
        private bool isConnecting = false;

        // Set to true while index is being downloaded from cloud
        private bool isDownloadingIndex = false;

        // Invite code put in by user
        private string inviteCode = "";

        // Index of selected model in list to download from cloud
        private int selectedItem = 0;

        // Position of downloadable model list scroll view
        private Vector2 scrollPosition = new Vector2(0, 0);

        // Messages that are displayed in window
        private string indexMessage = null;
        private string entryMessage = null;

        // Text to show for the entries in the download list
        string[] entriesText = null;

        // Mapping from item in UI list to entry hash in index file
        // It is needed because not all the entries in the index file
        // are shown in the UI
        private Dictionary<int, string> entryHashes = null;

        // Index file that was last downloaded from the cloud
        private Arkio.CloudExchangeLink.IndexFile indexFile = null;

        // TODO maybe add refresh button in UI
        // Starts downloading index from cloud
        public void Refresh()
        {
            bool connected = ArkioOperations.InitAndCheckLinkedState();
            if (connected)
            {
                entryMessage = null;
                indexMessage = "Downloading index from cloud...";
                indexFile = null;
                isDownloadingIndex = true;
                _ = ArkioOperations.GetIndexFromCloud(OnIndexFinish);
            }
        }

        // Called we get back the index file request to the cloud
        public void OnIndexFinish(Arkio.CloudExchangeLink.IndexFile indexFile)
        {
            isDownloadingIndex = false;
            indexMessage = null;
            entriesText = null;
            entriesText = null;
            this.indexFile = indexFile;
            entryHashes = new Dictionary<int, string>();

            if (indexFile == null)
            {
                indexMessage = "Unable to download index from cloud.";
            }
            else
            {
                if (indexFile.Entries == null)
                {
                    indexMessage = "Could not get any entries from index.";
                }
                else
                {

                    if (indexFile.Entries.Count > 0)
                    {
                        List<string> text = new List<string>();
                        int uiIndex = 0;
                        foreach (string hash in indexFile.Entries.Keys)
                        {
                            Arkio.CloudExchangeLink.IndexFile.Entry entry = indexFile.Entries[hash];

                            if (ArkioOperations.IsArkioToUnity(entry) && !entry.IsExpired())
                            {
                                string filename = "";
                                if (entry.HasFilename())
                                {
                                    filename = entry.File.Filename;
                                }
                                text.Add(String.Format("{0} - {1}", filename, entry.Created));
                                entryHashes[uiIndex] = hash;
                                uiIndex++;
                            }
                        }
                        if (text.Count > 0)
                        {
                            entriesText = text.ToArray();
                        }
                    }
                }
            }
            if (entriesText == null)
            {
                entryMessage = "No entries available for download";
            }
            else
            {
                entryMessage = "Available downloads";
            }
        }

        // Adds the gui elements to the window
        public void OnGUI()
        {
            if (isConnecting)
            {
                GUILayout.Label("Connecting to cloud group...", EditorStyles.boldLabel);
            }
            else
            {
                // Check if we are connected to the cloud
                bool connected = ArkioOperations.InitAndCheckLinkedState();

                GUILayout.BeginVertical();

                if (connected)
                {
                    inviteCode = "";
                    if (indexFile != null)
                    {
                        string groupName = "";
                        if (indexFile.GroupName != null)
                        {
                            groupName = indexFile.GroupName;
                        }
                        GUILayout.Label("Group name:", EditorStyles.boldLabel);
                        GUILayout.Label(groupName, EditorStyles.boldLabel);
                    }
                }
                else
                {
                    GUILayout.Label("Enter invitation code to join Arkio cloud group", EditorStyles.boldLabel);
                    inviteCode = GUILayout.TextField(inviteCode);
                    inviteCode = inviteCode.Trim().ToUpper();
                }

                // Message regarding the downloading of the index file
                if (indexMessage != null)
                {
                    GUILayout.Label("", EditorStyles.boldLabel);
                    GUILayout.Label(indexMessage, EditorStyles.boldLabel);
                }

                GUILayout.BeginHorizontal();
                bool openArkioCloud = GUILayout.Button("Open Arkio Cloud");

                bool joinGroup = false;
                bool leaveGroup = false;

                if (!connected)
                {
                    EditorGUI.BeginDisabledGroup(inviteCode.Length != 6);
                    joinGroup = GUILayout.Button("Join Group");
                    EditorGUI.EndDisabledGroup();
                }
                else
                {
                    leaveGroup = GUILayout.Button("Leave Group");
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();

                if (openArkioCloud)
                {
                    Application.OpenURL(@"https://cloud.arkio.is");
                }
                if (joinGroup)
                {
                    //TODO maybe show error message in UI when there is a failure to connect
                    // it is already shown in console
                    isConnecting = true;
                    _ = ArkioOperations.LinkToCloud(inviteCode, LinkingFinished);
                }
                if (leaveGroup)
                {
                    ArkioOperations.Unlink();
                }
                if (connected)
                {
                    GUILayout.Label("");
                    bool upload = GUILayout.Button("Upload active scene to Arkio Cloud");
                    GUILayout.Label("");

                    bool import = false;

                    string entryMsg = "";
                    if (entryMessage != null)
                    {
                        entryMsg = entryMessage;
                    }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(entryMsg, EditorStyles.boldLabel);
                    EditorGUI.BeginDisabledGroup(isDownloadingIndex);
                    bool refresh = GUILayout.Button("Refresh");
                    EditorGUI.EndDisabledGroup();
                    GUILayout.EndHorizontal();

                    if (entriesText != null && !isDownloadingIndex)
                    {
                        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, false, false);
                        selectedItem = GUILayout.SelectionGrid(selectedItem, entriesText, 1);
                        EditorGUILayout.EndScrollView();
                        import = GUILayout.Button("Download and import from Arkio Cloud");
                    }

                    if (upload)
                    {
                        _ = ArkioOperations.SyncToCloud();
                    }

                    if (refresh)
                    {
                        Refresh();
                    }

                    if (import)
                    {
                        if (indexFile != null)
                        {
                            if (entryHashes != null)
                            {
                                if (entryHashes.Count > 0)
                                {
                                    string entryHash = entryHashes[selectedItem];
                                    Arkio.CloudExchangeLink.IndexFile.Entry entry = indexFile.Entries[entryHash];
                                    _ = ArkioOperations.UpdateFromCloud(entryHash, entry);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void LinkingFinished(bool success)
        {
            isConnecting = false;
            if (success)
            {
                Refresh();
            }
        }
    }
}