using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Drawing (spec 2.10): members colored by how close they are to failing (the worst of tension, compression,
    /// buckling and bending), joints by their worst member, decks by their frame, and where things broke.
    /// </summary>
    public sealed partial class BridgeGame
    {
        [Header("Look")]
        [SerializeField] float memberRadius = 0.005f;
        [SerializeField] float jointRadius = 0.007f;
        [SerializeField] float dotRadius = 0.0035f;
        [SerializeField] Color memberColor = new Color(0.85f, 0.88f, 0.95f);
        [SerializeField] Color padColor = new Color(0.95f, 0.6f, 0.2f);
        [SerializeField] Color groundColor = new Color(0.7f, 0.45f, 0.15f);
        [SerializeField] Color roadColor = new Color(0.25f, 0.25f, 0.28f);
        [SerializeField] Color highlightColor = new Color(0.3f, 0.9f, 1f);
        [SerializeField] Color warningColor = new Color(1f, 0.2f, 0.25f);
        [SerializeField] Color floatingColor = new Color(0.85f, 0.2f, 0.85f);
        [Tooltip("The shape a stroke will become, and the rail it snaps parallel to.")]
        [SerializeField] Color ghostColor = new Color(0.45f, 1f, 0.55f);
        [Tooltip("Pieces that broke away and are falling.")]
        [SerializeField] Color fallingColor = new Color(0.62f, 0.58f, 0.52f);

        static Vector3 V(Vec3 v) => v.ToUnity();

        void Draw()
        {
            _view.Begin();
            switch (_phase)
            {
                case Phase.Place:
                    foreach (var t in _taps)
                        _view.Dot(t, jointRadius * 1.5f, padColor);
                    _view.Dot(_pointer.Tip, dotRadius * 1.5f, highlightColor);
                    if (_taps.Count == 1)
                        DrawCanyonGhost(_taps[0], _pointer.Tip);
                    break;
                case Phase.Build:
                    DrawPads();
                    DrawDesign();
                    DrawCursor();
                    DrawParkedCar();
                    break;
                case Phase.Testing:
                    DrawPads();
                    DrawRun();
                    break;
                case Phase.Results:
                    DrawPads();
                    DrawRun();
                    DrawStars();
                    break;
            }
            _view.End();
            if (_phase == Phase.Brief || _phase == Phase.Place)
                _car.Hide();
        }

        /// <summary>The ends are the road's ends on each clifftop: road-colored, outlined as supports you can build from.</summary>
        void DrawPads()
        {
            foreach (var pad in _pads)
            {
                var c = pad.Corners();
                Color edge = pad.Id == _start.Start ? padColor : Color.Lerp(padColor, Color.white, 0.25f);
                _view.Triangle(V(c[0]), V(c[1]), V(c[2]), roadColor);
                _view.Triangle(V(c[0]), V(c[2]), V(c[3]), roadColor);
                for (int i = 0; i < 4; i++)
                    _view.Tube(V(c[i]), V(c[(i + 1) % 4]), memberRadius * 0.6f, edge);
            }
            if (_phase == Phase.Build)
                DrawGroundDots();
        }

        [Tooltip("Floor dots within this distance of the controller tip brighten, so a pier is easy to aim.")]
        [SerializeField] float dotRevealMeters = 0.15f;

        /// <summary>Every buildable floor dot faintly; the ones near the controller tip bright and bigger.</summary>
        void DrawGroundDots()
        {
            Vector3 tip = _pointer.Tip;
            Color faint = groundColor * 0.45f;
            foreach (var g in _groundDots)
            {
                Vector3 p = V(g);
                float near = Mathf.Clamp01(1f - Vector3.Distance(p, tip) / dotRevealMeters);
                _view.Dot(p, dotRadius * (0.7f + 0.8f * near), Color.Lerp(faint, groundColor, near));
            }
        }

        // ---------- Placement ----------

        CanyonLayout _ghostCanyon;
        Vector3 _ghostTip;

        /// <summary>
        /// An outline of the canyon the two taps would make (clifftop edges, walls, floor), following the controller
        /// after the first tap. Recomputed only when the tip has moved a centimeter.
        /// </summary>
        void DrawCanyonGhost(Vector3 first, Vector3 tip)
        {
            Vector3 flat = tip - first;
            flat.y = 0f;
            if (flat.magnitude < 0.3f)
                return;                                   // too close to be a bridge (UpdatePlace refuses it too)
            if (_ghostCanyon == null || Vector3.Distance(tip, _ghostTip) > 0.01f)
            {
                double lift = CanyonLayout.LiftNeeded(first.ToCore(), tip.ToCore(), _pointer.FloorY);
                Vec3 up = Vec3.UnitY * lift;
                var (a, b) = Pad.FromTaps(first.ToCore() + up, tip.ToCore() + up);
                _ghostCanyon = new CanyonLayout(a, b);
                _ghostTip = tip;
            }

            var c = _ghostCanyon;
            double w = c.HalfWidth, back = Constants.Canyon.ClifftopDepthMeters, s = c.Span;
            for (int k = -1; k <= 1; k += 2)
            {
                double side = k * w;
                GhostLine(c, -back, side, c.NearTop, 0, side, c.NearTop);      // near clifftop side edge
                GhostLine(c, 0, side, c.NearTop, 0, side, c.FloorY);           // near wall corner
                GhostLine(c, 0, side, c.FloorY, s, side, c.FloorY);            // floor edge
                GhostLine(c, s, side, c.FloorY, s, side, c.FarTop);            // far wall corner
                GhostLine(c, s, side, c.FarTop, s + back, side, c.FarTop);     // far clifftop side edge
            }
            GhostLine(c, 0, -w, c.NearTop, 0, w, c.NearTop);                   // the two cliff edges
            GhostLine(c, s, -w, c.FarTop, s, w, c.FarTop);
        }

        void GhostLine(CanyonLayout c, double a0, double c0, double y0, double a1, double c1, double y1) =>
            _view.Tube(V(c.Point(a0, c0, y0)), V(c.Point(a1, c1, y1)), memberRadius * 0.4f, Color.Lerp(groundColor, Color.white, 0.4f));

        // ---------- Build ----------

        void DrawDesign()
        {
            var input = _bridge.Input;
            bool preview = PreviewCurrent;
            bool warn = Time.time < _roadWarningUntil;

            for (int d = 0; d < _bridge.Decks.Count; d++)
            {
                var deck = _bridge.Decks[d];
                Color c = roadColor;
                if (preview)
                    c = Color.Lerp(c, BridgeView.UtilizationColor((float)WorstEdge(deck, _preview.Utilizations, null)), 0.35f);
                if (warn && !_bridge.Road.Ok)
                    c = Color.Lerp(c, warningColor, Pulse(6f));
                DrawDeck(deck, i => V(input.Nodes[i].Position), c, null);
            }

            bool blocked = Time.time < _blockedUntil;
            for (int k = 0; k < input.Members.Count; k++)
            {
                var m = input.Members[k];
                if (m.HiddenBrace)
                    continue;
                Color c = memberColor;
                float stripe = 0f;
                if (preview)
                {
                    bool floating = _preview.Solve.MemberForces[k] == null;
                    c = floating ? floatingColor : BridgeView.UtilizationColor((float)_preview.Utilizations[k]);
                    stripe = floating ? 0f : (float)_preview.Utilizations[k];
                }
                if (m.LineId == _hoverLine)
                    c = warningColor;
                else if (_drawing && m.LineId == _strokeFx.PartnerLine)
                    c = ghostColor;               // W4-04: the rail the new line is snapping parallel to
                else if (blocked && _blockedMembers.Contains(m.Id))
                    c = Color.Lerp(c, warningColor, Pulse(8f));
                _view.Tube(V(Node(input, m.NodeA)), V(Node(input, m.NodeB)), memberRadius, c, stripe);
            }

            // Supports show as orange dots; every other snap point as a small light dot (spec 2.3).
            foreach (var n in _model.Nodes)
                _view.Dot(V(n.Position), n.Fixed ? dotRadius * 1.6f : dotRadius, n.Fixed ? padColor : memberColor);
            DrawJoints(input, i => V(input.Nodes[i].Position), preview ? _preview.Utilizations : null);
        }

        void DrawCursor()
        {
            Vector3 tip = _pointer.Tip;
            _view.Dot(tip, dotRadius * 1.2f, _hover.HasValue ? highlightColor : Color.white);
            if (_hover.HasValue)
                _view.Dot(_hover.Value, jointRadius * 1.5f, highlightColor);

            if (_drawing && _stroke.Count > 0)
            {
                for (int i = 2; i < _stroke.Count; i += 2)
                    _view.Tube(_stroke[i - 2], _stroke[i], memberRadius * 0.4f, highlightColor);
                _view.Tube(_stroke[_stroke.Count - 1], tip, memberRadius * 0.4f, highlightColor);
            }
            _strokeFx.Draw(_view, memberRadius, ghostColor, warningColor, _drawing, Time.time);   // W4-04
        }

        CarPose _parked;
        Pad _parkedOn;                        // the pad the parked pose was made for

        void DrawParkedCar()
        {
            var pad = _pads[_start.Start];
            if (_parked == null || _parkedOn != pad)
            {
                Vec3 spot = pad.FrontCenter - pad.Forward * (pad.Depth / 2);
                _parked = CarPoseTracker.Parked(Constants.Car.Standard(_level.CarWeightKg), Scale, spot, pad.Forward);
                _parkedOn = pad;
            }
            _car.Show(_parked);
        }

        // ---------- Test run ----------

        void DrawRun()
        {
            var snap = _snapshot;
            if (snap == null)
                return;
            var input = snap.Bridge.Input;
            var pos = snap.Positions;

            // A deck rides its nodes, so on a failed rail it tilts and falls with the debris; torn pieces fall on
            // their own (FailureFx), and fallen deck shrinks away once it has lain on the floor (W4-07).
            for (int d = 0; d < snap.Bridge.Decks.Count; d++)
            {
                var deck = snap.Bridge.Decks[d];
                Color c = Color.Lerp(roadColor, BridgeView.UtilizationColor((float)WorstEdge(deck, snap.Utilization, snap.State)), 0.35f);
                DrawDeck(deck, i => V(pos[i]), c, snap.State, input, _fx.NodeFade);
            }

            var buckling = _worker.LatestBuckling;
            for (int k = 0; k < input.Members.Count; k++)
            {
                var m = input.Members[k];
                var state = snap.State[k];
                if (state == MemberState.Broken || m.HiddenBrace)
                    continue;                 // broken members fall as pieces drawn by FailureFx
                int ia = snap.Bridge.NodeIndex[m.NodeA], ib = snap.Bridge.NodeIndex[m.NodeB];
                Vector3 a = V(pos[ia]), b = V(pos[ib]);
                float u = (float)snap.Utilization[k];
                if (state == MemberState.Falling)
                {
                    float fade = Mathf.Min(_fx.NodeFade(ia), _fx.NodeFade(ib));
                    if (fade > 0f)
                        _view.Tube(a, b, memberRadius * fade, fallingColor);
                }
                else
                {
                    _fx.Member(_view, a, b, memberRadius, BridgeView.UtilizationColor(u), u, k < buckling.Length ? (float)buckling[k] : 0f);
                }
            }
            _fx.DrawPieces(_view, memberRadius);
            DrawJoints(input, i => V(pos[i]), snap.Utilization, _fx.NodeFade);
            if (_phaseEndedAt >= 0f || _phase == Phase.Results)
                DrawWeakMember(snap);
            if (_runningPhase != null && _runningPhase.Kind == PhaseKind.Snow)
                DrawSnow();

            _car.Show(_worker.LatestCars);
        }

        /// <summary>Points at the member that broke first, or came closest to breaking, with a pulsing sleeve.</summary>
        void DrawWeakMember(RunSnapshot snap)
        {
            int k = snap.WeakMember;
            if (k < 0)
                return;
            var m = snap.Bridge.Input.Members[k];
            Vector3 a = V(snap.Positions[snap.Bridge.NodeIndex[m.NodeA]]), b = V(snap.Positions[snap.Bridge.NodeIndex[m.NodeB]]);
            Color c = snap.State[k] == MemberState.Broken ? warningColor : new Color(1f, 0.85f, 0.1f);
            _view.Tube(a, b, memberRadius * (2f + Pulse(6f)), c);
        }

        // ---------- Snow ----------

        const int SnowFlakes = 250;
        Vec3 _snowMin, _snowMax;
        Pad[] _snowFor;

        /// <summary>Flakes falling through a box over both pads (Snowfall: positions from time alone).</summary>
        void DrawSnow()
        {
            if (_snowFor != _pads)
            {
                _snowMin = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
                _snowMax = new Vec3(double.MinValue, double.MinValue, double.MinValue);
                foreach (var pad in _pads)
                {
                    foreach (var c in pad.Corners())
                    {
                        _snowMin = new Vec3(System.Math.Min(_snowMin.X, c.X), System.Math.Min(_snowMin.Y, c.Y), System.Math.Min(_snowMin.Z, c.Z));
                        _snowMax = new Vec3(System.Math.Max(_snowMax.X, c.X), System.Math.Max(_snowMax.Y, c.Y), System.Math.Max(_snowMax.Z, c.Z));
                    }
                }
                _snowMax = new Vec3(_snowMax.X, _snowMax.Y + 0.6, _snowMax.Z);
                _snowFor = _pads;
            }
            for (int i = 0; i < SnowFlakes; i++)
                _view.Dot(V(Snowfall.Flake(i, Time.time, _snowMin, _snowMax)), dotRadius * 0.6f, Color.white);
        }

        // ---------- Stars ----------

        [Header("Stars")]
        [SerializeField] Color starColor = new Color(1f, 0.82f, 0.15f);
        [SerializeField] Color starOutlineColor = new Color(0.45f, 0.45f, 0.5f);
        [SerializeField] float starRadius = 0.045f;

        /// <summary>Three stars above the results: earned ones fill in one after another, the rest stay outlines.</summary>
        void DrawStars()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            Vector3 center = HudAnchor(cam) + Vector3.up * 0.42f;         // just above the HUD text box (0.6 m tall)
            Vector3 toCam = cam.transform.position - center;
            Vector3 normal = Vector3.ProjectOnPlane(toCam, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
            float seconds = Time.time - _resultsShownAt;
            int earned = Stars;
            for (int i = 0; i < TestPhases.MaxStars; i++)
            {
                Vector3 p = center + right * ((i - 1) * starRadius * 2.6f) + Vector3.up * (i == 1 ? starRadius * 0.5f : 0f);
                float fill = (float)StarReveal.Fill(i, earned, seconds);
                Color outline = fill > 0f ? starColor * 0.75f : starOutlineColor;
                _view.Star(p, normal, Vector3.up, starRadius, fill, starColor, outline, memberRadius * 0.5f);
            }
        }

        // ---------- Shared ----------

        Vec3 Node(SolverInput input, int id) => input.Nodes[_bridge.NodeIndex[id]].Position;

        /// <summary>Torn triangles are skipped (they fall as pieces); fade shrinks fallen ones toward their middle.</summary>
        void DrawDeck(DeckGeometry deck, System.Func<int, Vector3> position, Color color, MemberState[] state = null,
                      SolverInput input = null, System.Func<int, float> fade = null)
        {
            for (int t = 0; t < deck.Triangles.Length; t += 3)
            {
                if (state != null && TriangleBroken(deck, t, state, input))
                    continue;
                int i0 = deck.Triangles[t], i1 = deck.Triangles[t + 1], i2 = deck.Triangles[t + 2];
                Vector3 p0 = position(i0), p1 = position(i1), p2 = position(i2);
                float f = fade == null ? 1f : Mathf.Min(fade(i0), Mathf.Min(fade(i1), fade(i2)));
                if (f <= 0f)
                    continue;
                if (f < 1f)
                {
                    Vector3 mid = (p0 + p1 + p2) / 3f;
                    p0 = Vector3.Lerp(mid, p0, f);
                    p1 = Vector3.Lerp(mid, p1, f);
                    p2 = Vector3.Lerp(mid, p2, f);
                }
                _view.Triangle(p0, p1, p2, color);
            }
        }

        void DrawJoints(SolverInput input, System.Func<int, Vector3> position, double[] utilization, System.Func<int, float> fade = null)
        {
            var degree = new int[input.Nodes.Count];
            var index = _bridge != null && _phase == Phase.Build ? _bridge.NodeIndex : _snapshot?.Bridge.NodeIndex;
            if (index == null)
                return;
            var worst = new double[input.Nodes.Count];
            for (int k = 0; k < input.Members.Count; k++)
            {
                var m = input.Members[k];
                if (m.HiddenBrace)
                    continue;
                int a = index[m.NodeA], b = index[m.NodeB];
                degree[a]++;
                degree[b]++;
                if (utilization != null)
                {
                    worst[a] = System.Math.Max(worst[a], utilization[k]);
                    worst[b] = System.Math.Max(worst[b], utilization[k]);
                }
            }
            for (int i = 0; i < degree.Length; i++)
            {
                float f = fade == null ? 1f : fade(i);
                if (degree[i] >= 3 && !input.Nodes[i].Fixed && f > 0f)
                    _view.Dot(position(i), jointRadius * f, utilization != null ? BridgeView.UtilizationColor((float)worst[i]) : memberColor);
            }
        }

        /// <summary>The deck itself tore here (a hidden brace broke). A broken rail doesn't hide the deck.</summary>
        static bool TriangleBroken(DeckGeometry deck, int t, MemberState[] state, SolverInput input)
        {
            for (int e = 0; e < 3; e++)
            {
                int k = deck.TriangleEdges[t + e];
                if (k >= 0 && state[k] == MemberState.Broken && input.Members[k].HiddenBrace)
                    return true;
            }
            return false;
        }

        static double WorstEdge(DeckGeometry deck, double[] utilization, MemberState[] state)
        {
            double worst = 0;
            foreach (int k in deck.TriangleEdges)
            {
                if (k >= 0 && (state == null || state[k] != MemberState.Broken))
                    worst = System.Math.Max(worst, utilization[k]);
            }
            return worst;
        }

        static float Pulse(float speed) => 0.5f + 0.5f * Mathf.Sin(Time.time * speed);
    }
}
