using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// The canyon diorama the bridge crosses: one static, vertex-colored mesh built from CanyonMesh when the ends are
    /// placed. Snow and the town's lights only redo its colors.
    /// </summary>
    public sealed class CanyonView : MonoBehaviour
    {
        Mesh _mesh;
        GameObject _object;
        CanyonMesh _canyon;
        Rgb[] _rgb;
        Color32[] _colors;
        bool _snowy, _lit;

        /// <summary>The town's windows are lit (it was connected).</summary>
        public bool Lit => _lit;

        /// <summary>Where the town's flag hangs from, once the canyon is built.</summary>
        public Vector3? FlagTop => _canyon != null && _object.activeSelf ? _canyon.FlagTop.ToUnity() : (Vector3?)null;

        void Awake()
        {
            _mesh = new Mesh { name = "Canyon" };
            _object = new GameObject("Canyon");
            _object.transform.SetParent(transform, false);
            _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = _object.AddComponent<MeshRenderer>();
            var shader = Shader.Find("BridgeBuilder/VertexColor");
            r.sharedMaterial = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Lit"));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _object.SetActive(false);
        }

        public void Show(CanyonLayout layout)
        {
            _canyon = CanyonMesh.Build(layout);
            int n = _canyon.Positions.Count;
            var vertices = new Vector3[n];
            var normals = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                vertices[i] = _canyon.Positions[i].ToUnity();
                normals[i] = _canyon.Normals[i].ToUnity();
            }
            _rgb = new Rgb[n];
            _colors = new Color32[n];
            _mesh.Clear();
            _mesh.indexFormat = n > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.vertices = vertices;
            _mesh.normals = normals;
            _mesh.SetTriangles(_canyon.Triangles, 0);
            _mesh.RecalculateBounds();
            _snowy = _lit = false;
            Recolor();
            _object.SetActive(true);
        }

        public void Hide()
        {
            if (_object != null)
                _object.SetActive(false);
        }

        /// <summary>Snow on the clifftops and floor, or not.</summary>
        public void SetSnow(bool snowy)
        {
            if (_canyon == null || snowy == _snowy)
                return;
            _snowy = snowy;
            Recolor();
        }

        /// <summary>The town's windows lit, or dark.</summary>
        public void SetLights(bool lit)
        {
            if (_canyon == null || lit == _lit)
                return;
            _lit = lit;
            Recolor();
        }

        void Recolor()
        {
            _canyon.Colors(_snowy, _lit, _rgb);
            for (int i = 0; i < _rgb.Length; i++)
                _colors[i] = new Color32(_rgb[i].R, _rgb[i].G, _rgb[i].B, 255);
            _mesh.colors32 = _colors;
        }
    }
}
