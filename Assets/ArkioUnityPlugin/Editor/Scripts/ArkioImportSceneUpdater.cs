using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ArkioUnityPlugin
{
    // Class used to update a unity scene
    // after an arkio model has been imported and placed
    // in it
    public class ArkioImportSceneUpdater
    {
        // Entity id table for knowing entity id for each GameObject
        protected ArkioEntityIDTable entityIdTable;

        // Arkio model prefab instance
        protected GameObject arkioModelInstance;

        // Unity scene to be updated
        protected Scene scene;

        // New transform data for game objects to be updated
        protected Dictionary<GameObject, Transform> newGameObjectTransforms;

        // GameObjects whose transforms have been updated
        protected HashSet<GameObject> alreadUpdated;

        public static bool IsScaleNonUniform(Vector3 scale)
        {
            return (scale.x != scale.y || scale.y != scale.z);
        }

        // Update the transform of a single GameObject
        // if appropriate
        // returns true if some updating was performed
        protected bool UpdateTransformOfSingleGameObject(GameObject go, Transform newTransform)
        {
            bool updated = false;

            // Difference threshold for testing if position, rotation and scale should be updated
            float threshold = 0.01f;

            // Only update if there is a certain difference
            // both for position, rotation and scale to (see later)
            if (Vector3.Distance(go.transform.position, newTransform.position) > threshold)
            {
                go.transform.position = newTransform.position;
                updated = true;
            }
            if (Mathf.Abs(Quaternion.Angle(go.transform.rotation, newTransform.rotation)) > threshold)
            {
                go.transform.rotation = newTransform.rotation;
                updated = true;
            }

            // Only update scale of GameObject if it is a uniform scale
            if (!IsScaleNonUniform(go.transform.localScale))
            {
                // Settin global scale of object by removing parent
                // then setting local scale and then reparenting to the parent
                // in the case it has a parent
                Transform parent = go.transform.parent;
                go.transform.parent = null;
                if (Vector3.Distance(go.transform.localScale, newTransform.lossyScale) > threshold)
                {
                    go.transform.localScale = newTransform.lossyScale;
                    updated = true;
                }
                go.transform.parent = parent;
            }
            return updated;
        }

        // TODO use this method in UpdateScene
        // Updates transform of game object with new transform data from 
        // newGameObjectTransforms if the game object is in newGameObjectTransforms,
        // makes sure to first update any parent
        // or anchestor GameObject transforms first if needed
        protected void UpdateGameObject(GameObject go)
        {
            if (newGameObjectTransforms.ContainsKey(go) &&
                !alreadUpdated.Contains(go))
            {
                Transform parent = go.transform.parent;
                if (parent != null)
                {
                    UpdateGameObject(parent.gameObject);
                }
                Transform t = newGameObjectTransforms[go];

                UpdateTransformOfSingleGameObject(go, t);

                // This GameObject has now been updated
                alreadUpdated.Add(go);
            }
        }

        public ArkioImportSceneUpdater(
            ArkioEntityIDTable entityIdTable,
            GameObject arkioModelInstance, Scene scene)
        {
            this.entityIdTable = entityIdTable;
            this.arkioModelInstance = arkioModelInstance;
            this.scene = scene;
        }

        public void UpdateScene()
        {
            // objects that were in the scene by entity id
            Dictionary<ulong, ArkioEntityIDTable.ArkioObjectInfo> objByEntityId = entityIdTable.GetIdDict();

            // go through all the objects in the newly imported model
            // to see if they have the same entity id as any of the objects
            // that were in the scene before the arkio model was placed in the scene
            int childCount = arkioModelInstance.transform.childCount;

            // Nr of game objects that got updated
            int nrOfUpdated = 0;

            for (int i = 0; i < childCount; i++)
            {
                Transform childTransform = arkioModelInstance.transform.GetChild(i);
                GameObject childGO = childTransform.gameObject;
                ulong id;

                // Try getting the entity id from the node name
                // The ids are stored in the names of the nodes
                bool success = ArkioImportExportCommon.EntityIDUtil.GetEntityIdFromNodeName(childGO.name, out id);
                if (success && objByEntityId.ContainsKey(id))
                {
                    GameObject go = objByEntityId[id].gameObject;
                    if (go != null)
                    {
                        // Check if the GameObject is in arkioModelInstance
                        bool isPartOfImportedModel = false;
                        if (go.transform.parent != null)
                        {
                            if (go.transform.parent.GetInstanceID() == arkioModelInstance.transform.GetInstanceID())
                            {
                                isPartOfImportedModel = true;
                            }
                        }

                        // If its not part of the imported model
                        if (!isPartOfImportedModel)
                        {
                            // It is outside of the glb model
                            // Then we need to disable this child object and update transform of the
                            // object that is outside of the glb model

                            //TODO collect new transforms and then update parent transforms before
                            // updating transform of children
                            bool updated = UpdateTransformOfSingleGameObject(go, childGO.transform);
                            if (updated)
                            {
                                nrOfUpdated++;
                            }

                            // Make sure to hide the child object because we dont want to show it
                            // Only the object that was in the scene should be shown
                            Undo.RecordObject(childGO, "Deactivating object");
                            childGO.SetActive(false);
                            PrefabUtility.RecordPrefabInstancePropertyModifications(childGO);

                            // Setting the changed objects as dirty
                            // It will make the scene dirty so the user can save the changes
                            // We should probably let the user save the scene and
                            // not save it automatically here
                            EditorUtility.SetDirty(go);
                            EditorUtility.SetDirty(childGO);
                        }
                    }
                }
            }
            Debug.Log(string.Format("Updated {0} GameObjects in current scene outside of imported model.", nrOfUpdated));
        }
    }
}

