using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Forces Quest passthrough on, so the real room shows behind the game: an AR session, an enabled
    /// ARCameraManager on the main camera, a fully transparent clear color, and the template's virtual
    /// "Environment" room hidden.
    /// </summary>
    static class Passthrough
    {
        public static void Enable()
        {
            if (Object.FindAnyObjectByType<ARSession>() == null)
                new GameObject("AR Session").AddComponent<ARSession>();

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                var cameraManager = cam.GetComponent<ARCameraManager>();
                if (cameraManager == null)
                    cameraManager = cam.gameObject.AddComponent<ARCameraManager>();
                cameraManager.enabled = true;
            }

            var environment = GameObject.Find("Environment");
            if (environment != null)
                environment.SetActive(false);
        }
    }
}
