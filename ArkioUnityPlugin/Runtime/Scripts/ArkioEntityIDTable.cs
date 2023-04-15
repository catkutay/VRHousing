using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArkioUnityPlugin
{
    // A component that can be used for storing info about GameObjects
    // in a scene that are exported to and from Arkio
    public class ArkioEntityIDTable : MonoBehaviour
    {
        /** Currently placed arkio models in the scene. */
        public GameObject[] ArkioModelsInScene;

        // Removes null references from ArkioModelsInScene
        public void CleanModelsArray()
        {
            if (ArkioModelsInScene == null) return;

            // Check if any is null before allocating memory
            // Also count number of objects that are not null
            bool doClean = false;
            int validCount = 0;
            foreach(var model in ArkioModelsInScene)
            {
                if (model != null)
                {
                    validCount++;
                }
                else
                {
                    doClean = true;
                }
            }
            if (doClean)
            {
                GameObject[] validObjects = new GameObject[validCount];
                int i = 0;
                foreach (var model in ArkioModelsInScene)
                {
                    if (model != null)
                    {
                        validObjects[i] = model;
                        i++;
                    }
                }
                ArkioModelsInScene = validObjects;
            }
        }

        // Origin of an entity
        public enum EntityOrigin
        {
            // Originating from Arkio model
            Arkio,

            // Originating from Unity scene
            Unity,

            // Object of unknown origin
            Unknown
        }

        // Check if GameObject is inside any of the arkio models
        // in the scene
        public bool IsInsideArkioModelInstance(GameObject go)
        {
            if (ArkioModelsInScene != null)
            {
                Transform parentOfObject = go.transform.parent;
                if (parentOfObject != null)
                {
                    // Check if go is inside an arkio model prefab instance
                    foreach (GameObject arkioModelInstance in ArkioModelsInScene)
                    {
                        // Check if the GameObject go is inside arkioModelInstance
                        if (arkioModelInstance.transform.GetInstanceID() == parentOfObject.GetInstanceID())
                        {
                            // This GameObject is inside an arkio model instance
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        // Get origin of GameObject,
        // that is, if it was born in Arkio
        // or Unity.
        public EntityOrigin GetOrigin(GameObject go)
        {
            if (IsInsideArkioModelInstance(go))
            {
                // This GameObject is inside an arkio model instance
                if (go.name.StartsWith("Arkio"))
                {
                    return EntityOrigin.Arkio;
                }
                else if (go.name.StartsWith("Unity"))
                {
                    return EntityOrigin.Unity;
                }
                else
                {
                    return EntityOrigin.Unknown;
                }
            }
            return EntityOrigin.Unity;
        }

        /** Information about a GameObject that is exported
         * to Arkio */
        [System.Serializable]
        public struct ArkioObjectInfo
        {
            // GameObject in scene representing the object
            public GameObject gameObject;

            // Id that is generated for the object
            public ulong id;

            // Origin of the object, i.e. if it is from Arkio or Unity
            public EntityOrigin origin;
        }

        [SerializeField]
        public ArkioObjectInfo[] gameObjectInfo;

        // Set values of gameObjectInfo from a dictionary
        // mapping from GameObject to ArkioObjectInfo
        // The dictionary should contain the new values
        public void SetData(Dictionary<GameObject, ArkioObjectInfo> data)
        {
            gameObjectInfo = new ArkioObjectInfo[data.Count];
            int i = 0;
            foreach(GameObject key in data.Keys)
            {
                gameObjectInfo[i] = data[key];
                i++;
            }
        }

        // Set values of gameObjectInfo from a dictionary
        // mapping from id to GameObject
        // The dictionary should contain the new values
        public void SetData(Dictionary<ulong, ArkioObjectInfo> data)
        {
            gameObjectInfo = new ArkioObjectInfo[data.Count];
            int i = 0;
            foreach(ulong id in data.Keys)
            {
                gameObjectInfo[i] = data[id];
                i++;
            }
        }

        // Get a dictionary with mapping from entity id to ArkioObjectInfo
        // Creates it from the data in gameObjectInfo
        public Dictionary<ulong, ArkioObjectInfo> GetIdDict()
        {
            Dictionary<ulong, ArkioObjectInfo> objByEntityId = new Dictionary<ulong, ArkioObjectInfo>();
            if (gameObjectInfo != null)
            {
                foreach (ArkioEntityIDTable.ArkioObjectInfo info in gameObjectInfo)
                {
                    objByEntityId[info.id] = info;
                }
            }
            return objByEntityId;
        }

        // Get a dictionary with mapping from GameObject to entity
        // Creates it from the data in gameObjectInfo
        public Dictionary<GameObject, ArkioObjectInfo> GetGameObjectDict()
        {
            Dictionary<GameObject, ArkioObjectInfo> idByObj = new Dictionary<GameObject, ArkioObjectInfo>();
            if (gameObjectInfo != null)
            {
                foreach (ArkioEntityIDTable.ArkioObjectInfo info in gameObjectInfo)
                {
                    idByObj[info.gameObject] = info;
                }
            }
            return idByObj;
        }

        // Goes through all GameObjects in a scene and adds their ids to the
        // entity id table if they are missing there
        // If there are GameObjects in the table that are not in the scene
        // they are removed from the table
        // Returns dictionary with the new data
        public Dictionary<GameObject, ArkioObjectInfo> UpdateFromScene(Scene scene)
        {
            List<GameObject> gos = SceneUtil.GetAllGameObjects(scene);
            HashSet<GameObject> goHS = new HashSet<GameObject>();
            foreach(GameObject go in gos)
            {
                goHS.Add(go);
            }
            Dictionary<GameObject, ArkioObjectInfo> ids = GetGameObjectDict();

            System.Random rand = ArkioIEUtil.GetRandom();

            // Add missing GameObject and create new ids for them
            foreach(GameObject go in gos)
            {
                if (!ids.ContainsKey(go))
                {
                    ArkioObjectInfo aoi = new ArkioObjectInfo();
                    aoi.gameObject = go;
                    aoi.origin = GetOrigin(go);
                    bool gotId = false;
                    
                    if (aoi.origin != EntityOrigin.Unity)
                    {
                        // If its from arkio then we should use the entity id from arkio
                        ulong id;
                        gotId = ArkioImportExportCommon.EntityIDUtil.GetEntityIdFromNodeName(aoi.gameObject.name, out id);
                        if (gotId)
                        {
                            aoi.id = id;
                        }
                    }
                    if(!gotId)
                    {
                        // If its not from arkio or it was not possible to get the id and was not in the entity table
                        // then we need to generate an id for it
                        aoi.id = ArkioIEUtil.RandomEntityId(rand);
                    }
                    ids[go] = aoi;
                }
            }

            // Remove GameObjects from id dictionary
            // if they are not found in the scene
            GameObject[] gosInIds = new GameObject[ids.Keys.Count];
            ids.Keys.CopyTo(gosInIds, 0);
            foreach(GameObject go in gosInIds)
            {
                if (!goHS.Contains(go))
                {
                    ids.Remove(go);
                }
            }

            // Update the persistent data
            SetData(ids);

            return ids;
        }
    }
}

