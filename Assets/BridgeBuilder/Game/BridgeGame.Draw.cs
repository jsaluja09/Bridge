using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Drawing (spec 2.10): members colored by utilization (worst of tension, compression/buckling and
    /// bending), joints by their worst member, fills by their frame. Bowing shows through the 10 cm nodes.
    /// </summary>
    public sealed partial class BridgeGame
    {
        [Header("Look")]
        [SerializeField] float memberRadius = 0.005f;
        [SerializeField] float jointRadius = 0.007f;
        [SerializeField] float dotRadius = 0.004f;
        [SerializeField] Color memberColor = new Color(0.85f, 0.88f, 0.95f);
        [SerializeField] Color anchorColor = new Color(1f, 0.55f, 0.1f);
        [SerializeField] Color groundColor = new Color(0.7f, 0.45f, 0.15f);
        [SerializeField] Color roadColor = new Color(0.22f, 0.22f, 0.25f);
        [SerializeField] Color fillColor = new Color(0.55f, 0.6f, 0.68f);
        [SerializeField] Color highlightColor = new Color(0.3f, 0.9f, 1f);
        [SerializeField] Color warningColor = new Color(1f, 0.2f, 0.25f);
        [SerializeField] Color detachedColor = new Color(0.85f, 0.2f, 0.85f);

        static Vector3 V(System.Numerics.Vector3 v) => v.ToUnity();

        void Draw()
        {
            _view.Begin();
            switch (_phase)
            {
                case Phase.MarkSurfaces:
                    DrawTaps();
                    break;
                case Phase.Build:
                    DrawSurfaces();
                    DrawDesign();
                    DrawTools();
                    DrawParkedCar();
                    break;
                case Phase.Testing:
                    DrawSurfaces();
                    DrawRun();
                    break;
            }
            _view.End();
            if (_phase != Phase.Build && _phase != Phase.Testing)
                _car.Hide();
        }

        void DrawTaps()
        {
            for (int i = 0; i < _taps.Count; i++)
            {
                _view.Dot(_taps[i], jointRadius, anchorColor);
                if (i % 2 == 1)
                    _view.Tube(_taps[i - 1], _taps[i], memberRadius * 0.6f, anchorColor);
            }
            _view.Dot(_pointer.Tip, dotRadius * 1.5f, highlightColor);
        }

        void DrawSurfaces()
        {
            foreach (var s in _design.Surfaces)
            {
                _view.Tube(V(s.EdgeA), V(s.EdgeB), memberRadius * 0.5f, anchorColor);
                bool start = s.Index == _design.StartSurface;
                foreach (int id in s.Anchors)
                    _view.Dot(V(_design.GetNode(id).Position), dotRadius * (start ? 1.4f : 1.2f), anchorColor);
            }
            if (_phase == Phase.Build)
            {
                foreach (int id in _design.GroundAnchors)
                    _view.Dot(V(_design.GetNode(id).Position), dotRadius, groundColor);
            }
        }

        // ---------- Build ----------

        void DrawDesign()
        {
            var st = _structure;
            bool preview = PreviewCurrent;
            bool warnRoads = Time.time < _roadWarningUntil && _roads != null;

            foreach (var fill in st.Fills)
            {
                Color c = fill.IsRoad ? roadColor : fillColor;
                if (preview)
                    c = Color.Lerp(c, BridgeView.UtilizationColor(WorstEdge(fill, _preview.Utilization)), 0.35f);
                if (warnRoads && fill.IsRoad && (_roads.SteepFills.Contains(fill.DesignId) || !_roads.Ok))
                    c = Color.Lerp(c, warningColor, Pulse(6f));
                if (_hoverFill == fill.DesignId)
                    c = Color.Lerp(c, highlightColor, 0.5f);
                for (int t = 0; t < fill.Tris.Length; t += 3)
                    _view.Triangle(_restRoom[fill.Tris[t]], _restRoom[fill.Tris[t + 1]], _restRoom[fill.Tris[t + 2]], c);
            }

            bool blocked = Time.time < _blockedUntil;
            for (int k = 0; k < st.ElementCount; k++)
            {
                if (st.Kind[k] == ElementKind.Bracing)
                    continue;
                int line = st.ElemLine[k];
                Color c = memberColor;
                float stripe = 0f;
                if (preview)
                {
                    c = _preview.Detached[k] ? detachedColor : BridgeView.UtilizationColor(_preview.Utilization[k]);
                    stripe = _preview.Utilization[k];
                }
                if (line == _hoverLine)
                    c = warningColor;
                else if (blocked && _blocked.Contains(line))
                    c = Color.Lerp(c, warningColor, Pulse(8f));
                _view.Tube(_restRoom[st.ElemA[k]], _restRoom[st.ElemB[k]], memberRadius, c, stripe);
            }

            for (int i = 0; i < st.NodeCount; i++)
            {
                if (st.Fixed[i] || st.Degree(i) < 3)
                    continue;
                Color c = preview ? BridgeView.UtilizationColor(WorstAttached(st, i, _preview.Utilization)) : memberColor;
                _view.Dot(_restRoom[i], jointRadius, c);
            }

            // Every snap point is a node a line can start from (spec 2.3).
            foreach (var node in _design.Nodes)
            {
                if (!node.IsFixed)
                    _view.Dot(V(node.Position), dotRadius, memberColor);
            }
        }

        void DrawTools()
        {
            Vector3 tip = _pointer.Tip;
            _view.Dot(tip, dotRadius, _hoverNode >= 0 ? highlightColor : Color.white);
            if (_hoverNode >= 0)
                _view.Dot(V(_design.GetNode(_hoverNode).Position), jointRadius * 1.6f, highlightColor);

            if (Drawing)
            {
                Vector3 previous = V(_design.GetNode(_drawStart).Position);
                for (int i = 0; i < _stroke.Count; i += 2)
                {
                    _view.Tube(previous, _stroke[i], memberRadius * 0.6f, highlightColor);
                    if (_symmetry)
                        _view.Tube(V(_design.Frame.Mirror(previous.ToNumerics())), V(_design.Frame.Mirror(_stroke[i].ToNumerics())), memberRadius * 0.4f, highlightColor * 0.6f);
                    previous = _stroke[i];
                }
                _view.Tube(previous, tip, memberRadius * 0.6f, highlightColor);
            }
            else if (Time.time < _rejectedUntil)
            {
                for (int i = 1; i < _rejected.Count; i += 2)
                    _view.Tube(_rejected[i - 1], _rejected[i], memberRadius * 0.6f, warningColor);
            }

            if (_fillCorners.Count > 0)
            {
                Color c = new Color(1f, 0.85f, 0.2f);
                for (int i = 0; i < _fillCorners.Count; i++)
                {
                    Vector3 p = V(_design.GetNode(_fillCorners[i]).Position);
                    _view.Dot(p, jointRadius * 1.3f, c);
                    Vector3 next = i + 1 < _fillCorners.Count ? V(_design.GetNode(_fillCorners[i + 1]).Position) : tip;
                    _view.Tube(p, next, memberRadius * 0.4f, c);
                }
            }
        }

        void DrawParkedCar()
        {
            if (_parkedCar == null)
                return;
            var st = _structure;
            Vector3 contact = V(st.ToRoom(_parkedCar.Position - _parkedCar.Up * settings.RideHeight));
            _car.Show(contact, V(_parkedCar.Forward), V(_parkedCar.Up), settings.CarLength / settings.Scale);
        }

        // ---------- Test run ----------

        void DrawRun()
        {
            var snap = _snapshot;
            if (snap == null)
                return;
            var st = snap.Structure;
            var pos = snap.Positions;

            foreach (var fill in st.Fills)
            {
                Color baseColor = Color.Lerp(fill.IsRoad ? roadColor : fillColor, BridgeView.UtilizationColor(WorstEdge(fill, snap.Utilization)), 0.35f);
                for (int t = 0; t < fill.Tris.Length; t += 3)
                {
                    if (TriangleBroken(fill, t, snap))
                        continue;
                    _view.Triangle(V(pos[fill.Tris[t]]), V(pos[fill.Tris[t + 1]]), V(pos[fill.Tris[t + 2]]), baseColor);
                }
            }

            for (int k = 0; k < st.ElementCount; k++)
            {
                var state = snap.State[k];
                if (state == ElementState.Broken || st.Kind[k] == ElementKind.Bracing)
                    continue;
                float u = snap.Utilization[k];
                Color c = state == ElementState.Debris ? memberColor * 0.6f : BridgeView.UtilizationColor(u);
                if (state == ElementState.Frame && u > settings.PulseThreshold)
                    c = Color.Lerp(c, Color.white, Pulse(10f) * 0.6f);
                _view.Tube(V(pos[st.ElemA[k]]), V(pos[st.ElemB[k]]), memberRadius, c, state == ElementState.Frame ? u : 0f);
            }

            for (int i = 0; i < st.NodeCount; i++)
            {
                if (st.Fixed[i] || st.Degree(i) < 3)
                    continue;
                _view.Dot(V(pos[i]), jointRadius, BridgeView.UtilizationColor(WorstAttached(st, i, snap.Utilization)));
            }

            _car.Show(V(snap.CarPosition), V(snap.CarForward), V(snap.CarUp), settings.CarLength / settings.Scale);
        }

        static bool TriangleBroken(FillGeometry fill, int t, Snapshot snap)
        {
            for (int e = 0; e < 3; e++)
            {
                int k = fill.TriEdges[t + e];
                if (k >= 0 && snap.State[k] == ElementState.Broken)
                    return true;
            }
            return false;
        }

        static float WorstEdge(FillGeometry fill, float[] utilization)
        {
            float worst = 0f;
            foreach (int k in fill.TriEdges)
            {
                if (k >= 0)
                    worst = Mathf.Max(worst, utilization[k]);
            }
            return worst;
        }

        static float WorstAttached(BridgeStructure st, int node, float[] utilization)
        {
            float worst = 0f;
            for (int e = st.NodeElementStart[node]; e < st.NodeElementStart[node + 1]; e++)
                worst = Mathf.Max(worst, utilization[st.NodeElements[e]]);
            return worst;
        }

        static float Pulse(float speed) => 0.5f + 0.5f * Mathf.Sin(Time.time * speed);
    }
}
