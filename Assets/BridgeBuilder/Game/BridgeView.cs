using System.Collections.Generic;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Immediate-mode drawing for the bridge: call Begin, add segments, dots and road quads, then End.
    /// Backed by pooled primitives and one dynamic road mesh, so nothing is allocated per frame once warm.
    /// </summary>
    public sealed class BridgeView : MonoBehaviour
    {
        [Tooltip("Any URP Lit material. Created at runtime if empty.")]
        [SerializeField] Material memberMaterial;
        [SerializeField] Material roadMaterial;
        [SerializeField] Color roadColor = new Color(0.25f, 0.25f, 0.28f);

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int LegacyColor = Shader.PropertyToID("_Color");

        readonly Pool _segments = new Pool(PrimitiveType.Cylinder);
        readonly Pool _dots = new Pool(PrimitiveType.Sphere);
        MaterialPropertyBlock _block;

        Mesh _roadMesh;
        readonly List<Vector3> _roadVerts = new List<Vector3>();
        readonly List<int> _roadTris = new List<int>();

        public static Color UtilizationColor(float u)
        {
            u = Mathf.Clamp01(u);
            return u < 0.5f
                ? Color.Lerp(new Color(0.2f, 0.85f, 0.3f), new Color(1f, 0.85f, 0.1f), u * 2f)
                : Color.Lerp(new Color(1f, 0.85f, 0.1f), new Color(0.95f, 0.15f, 0.1f), (u - 0.5f) * 2f);
        }

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (memberMaterial == null)
                memberMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (roadMaterial == null)
            {
                roadMaterial = new Material(memberMaterial);
                roadMaterial.SetColor(BaseColor, roadColor);
            }

            _segments.Init(transform, memberMaterial);
            _dots.Init(transform, memberMaterial);

            _roadMesh = new Mesh { name = "Road" };
            _roadMesh.MarkDynamic();
            var road = new GameObject("Road");
            road.transform.SetParent(transform, false);
            road.AddComponent<MeshFilter>().sharedMesh = _roadMesh;
            road.AddComponent<MeshRenderer>().sharedMaterial = roadMaterial;
        }

        public void Begin()
        {
            _segments.Begin();
            _dots.Begin();
            _roadVerts.Clear();
            _roadTris.Clear();
        }

        public void Segment(Vector3 a, Vector3 b, float radius, Color color)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f)
                return;
            var t = _segments.Next(out var r);
            t.SetPositionAndRotation((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / len));
            t.localScale = new Vector3(radius * 2f, len * 0.5f, radius * 2f);
            Tint(r, color);
        }

        public void Dot(Vector3 p, float radius, Color color)
        {
            var t = _dots.Next(out var r);
            t.SetPositionAndRotation(p, Quaternion.identity);
            t.localScale = Vector3.one * (radius * 2f);
            Tint(r, color);
        }

        /// <summary>A double-sided quad: a0-b0 on the drawn side, a1-b1 on the twin.</summary>
        public void RoadQuad(Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1)
        {
            int i = _roadVerts.Count;
            _roadVerts.Add(a0);
            _roadVerts.Add(b0);
            _roadVerts.Add(b1);
            _roadVerts.Add(a1);
            // Both windings, so the road shows from above and below.
            Tri(i, i + 1, i + 2);
            Tri(i, i + 2, i + 3);
            Tri(i, i + 2, i + 1);
            Tri(i, i + 3, i + 2);
        }

        void Tri(int a, int b, int c)
        {
            _roadTris.Add(a);
            _roadTris.Add(b);
            _roadTris.Add(c);
        }

        public void End()
        {
            _segments.End();
            _dots.End();
            _roadMesh.Clear();
            _roadMesh.SetVertices(_roadVerts);
            _roadMesh.SetTriangles(_roadTris, 0);
            _roadMesh.RecalculateNormals();
            _roadMesh.RecalculateBounds();
        }

        void Tint(Renderer r, Color c)
        {
            _block.SetColor(BaseColor, c);
            _block.SetColor(LegacyColor, c);
            r.SetPropertyBlock(_block);
        }

        sealed class Pool
        {
            readonly PrimitiveType _type;
            readonly List<Transform> _items = new List<Transform>();
            readonly List<Renderer> _renderers = new List<Renderer>();
            Transform _parent;
            Material _material;
            int _used;

            public Pool(PrimitiveType type) => _type = type;

            public void Init(Transform parent, Material material)
            {
                _parent = parent;
                _material = material;
            }

            public void Begin() => _used = 0;

            public Transform Next(out Renderer renderer)
            {
                if (_used == _items.Count)
                {
                    var go = GameObject.CreatePrimitive(_type);
                    Object.Destroy(go.GetComponent<Collider>());
                    go.transform.SetParent(_parent, false);
                    var r = go.GetComponent<Renderer>();
                    r.sharedMaterial = _material;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _items.Add(go.transform);
                    _renderers.Add(r);
                }
                renderer = _renderers[_used];
                var t = _items[_used++];
                if (!t.gameObject.activeSelf)
                    t.gameObject.SetActive(true);
                return t;
            }

            public void End()
            {
                for (int i = _used; i < _items.Count; i++)
                {
                    if (_items[i].gameObject.activeSelf)
                        _items[i].gameObject.SetActive(false);
                }
            }
        }
    }
}
