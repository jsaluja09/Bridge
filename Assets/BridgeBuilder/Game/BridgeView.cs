using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Immediate-mode bridge drawing into one dynamic, vertex-colored mesh: one draw call for every member,
    /// joint and fill. Call Begin, add shapes, then End. Lists are reused, so a warm frame allocates nothing.
    /// </summary>
    public sealed class BridgeView : MonoBehaviour
    {
        [Tooltip("Optional. Defaults to the BridgeBuilder/VertexColor shader.")]
        [SerializeField] Material material;
        [Tooltip("The non-color utilization cue: stripes that grow denser with load.")]
        [Range(0f, 1f)] [SerializeField] float stripeAmount = 0.35f;

        const int Sides = 6;
        static readonly int StripeId = Shader.PropertyToID("_StripeAmount");

        Mesh _mesh;
        readonly List<Vector3> _vertices = new List<Vector3>(16384);
        readonly List<Vector3> _normals = new List<Vector3>(16384);
        readonly List<Color32> _colors = new List<Color32>(16384);
        readonly List<Vector2> _uvs = new List<Vector2>(16384);
        readonly List<int> _indices = new List<int>(49152);
        readonly Vector3[] _ring = new Vector3[Sides];

        public static Color UtilizationColor(float u)
        {
            u = Mathf.Clamp01(u);
            return u < 0.5f
                ? Color.Lerp(new Color(0.2f, 0.85f, 0.3f), new Color(1f, 0.85f, 0.1f), u * 2f)
                : Color.Lerp(new Color(1f, 0.85f, 0.1f), new Color(0.95f, 0.15f, 0.1f), (u - 0.5f) * 2f);
        }

        void Awake()
        {
            if (material == null)
            {
                var shader = Shader.Find("BridgeBuilder/VertexColor");
                material = shader != null ? new Material(shader) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }

            _mesh = new Mesh { name = "Bridge", indexFormat = IndexFormat.UInt32 };
            _mesh.MarkDynamic();
            var go = new GameObject("Bridge Mesh");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        public void Begin()
        {
            _vertices.Clear();
            _normals.Clear();
            _colors.Clear();
            _uvs.Clear();
            _indices.Clear();
        }

        /// <summary>A six-sided tube from a to b. utilization > 0 turns on the stripe cue for it.</summary>
        public void Tube(Vector3 a, Vector3 b, float radius, Color color, float utilization = 0f)
        {
            Vector3 axis = b - a;
            float length = axis.magnitude;
            if (length < 1e-5f)
                return;
            axis /= length;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            for (int i = 0; i < Sides; i++)
            {
                float angle = i * Mathf.PI * 2f / Sides;
                _ring[i] = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
            }

            int start = _vertices.Count;
            Color32 c = color;
            float stripe = utilization > 0f ? Mathf.Max(utilization, 0.002f) : 0f;
            for (int i = 0; i < Sides; i++)
            {
                _vertices.Add(a + _ring[i] * radius);
                _vertices.Add(b + _ring[i] * radius);
                _normals.Add(_ring[i]);
                _normals.Add(_ring[i]);
                _colors.Add(c);
                _colors.Add(c);
                _uvs.Add(new Vector2(0f, stripe));
                _uvs.Add(new Vector2(length, stripe));
            }
            for (int i = 0; i < Sides; i++)
            {
                int i0 = start + i * 2, i1 = start + ((i + 1) % Sides) * 2;
                Quad(i0, i0 + 1, i1 + 1, i1);
            }
        }

        /// <summary>A small octahedron, for joints, snap dots and cursors.</summary>
        public void Dot(Vector3 p, float radius, Color color)
        {
            int start = _vertices.Count;
            Color32 c = color;
            Add(p, Vector3.up, radius, c);
            Add(p, Vector3.down, radius, c);
            Add(p, Vector3.left, radius, c);
            Add(p, Vector3.right, radius, c);
            Add(p, Vector3.forward, radius, c);
            Add(p, Vector3.back, radius, c);
            int top = start, bottom = start + 1;
            int[] ring = { start + 2, start + 4, start + 3, start + 5 };
            for (int i = 0; i < 4; i++)
            {
                int x = ring[i], y = ring[(i + 1) % 4];
                Tri(top, x, y);
                Tri(bottom, y, x);
            }
        }

        void Add(Vector3 center, Vector3 direction, float radius, Color32 color)
        {
            _vertices.Add(center + direction * radius);
            _normals.Add(direction);
            _colors.Add(color);
            _uvs.Add(Vector2.zero);
        }

        /// <summary>A flat triangle (fills). The shader draws both sides.</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f)
                return;
            n.Normalize();
            int start = _vertices.Count;
            Color32 col = color;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            for (int i = 0; i < 3; i++)
            {
                _normals.Add(n);
                _colors.Add(col);
                _uvs.Add(Vector2.zero);
            }
            Tri(start, start + 1, start + 2);
        }

        void Quad(int a, int b, int c, int d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        void Tri(int a, int b, int c)
        {
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
        }

        public void End()
        {
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetNormals(_normals);
            _mesh.SetColors(_colors);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_indices, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            material.SetFloat(StripeId, stripeAmount);
        }
    }
}
