using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ArkioUnityPlugin
{
    public static class SceneUtil
    {
        // Get total number of transforms under a transform recursivly
        // The supplied transform itself it counted as part of the total count
        public static int GetObjectCountRecursive(Transform t)
        {
            int count = 1;
            int childCount = t.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = t.GetChild(i);
                count += GetObjectCountRecursive(child);
            }
            return count;
        }

        // Get all GameObjects in a Unity scene
        public static List<GameObject> GetAllGameObjects(Scene scene)
        {
            GameObject[] gameObjects = scene.GetRootGameObjects();
            List<GameObject> all = new List<GameObject>();
            foreach (var go in gameObjects)
            {
                all.Add(go);
                List<GameObject> children = GetChildrenRecursive(go);
                all.AddRange(children);
            }
            return all;
        }

        /** Get all descendant GameObjects of a GameObject */
        public static List<GameObject> GetChildrenRecursive(GameObject go)
        {
            List<GameObject> children = new List<GameObject>();
            int n = go.transform.childCount;
            for (int i = 0; i < n; i++)
            {
                Transform child = go.transform.GetChild(i);
                children.Add(child.gameObject);
                List<GameObject> grandChildren = GetChildrenRecursive(child.gameObject);
                if (grandChildren.Count > 0)
                {
                    children.AddRange(grandChildren);
                }
            }
            return children;
        }

        // Get all componenets of type T in a scene
        public static List<T> FindComponents<T>(Scene scene)
        {
            List<T> components = new List<T>();
            var gos = GetAllGameObjects(scene);
            foreach (var go in gos)
            {
                T comp = go.GetComponent<T>();
                if (comp != null)
                {
                    components.Add(comp);
                }
            }
            return components;
        }
    }
}
