using System.Collections.Generic;
using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// The game's handle on the cars. Drawing is CarVisual's job (W4-08); this keeps a pool of them, one per car
    /// slot, built the first time a slot is used and reused after that, each in its own body color.
    /// </summary>
    public sealed class CarView : MonoBehaviour
    {
        [Tooltip("Any free, license-checked car model (e.g. Kenney Car Kit, CC0). Empty = the primitive toy car.")]
        [SerializeField] GameObject carPrefab;

        static readonly Color[] BodyColors =
        {
            new Color(1f, 0.25f, 0.08f),     // the original orange-red first
            new Color(0.15f, 0.45f, 0.95f),
            new Color(0.98f, 0.8f, 0.1f),
            new Color(0.2f, 0.75f, 0.35f),
            new Color(0.9f, 0.9f, 0.92f),
            new Color(0.55f, 0.25f, 0.8f),
        };

        readonly List<CarVisual> _pool = new List<CarVisual>();

        /// <summary>One car (the parked car while building).</summary>
        public void Show(CarPose pose)
        {
            Get(0).Show(pose);
            HideFrom(1);
        }

        /// <summary>Every visible car slot of a run.</summary>
        public void Show(CarPoses cars)
        {
            for (int s = 0; s < cars.Count; s++)
            {
                if (cars.Visible[s])
                    Get(s).Show(cars.Poses[s]);
                else if (s < _pool.Count)
                    _pool[s].Hide();
            }
            HideFrom(cars.Count);
        }

        public void Hide() => HideFrom(0);

        void HideFrom(int first)
        {
            for (int i = first; i < _pool.Count; i++)
                _pool[i].Hide();
        }

        CarVisual Get(int slot)
        {
            while (_pool.Count <= slot)
            {
                var go = new GameObject("Car " + _pool.Count);
                go.transform.SetParent(transform, false);
                var visual = go.AddComponent<CarVisual>();
                visual.sourcedModelPrefab = carPrefab;
                visual.Tint(BodyColors[_pool.Count % BodyColors.Length]);
                _pool.Add(visual);
            }
            return _pool[slot];
        }
    }
}
