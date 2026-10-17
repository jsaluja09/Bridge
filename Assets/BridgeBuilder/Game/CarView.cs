using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// The car model. Uses the assigned prefab (scaled to the car's room-space length, sitting on its wheels)
    /// or a placeholder box. The simulation drives its pose, including falling.
    /// </summary>
    public sealed class CarView : MonoBehaviour
    {
        [Tooltip("Any free, license-checked car model (e.g. Kenney Car Kit, CC0). Empty = placeholder box.")]
        [SerializeField] GameObject carPrefab;
        [Tooltip("Turn the model if its front isn't along +Z.")]
        [SerializeField] float yawOffset;

        Transform _root;
        Transform _holder;
        float _length = -1f;

        /// <summary>Places the car: position is the ground contact point (room space).</summary>
        public void Show(Vector3 position, Vector3 forward, Vector3 up, float roomLength, bool ghost = false)
        {
            EnsureCar();
            if (!Mathf.Approximately(roomLength, _length))
                Fit(roomLength);
            _root.gameObject.SetActive(true);
            _root.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));
            _root.localScale = ghost ? Vector3.one * 0.999f : Vector3.one;
        }

        public void Hide()
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
