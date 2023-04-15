using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace ArkioUnityPlugin
{
    public static class ArkioEditorUtil
    {
        // Gets texture from asset database
        public static string RetrieveTexturePath(Texture texture)
        {
            return AssetDatabase.GetAssetPath(texture);
        }
    }
}

