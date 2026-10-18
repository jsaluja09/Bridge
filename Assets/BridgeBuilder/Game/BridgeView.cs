using System.Collections.Generic;
using BridgeBuilder.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Immediate-mode bridge drawing into one dynamic, vertex-colored mesh: one draw call for every member,
    /// joint and fill. Call Begin, add shapes, then End. Lists are reused, so a warm frame allocates nothing.
    /// Members and decks show their material's texture with a color tinted over it; everything else is flat color.
    /// </summary>
    public sealed class BridgeView : MonoBehaviour
    {
        [Tooltip("Optional. Defaults to the BridgeBuilder/VertexColor shader.")]
        [SerializeField] Material material;
        [Tooltip("The non-color utilization cue: stripes that grow denser with load.")]
        [Range(0f, 1f)] [SerializeField] float stripeAmount = 0.35f;

        [Header("Materials (room meters)")]
        [Tooltip("Half the side of a wood beam's square section.")]
        [SerializeField] float woodHalfWidth = 0.005f;
        [Tooltip("Radius of a steel cable.")]
        [SerializeField] float cableRadius = 0.002f;
        [Tooltip("Half the side of a concrete beam's square section.")]
        [SerializeField] float concreteHalfWidth = 0.0085f;
        [Tooltip("How far one texture tile runs along a member (and across a deck).")]
        [SerializeField] float woodTile = 0.06f;
        [SerializeField] float cableTile = 0.01f;
        [SerializeField] float concreteTile = 0.04f;

        const int Sides = 6;
        static readonly int StripeId = Shader.PropertyToID("_StripeAmount");

        // Indexed by (int)BridgeMaterial, which is also the texture slot the shader reads from uv1.z.
        static readonly string[] TextureNames = { "Wood049_1K-JPG_Color", "Rope002_1K-JPG_Color", "Concrete034_1K-JPG_Color" };
        static readonly int[] TextureIds = { Shader.PropertyToID("_WoodTex"), Shader.PropertyToID("_CableTex"), Shader.PropertyToID("_ConcreteTex") };
        static readonly int[] ColorIds = { Shader.PropertyToID("_WoodColor"), Shader.PropertyToID("_CableColor"), Shader.PropertyToID("_ConcreteColor") };
        static readonly Color[] FallbackColors = { new Color(0.62f, 0.45f, 0.28f), new Color(0.35f, 0.37f, 0.40f), new Color(0.70f, 0.70f, 0.68f) };

        /// <summary>uv1 for a vertex drawn in plain vertex color: any other mesh using this shader must set it.</summary>
        internal static readonly Vector4 Untextured = new Vector4(0f, 0f, -1f, 0f);
        static readonly Vector2[] Circle = UnitCircle(Sides);
        static readonly float[] CornerX = { 1f, -1f, -1f, 1f };
        static readonly float[] CornerY = { 1f, 1f, -1f, -1f };

        Mesh _mesh;
        readonly List<Vector3> _vertices = new List<Vector3>(16384);
        readonly List<Vector3> _normals = new List<Vector3>(16384);
        readonly List<Color32> _colors = new List<Color32>(16384);
        readonly List<Vector2> _uvs = new List<Vector2>(16384);
        readonly List<Vector4> _materialUvs = new List<Vector4>(16384);    // (u, v, texture slot or -1, tint)
        readonly List<int> _indices = new List<int>(49152);
        readonly Vector3[] _ring = new Vector3[Sides];
        readonly Vector3[] _corners = new Vector3[4];
        readonly float[] _aspect = { 1f, 1f, 1f };                         // texture height / width: square texels

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
            for (int i = 0; i < TextureNames.Length; i++)
            {
                var texture = Resources.Load<Texture2D>("MaterialTextures/" + TextureNames[i]);
                if (texture != null)
                {
                    material.SetTexture(TextureIds[i], texture);
                    _aspect[i] = texture.height / (float)texture.width;
                }
                material.SetColor(ColorIds[i], texture != null ? Color.white : FallbackColors[i]);
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
            _materialUvs.Clear();
            _indices.Clear();
        }

        /// <summary>Half the thickness of a member of this material, room meters.</summary>
        public float HalfWidth(BridgeMaterial m) => m switch
        {
            BridgeMaterial.Cable => cableRadius,
            BridgeMaterial.Concrete => concreteHalfWidth,
            _ => woodHalfWidth,
        };

        float Tile(BridgeMaterial m) => Mathf.Max(1e-4f, m switch
        {
            BridgeMaterial.Cable => cableTile,
            BridgeMaterial.Concrete => concreteTile,
            _ => woodTile,
        });

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
            float stripe = Stripe(utilization);
            for (int i = 0; i < Sides; i++)
            {
                Vertex(a + _ring[i] * radius, _ring[i], c, new Vector2(0f, stripe), Untextured);
                Vertex(b + _ring[i] * radius, _ring[i], c, new Vector2(length, stripe), Untextured);
            }
            for (int i = 0; i < Sides; i++)
            {
                int i0 = start + i * 2, i1 = start + ((i + 1) % Sides) * 2;
                Quad(i0, i0 + 1, i1 + 1, i1);
            }
        }

        /// <summary>
        /// A member in its material's shape: wood and concrete square beams, steel a thin round cable, textured
        /// along its length. tint (0..1) covers the texture with tintColor. utilization > 0 turns on the stripe cue;
        /// scale shrinks the section; along is how far into the member this piece starts, so bowed pieces line up.
        /// </summary>
        public void Beam(Vector3 a, Vector3 b, BridgeMaterial material, Color tintColor, float tint, float utilization = 0f,
                         float scale = 1f, float along = 0f)
        {
            Vector3 axis = b - a;
            float length = axis.magnitude;
            float half = HalfWidth(material) * scale;
            if (length < 1e-5f || half <= 0f)
                return;
            axis /= length;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(axis, u);

            int slot = (int)material;
            float tileU = Tile(material), tileV = tileU * _aspect[slot];
            float stripe = Stripe(utilization);
            var stripeA = new Vector2(along, stripe);
            var stripeB = new Vector2(along + length, stripe);
            float ua = along / tileU, ub = (along + length) / tileU;
            Color32 c = tintColor;

            if (material == BridgeMaterial.Cable)
            {
                // Whole repeats around, so the texture meets itself at the seam.
                int around = Mathf.Max(1, Mathf.RoundToInt(2f * Mathf.PI * half / tileV));
                int start = _vertices.Count;
                for (int i = 0; i <= Sides; i++)
                {
                    Vector2 p = Circle[i % Sides];
                    Vector3 n = u * p.x + v * p.y;
                    float tv = i * around / (float)Sides;
                    Vertex(a + n * half, n, c, stripeA, new Vector4(ua, tv, slot, tint));
                    Vertex(b + n * half, n, c, stripeB, new Vector4(ub, tv, slot, tint));
                }
                for (int i = 0; i < Sides; i++)
                {
                    int i0 = start + i * 2, i1 = i0 + 2;
                    Quad(i0, i0 + 1, i1 + 1, i1);
                }
                return;
            }

            for (int i = 0; i < 4; i++)
                _corners[i] = u * CornerX[i] + v * CornerY[i];
            float face = 2f * half / tileV;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = _corners[i], q = _corners[(i + 1) % 4];
                Vector3 n = (p + q) * 0.5f;
                float v0 = i * face, v1 = v0 + face;
                int start = _vertices.Count;
                Vertex(a + p * half, n, c, stripeA, new Vector4(ua, v0, slot, tint));
                Vertex(b + p * half, n, c, stripeB, new Vector4(ub, v0, slot, tint));
                Vertex(b + q * half, n, c, stripeB, new Vector4(ub, v1, slot, tint));
                Vertex(a + q * half, n, c, stripeA, new Vector4(ua, v1, slot, tint));
                Quad(start, start + 1, start + 2, start + 3);
            }
            Cap(a, -axis, half, slot, tileU, tileV, c, stripeA, tint);
            Cap(b, axis, half, slot, tileU, tileV, c, stripeB, tint);
        }

        /// <summary>The square end of a beam, using the corners Beam just set.</summary>
        void Cap(Vector3 center, Vector3 normal, float half, int slot, float tileU, float tileV, Color32 c, Vector2 stripe, float tint)
        {
            int start = _vertices.Count;
            for (int i = 0; i < 4; i++)
                Vertex(center + _corners[i] * half, normal, c, stripe, new Vector4(CornerX[i] * half / tileU, CornerY[i] * half / tileV, slot, tint));
            Quad(start, start + 1, start + 2, start + 3);
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
            for (int i = 0; i < 4; i++)
            {
                int x = start + DotRing[i], y = start + DotRing[(i + 1) % 4];
                Tri(top, x, y);
                Tri(bottom, y, x);
            }
        }

        /// <summary>The octahedron's middle ring (left, forward, right, back) as offsets from its first vertex.</summary>
        static readonly int[] DotRing = { 2, 4, 3, 5 };

        /// <summary>A flat star facing `normal`: filled (scale 0..) and/or outlined, centered on p. radius in meters.</summary>
        public void Star(Vector3 p, Vector3 normal, Vector3 up, float radius, float fill, Color fillColor, Color outlineColor, float outlineRadius)
        {
            Vector3 right = Vector3.Cross(up, normal).normalized;
            up = Vector3.Cross(normal, right).normalized;
            var points = StarPoints(radius);
            for (int i = 0; i < 10; i++)
            {
                Vector3 a = p + right * points[i].x + up * points[i].y;
                Vector3 b = p + right * points[(i + 1) % 10].x + up * points[(i + 1) % 10].y;
                Tube(a, b, outlineRadius, outlineColor);
                if (fill > 0f)
                    Triangle(p, Vector3.LerpUnclamped(p, a, fill), Vector3.LerpUnclamped(p, b, fill), fillColor);
            }
        }

        readonly Vector2[] _star = new Vector2[10];
        float _starRadius = -1f;

        Vector2[] StarPoints(float radius)
        {
            if (radius != _starRadius)
            {
                var points = StarShape.Points(radius);
                for (int i = 0; i < 10; i++)
                    _star[i] = new Vector2((float)points[i].x, (float)points[i].y);
                _starRadius = radius;
            }
            return _star;
        }

        void Add(Vector3 center, Vector3 direction, float radius, Color32 color) =>
            Vertex(center + direction * radius, direction, color, Vector2.zero, Untextured);

        /// <summary>A flat triangle (fills). The shader draws both sides.</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f)
                return;
            n.Normalize();
            int start = _vertices.Count;
            Color32 col = color;
            Vertex(a, n, col, Vector2.zero, Untextured);
            Vertex(b, n, col, Vector2.zero, Untextured);
            Vertex(c, n, col, Vector2.zero, Untextured);
            Tri(start, start + 1, start + 2);
        }

        /// <summary>A flat triangle in a material (decks), textured by x and z so neighboring triangles line up.</summary>
        public void MaterialTriangle(Vector3 a, Vector3 b, Vector3 c, BridgeMaterial material, Color tintColor, float tint)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f)
                return;
            n.Normalize();
            int slot = (int)material;
            float tileU = Tile(material), tileV = tileU * _aspect[slot];
            int start = _vertices.Count;
            Color32 col = tintColor;
            Vertex(a, n, col, Vector2.zero, new Vector4(a.x / tileU, a.z / tileV, slot, tint));
            Vertex(b, n, col, Vector2.zero, new Vector4(b.x / tileU, b.z / tileV, slot, tint));
            Vertex(c, n, col, Vector2.zero, new Vector4(c.x / tileU, c.z / tileV, slot, tint));
            Tri(start, start + 1, start + 2);
        }

        void Vertex(Vector3 position, Vector3 normal, Color32 color, Vector2 uv, Vector4 materialUv)
        {
            _vertices.Add(position);
            _normals.Add(normal);
            _colors.Add(color);
            _uvs.Add(uv);
            _materialUvs.Add(materialUv);
        }

        static float Stripe(float utilization) => utilization > 0f ? Mathf.Max(utilization, 0.002f) : 0f;

        static Vector2[] UnitCircle(int sides)
        {
            var points = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return points;
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
            _mesh.SetUVs(1, _materialUvs);
            _mesh.SetTriangles(_indices, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
            material.SetFloat(StripeId, stripeAmount);
        }
    }
}
