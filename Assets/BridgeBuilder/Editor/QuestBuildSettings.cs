using UnityEditor;
using UnityEngine;

namespace BridgeBuilder.EditorTools
{
    /// <summary>
    /// Keeps the Android minimum API level at 32 so builds install on Quest 2 (Android 12L).
    /// The MR template ships with 34, which only newer headsets run.
    /// </summary>
    [InitializeOnLoad]
    static class QuestBuildSettings
    {
        const AndroidSdkVersions QuestMinimum = AndroidSdkVersions.AndroidApiLevel32;

        static QuestBuildSettings()
        {
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            if (PlayerSettings.Android.minSdkVersion <= QuestMinimum)
                return;
            PlayerSettings.Android.minSdkVersion = QuestMinimum;
            AssetDatabase.SaveAssets();
            Debug.Log("BridgeBuilder: set Android minimum API level to 32 so the build runs on Quest 2.");
        }
    }
}
