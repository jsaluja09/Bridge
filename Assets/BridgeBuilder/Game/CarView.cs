using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// The game's handle on the car. Drawing is CarVisual's job (W4-08); the game is built at runtime, so this
    /// adds the CarVisual itself and passes an optional model through to its slot.
    /// </summary>
    public sealed class CarView : MonoBehaviour
    {
        [Tooltip("Any free, license-checked car model (e.g. Kenney Car Kit, CC0). Empty = the primitive toy car.")]
        [SerializeField] GameObject carPrefab;

        CarVisual _visual;

        void Awake()
        {
            _visual = GetComponentInChildren<CarVisual>();
            if (_visual == null)
                _visual = gameObject.AddComponent<CarVisual>();
            if (carPrefab != null && _visual.sourcedModelPrefab == null)
                _visual.sourcedModelPrefab = carPrefab;
        }

        public void Show(CarPose pose) => _visual.Show(pose);

        public void Hide() => _visual.Hide();
    }
}
