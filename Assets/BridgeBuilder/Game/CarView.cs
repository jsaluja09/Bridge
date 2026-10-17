using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// The car model. Uses the assigned prefab (scaled to the car's room-space length, sitting on its wheels)
    /// or a placeholder box. The simulation drives its pose, including falling.
    /// W4-08: hook into <see cref="CarVisual"/> for the full primitive Hot-Wheels car with spinning wheels and
    /// suspension-driven pitch/roll.  The original <see cref="Show(Vector3,Vector3,Vector3,float,bool)"/>
    /// signature is kept so the Build-phase parked-car path needs no changes.
    /// </summary>
    public sealed class CarView : MonoBehaviour
    {
        [Tooltip("Any free, license-checked car model (e.g. Kenney Car Kit, CC0). Empty = placeholder box.")]
        [SerializeField] GameObject carPrefab;
        [Tooltip("Turn the model if its front isn't along +Z.")]
        [SerializeField] float yawOffset;

        // W4-08: optional CarVisual for the full Hot-Wheels primitive car.
        [Tooltip("W4-08 CarVisual component (on this GameObject or a child). " +
                 "When set it handles the run-time visual; the placeholder box is used otherwise.")]
        [SerializeField] CarVisual carVisual;

        Transform _root;
        Transform _holder;
        float _length = -1f;

        /// <summary>Places the car: position is the ground contact point (room space).</summary>
        public void Show(Vector3 position, Vector3 forward, Vector3 up, float roomLength, bool ghost = false)
        {
            if (carVisual != null)
            {
                // Static pose (parked car): show with zero speed, no pitch/roll, no dt.
                carVisual.Show(position, forward, up, roomLength, speed: 0f, pitch: 0f, roll: 0f, dt: 0f);
                HidePlaceholder();
                return;
            }
            EnsureCar();
            if (!Mathf.Approximately(roomLength, _length))
                Fit(roomLength);
            _root.gameObject.SetActive(true);
            _root.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));
            _root.localScale = ghost ? Vector3.one * 0.999f : Vector3.one;
        }

        /// <summary>
        /// W4-08: Places the car with full physics state.  Call this during a test run so the wheels spin and the
        /// body pitches and rolls correctly.
        /// </summary>
        /// <param name="position">Ground contact point in room space.</param>
        /// <param name="forward">Car nose direction with pitch baked in (from RunSnapshot.CarForward).</param>
        /// <param name="up">Car up direction with roll baked in (from RunSnapshot.CarUp).</param>
        /// <param name="roomLength">Room-space car length.</param>
        /// <param name="speed">Car speed in bridge-world m/s (used to spin wheels).</param>
        /// <param name="pitch">Suspension pitch in radians.</param>
        /// <param name="roll">Suspension roll in radians.</param>
        /// <param name="dt">Frame delta-time in seconds.</param>
        public void Show(Vector3 position, Vector3 forward, Vector3 up, float roomLength,
                         float speed, float pitch, float roll, float dt)
        {
            if (carVisual != null)
            {
                carVisual.Show(position, forward, up, roomLength, speed, pitch, roll, dt);
                HidePlaceholder();
                return;
            }
            // Fallback: same as static show.
            Show(position, forward, up, roomLength);
        }

        public void Hide()
        {
            if (_root != null)
                _root.gameObject.SetActive(false);
            if (carVisual != null)
                carVisual.Hide();
        }

        void HidePlaceholder()
        {
            if (_root != null)
                _root.gameObject.SetActive(false);
        }

        void EnsureCar()
        {
            if (_root != null)
                return;

            _root = new GameObject("Car").transform;
            _root.SetParent(transform, false);
            _holder = new GameObject("Model").transform;
            _holder.SetParent(_root, false);

            GameObject model;
            if (carPrefab != null)
            {
                model = Instantiate(carPrefab);
                foreach (var c in model.GetComponentsInChildren<Collider>())
                    Destroy(c);
            }
            else
            {
                model = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(model.GetComponent<Collider>());
                model.transform.localScale = new Vector3(0.4f, 0.3f, 1f);
                model.GetComponent<Renderer>().material.color = new Color(0.9f, 0.3f, 0.2f);
            }
            model.transform.SetParent(_holder, false);
            _holder.localRotation = Quaternion.Euler(0f, yawOffset, 0f);
        }

        /// <summary>Scales the model to the given length and seats its bottom center on the root.</summary>
        void Fit(float roomLength)
        {
            _length = roomLength;
            _root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _holder.localScale = Vector3.one;
            _holder.localPosition = Vector3.zero;

            var bounds = WorldBounds(_holder.gameObject);
            float length = Mathf.Max(bounds.size.x, bounds.size.z);
            _holder.localScale = Vector3.one * (length > 1e-5f ? roomLength / length : 1f);
            bounds = WorldBounds(_holder.gameObject);
            _holder.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
