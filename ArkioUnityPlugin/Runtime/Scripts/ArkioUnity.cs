using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/** Script for showing things in editor
 * for imported objects
 */
namespace ArkioUnityPlugin
{
    [ExecuteInEditMode]
    public class ArkioUnity : MonoBehaviour
    {
        protected bool initialized = false;

        public bool isCollider = false;

        public bool isTrigger = false;

        public bool isHidden = false;

        private void Start()
        {
            bool invisible = isHidden && Application.isPlaying;
            SetVisible(!invisible);
        }

        private void OnApplicationQuit()
        {
            bool invisible = isHidden && Application.isPlaying;
            SetVisible(!invisible);
        }

        private void Update()
        {
            if (!initialized)
            {
                Initialize();
            }
        }

        public void SetVisible(bool visible)
        {
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.enabled = visible;
            }
        }

        public void Initialize()
        {
            if (isCollider)
            {
                MeshCollider collider = gameObject.GetComponent<MeshCollider>();
                if (collider == null)
                {
                    collider = gameObject.AddComponent<MeshCollider>();
                }
                if (isTrigger)
                {
                    if (collider != null)
                    {
                        collider.convex = true;
                        collider.isTrigger = true;
                    }
                }
            }

            if (isHidden)
            {
                if (isCollider)
                {
                    string matName = "ArkioCollider";
                    if (isTrigger)
                    {
                        matName = "ArkioTrigger";
                    }
                    Material mat = Resources.Load<Material>(matName) as Material;
                    if (mat != null)
                    {
                        MeshRenderer mRend = gameObject.GetComponent<MeshRenderer>();
                        if (mRend != null)
                        {
                            mRend.sharedMaterial = mat;
                        }
                    }
                }
            }
            initialized = true;
        }
    }
}

