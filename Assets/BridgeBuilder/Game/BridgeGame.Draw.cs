using BridgeBuilder.Core;
using UnityEngine;

namespace BridgeBuilder.Game
{
    /// <summary>
    /// Drawing (spec 2.10): members and decks in their material, tinted by how close they are to failing (the
    /// worst of tension, compression, buckling and bending), joints by their worst member, and where things broke.
    /// </summary>
    public sealed partial class BridgeGame
    {
        [Header("Look")]
        [Tooltip("Plain tubes: pad edges, the stroke trail, star outlines. Member sizes are on BridgeView.")]
        [SerializeField] float memberRadius = 0.005f;
        [SerializeField] float jointRadius = 0.007f;
        [SerializeField] float dotRadius = 0.0035f;
        [Tooltip("Snap dots, and joints before there are loads to show.")]
        [SerializeField] Color memberColor = new Color(0.85f, 0.88f, 0.95f);
        [SerializeField] Color padColor = new Color(0.95f, 0.6f, 0.2f);
        [SerializeField] Color groundColor = new Color(0.7f, 0.45f, 0.15f);
        [Tooltip("How much the load color covers a member's material at no load; loadTintRise is added at full load.")]
        [Range(0f, 1f)] [SerializeField] float loadTint = 0.35f;
        [Range(0f, 1f)] [SerializeField] float loadTintRise = 0.45f;
        [Tooltip("How much a highlight (delete, blocked, partner rail, unsupported, falling, ghost) covers the material.")]
        [Range(0f, 1f)] [SerializeField] float highlightTint = 0.75f;
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

        void DrawPads()
        {
            foreach (var pad in _pads)
            {
                var c = pad.Corners();
                Color color = pad.Id == _start.Start ? padColor : Color.Lerp(padColor, Color.white, 0.25f);
                _view.Triangle(V(c[0]), V(c[1]), V(c[2]), color * 0.8f);
                _view.Triangle(V(c[0]), V(c[2]), V(c[3]), color * 0.8f);
                for (int i = 0; i < 4; i++)
                    _view.Tube(V(c[i]), V(c[(i + 1) % 4]), memberRadius * 0.6f, color);
            }
            if (_phase == Phase.Build)
            {
                foreach (var g in _groundDots)
                    _view.Dot(V(g), dotRadius, groundColor);
            }
        }

        // ---------- Build ----------

        void DrawDesign()
        {
            var input = _bridge.Input;
            bool preview = PreviewCurrent;
            bool warn = Time.time < _roadWarningUntil && !_bridge.Road.Ok;

            for (int d = 0; d < _bridge.Decks.Count; d++)
            {
                var deck = _bridge.Decks[d];
                Color c = Color.white;
                float tint = 0f;
                if (preview)
                {
                    c = BridgeView.UtilizationColor((float)WorstEdge(deck, _preview.Utilizations, null));
                    tint = loadTint;
                }
                if (warn)
                    Blend(ref c, ref tint, warningColor, highlightTint, Pulse(6f));
                DrawDeck(deck, i => V(input.Nodes[i].Position), c, tint, null);
            }

            bool blocked = Time.time < _blockedUntil;
            for (int k = 0; k < input.Members.Count; k++)
            {
                var m = input.Members[k];
                if (m.HiddenBrace)
                    continue;
                Color c = Color.white;
                float tint = 0f, stripe = 0f;
                if (preview)
                {
                    bool floating = _preview.Solve.MemberForces[k] == null;
                    float u = (float)_preview.Utilizations[k];
                    c = floating ? floatingColor : BridgeView.UtilizationColor(u);
                    tint = floating ? highlightTint : LoadTint(u);
                    stripe = floating ? 0f : u;
                }
                if (m.LineId == _hoverLine)
                    (c, tint) = (warningColor, highlightTint);
                else if (_drawing && m.LineId == _strokeFx.PartnerLine)
                    (c, tint) = (ghostColor, highlightTint);      // W4-04: the rail the new line is snapping parallel to
                else if (blocked && _blockedMembers.Contains(m.Id))
                    Blend(ref c, ref tint, warningColor, highlightTint, Pulse(8f));
                _view.Beam(V(Node(input, m.NodeA)), V(Node(input, m.NodeB)), Materials.Of(m.SectionId), c, tint, stripe);
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
            _strokeFx.Draw(_view, _material, memberRadius, ghostColor, highlightTint, warningColor, _drawing, Time.time);   // W4-04
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
                Color c = BridgeView.UtilizationColor((float)WorstEdge(deck, snap.Utilization, snap.State));
                DrawDeck(deck, i => V(pos[i]), c, loadTint, snap.State, input, _fx.NodeFade);
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
                var material = Materials.Of(m.SectionId);
                if (state == MemberState.Falling)
                {
                    float fade = Mathf.Min(_fx.NodeFade(ia), _fx.NodeFade(ib));
                    if (fade > 0f)
                        _view.Beam(a, b, material, fallingColor, highlightTint, 0f, fade);
                }
                else
                {
                    _fx.Member(_view, a, b, material, BridgeView.UtilizationColor(u), LoadTint(u), u, k < buckling.Length ? (float)buckling[k] : 0f);
                }
            }
            _fx.DrawPieces(_view);
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
            float sleeve = Mathf.Max(memberRadius, _view.HalfWidth(Materials.Of(m.SectionId)));
            _view.Tube(a, b, sleeve * (2f + Pulse(6f)), c);
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

        /// <summary>
        /// The deck in its material, tint of color over it. Torn triangles are skipped (they fall as pieces); fade
        /// shrinks fallen ones toward their middle.
        /// </summary>
        void DrawDeck(DeckGeometry deck, System.Func<int, Vector3> position, Color color, float tint, MemberState[] state = null,
                      SolverInput input = null, System.Func<int, float> fade = null)
        {
            var material = Materials.Of(deck.SectionId);
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
                _view.MaterialTriangle(p0, p1, p2, material, color, tint);
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

        float LoadTint(float utilization) => loadTint + loadTintRise * Mathf.Clamp01(utilization);

        /// <summary>Moves a tinted look toward another by amount, mixing the two the way the shader would.</summary>
        static void Blend(ref Color color, ref float tint, Color toward, float towardTint, float amount)
        {
            float mixed = Mathf.Lerp(tint, towardTint, amount);
            if (mixed > 0f)
                color = (color * (tint * (1f - amount)) + toward * (towardTint * amount)) / mixed;
            tint = mixed;
        }

        static float Pulse(float speed) => 0.5f + 0.5f * Mathf.Sin(Time.time * speed);
    }
}
