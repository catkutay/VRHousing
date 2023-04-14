using UnityEditor;

namespace ArkioUnityPlugin
{
    public class ArkioAbout : EditorWindow
    {
        public void OnGUI()
        {
            EditorGUILayout.HelpBox("Version 1.3.0\n www.arkio.is", MessageType.Info);
        }
    }
}

