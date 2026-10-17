using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// The car model. Uses the assigned prefab (scaled to carLength, sitting on its wheels) or a
    /// placeholder box. Follows the simulated pose while driving; becomes a falling rigidbody when it drops.
    /// </summary>
    public sealed class CarView : MonoBehaviour
    {
        [Tooltip("Any free car model. Leave empty for a placeholder box.")]
        [SerializeField] GameObject carPrefab;
        [SerializeField] float carLength = 0.14f;
        [Tooltip("Turn the model if its front isn't along +Z.")]
        [SerializeField] float yawOffset;
        [SerializeField] float hideAfterFalling = 3f;

        Transform _root;
        Rigidbody _body;
        Vector3 _lastPos;
        Vector3 _velocity;
        float _fallTime;

        public void Show(Vector3 position, Vector3 forward)
        {
            EnsureCar();
            if (_body != null)
                return;
            _root.gameObject.SetActive(true);
            if (Time.deltaTime > 0f)
                _velocity = (position - _lastPos) / Time.deltaTime;
            _lastPos = position;
            _root.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
        }

        public void Fall()
        {
            if (_root == null || _body != null)
                return;
            _body = _root.gameObject.AddComponent<Rigidbody>();
            _body.mass = 2f;
            _body.linearVelocity = _velocity;
            _body.angularVelocity = new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
            _fallTime = 0f;
        }

        public void Hide()
        {
            if (_root == null)
                return;
            if (_body != null)
            {
                Destroy(_body);
                _body = null;
            }
            _root.gameObject.SetActive(false);
        }

        void Update()
        {
            if (_body == null)
                return;
            _fallTime += Time.deltaTime;
            if (_fallTime > hideAfterFalling)
                _root.gameObject.SetActive(false);
        }

        void EnsureCar()
        {
            if (_root != null)
                return;

            _root = new GameObject("Car").transform;
            _root.SetParent(transform, false);

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
                model.transform.localScale = new Vector3(0.6f, 0.35f, 1f);
                model.GetComponent<Renderer>().material.color = new Color(0.9f, 0.3f, 0.2f);
            }

            // Scale the model to carLength and seat its bottom center on the root.
            var holder = new GameObject("Model").transform;
            holder.SetParent(_root, false);
            model.transform.SetParent(holder, false);
            holder.localRotation = Quaternion.Euler(0f, yawOffset, 0f);

            var bounds = WorldBounds(model);
            float length = Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = length > 1e-5f ? carLength / length : 1f;
            holder.localScale = Vector3.one * scale;
            bounds = WorldBounds(model);
            Vector3 bottom = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            holder.localPosition -= _root.InverseTransformPoint(bottom);

            // A small box collider so the falling car tumbles believably if it lands on anything.
            var box = _root.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(carLength * 0.6f, carLength * 0.35f, carLength);
            box.center = new Vector3(0f, carLength * 0.175f, 0f);
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
