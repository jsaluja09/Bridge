using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Lets the template scene run with no setup: if neither a ModeMenu nor a BridgeGame is in the scene, this
    /// hides the MR template's demo objects and adds the start menu. Placing either by hand turns this off.
    /// </summary>
    static class BridgeBootstrap
    {
        static readonly string[] TemplateDemoObjects =
        {
            "Coaching UI",
            "Tutorial Player",
            "Hand Menu Setup MR Template Variant",
            "Tooltip Worldspace",
            "Spatial Panel Manipulator",
            "UI",
            "Object Spawner",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (Object.FindAnyObjectByType<BridgeGame>() != null || Object.FindAnyObjectByType<ModeMenu>() != null)
                return;

            foreach (var name in TemplateDemoObjects)
            {
                var go = GameObject.Find(name);
                if (go != null)
                    go.SetActive(false);
            }

            new GameObject("Bridge Builder").AddComponent<ModeMenu>();
        }
    }
}
