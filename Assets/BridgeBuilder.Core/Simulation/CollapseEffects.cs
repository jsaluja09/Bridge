using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// A falling piece of bridge, for drawing only: two points (half of a snapped member) or three (a torn
    /// piece of deck). Room units.
    /// </summary>
    public sealed class Fragment
    {
        public bool Plate;
        public int SourceMember;          // member index in the solver input
        public double BornAt;             // simulation seconds
        public double LandedAt = double.NaN;
        public double Scale = 1;          // 1 while whole, shrinking to 0 as it goes away
        public Vec3 Center;
        public Vec3 Velocity;
        public Vec3 Spin;                 // angular velocity, rad/s
        public readonly Vec3[] Offsets;   // from Center, already rotated

        public Fragment(int points) => Offsets = new Vec3[points];

        public int PointCount => Offsets.Length;
        public Vec3 Point(int i) => Center + Offsets[i] * Scale;
    }

    /// <summary>
    /// The visible side of a collapse (W4-07). When a member fails it splits at its failure point into two halves
    /// that fly apart, tumble and fall; when the deck tears, each torn triangle drops as a plate. Pieces land on
    /// the floor, lie there for a moment, then shrink away. Pieces the solver already drops as debris (spec 2.7)
    /// get the same ending: once a loose joint has settled, NodeFade shrinks what hangs from it. Stepped in the
    /// solver's fixed dt up to the simulation time it is given, so the same inputs always give the same pieces.
    /// </summary>
    public sealed class CollapseEffects
    {
        readonly CompiledBridge _bridge;
        readonly double _floor;
        readonly Vec3 _gravity;
        readonly double _dt = 1.0 / Constants.Dynamics.SolverRateHz;
        readonly List<Fragment> _fragments = new List<Fragment>();
        readonly Dictionary<int, int> _memberIndex = new Dictionary<int, int>();
        readonly List<(int deck, int tri)>[] _trianglesOf;
        readonly HashSet<(int, int)> _torn = new HashSet<(int, int)>();
        readonly Vec3[] _last, _velocity;
        readonly double[] _settledAt;
        readonly bool[] _loose;
        double _lastTime = double.NaN;
        double _stepped;

        public CollapseEffects(CompiledBridge bridge, double floorRoomY, double scale, double gravity = Constants.Loads.Gravity)
        {
            if (!(scale > 0))
                throw new ArgumentOutOfRangeException(nameof(scale));
            _bridge = bridge;
            _floor = floorRoomY;
            _gravity = new Vec3(0, -gravity / scale, 0);      // room m/s^2, DERIVED: the debris falls the same way
            var input = bridge.Input;
            for (int k = 0; k < input.Members.Count; k++)
                _memberIndex[input.Members[k].Id] = k;
            _trianglesOf = new List<(int, int)>[input.Members.Count];
            for (int d = 0; d < bridge.Decks.Count; d++)
            {
                var edges = bridge.Decks[d].TriangleEdges;
                for (int t = 0; t < edges.Length; t += 3)
                {
                    for (int e = 0; e < 3; e++)
                    {
                        int k = edges[t + e];
                        if (k >= 0)
                            (_trianglesOf[k] ??= new List<(int, int)>()).Add((d, t));
                    }
                }
            }
            int n = input.Nodes.Count;
            _last = new Vec3[n];
            _velocity = new Vec3[n];
            _settledAt = new double[n];
            _loose = new bool[n];
            for (int i = 0; i < n; i++)
                _settledAt[i] = double.NaN;
        }

        public IReadOnlyList<Fragment> Fragments => _fragments;

        /// <summary>Simulation seconds the pieces have been stepped to.</summary>
        public double Time => _stepped;

        /// <summary>True once this deck triangle has torn away and is drawn as a falling plate instead.</summary>
        public bool Torn(int deck, int triangleStart) => _torn.Contains((deck, triangleStart));

        /// <summary>1 for joints still in the bridge or still falling; shrinks to 0 after a loose joint has settled.</summary>
        public double NodeFade(int node)
        {
            double settled = _settledAt[node];
            return double.IsNaN(settled) ? 1 : ShrinkFactor(_lastTime - settled);
        }

        /// <summary>
        /// Takes the latest node positions (room units) and member states, spawns pieces for new failures, and
        /// steps every piece up to simTime.
        /// </summary>
        public void Update(double simTime, Vec3[] positions, MemberState[] state, IReadOnlyList<FailureReport> newReports)
        {
            if (!double.IsFinite(simTime))
                throw new ArgumentOutOfRangeException(nameof(simTime));
            TrackNodes(simTime, positions, state);
            if (newReports != null)
            {
                foreach (var report in newReports)
                {
                    foreach (var f in report.Failures)
                        Spawn(f, positions, simTime);
                }
            }
            while (_stepped + _dt <= simTime + 1e-9)
            {
                _stepped += _dt;
                for (int i = _fragments.Count - 1; i >= 0; i--)
                {
                    if (!Step(_fragments[i]))
                        _fragments.RemoveAt(i);
                }
            }
        }

        // ---------- Joints the solver already dropped ----------

        void TrackNodes(double time, Vec3[] positions, MemberState[] state)
        {
            var input = _bridge.Input;
            Array.Clear(_loose, 0, _loose.Length);
            var held = new bool[_loose.Length];
            for (int k = 0; k < input.Members.Count; k++)
            {
                int a = _bridge.NodeIndex[input.Members[k].NodeA], b = _bridge.NodeIndex[input.Members[k].NodeB];
                if (state[k] == MemberState.Falling)
                {
                    _loose[a] = true;
                    _loose[b] = true;
                }
                else if (state[k] == MemberState.Standing)
                {
                    held[a] = true;
                    held[b] = true;
                }
            }

            double elapsed = double.IsNaN(_lastTime) ? 0 : time - _lastTime;
            for (int i = 0; i < positions.Length; i++)
            {
                if (!positions[i].IsFinite)
                    throw new ArgumentOutOfRangeException(nameof(positions), "Node position is NaN or Infinity");
                if (elapsed > 0)
                    _velocity[i] = (positions[i] - _last[i]) / elapsed;
                _last[i] = positions[i];
                bool loose = _loose[i] && !held[i];
                if (loose && double.IsNaN(_settledAt[i]) && elapsed > 0 && _velocity[i].Length < Constants.CollapseEffects.SettleSpeed)
                    _settledAt[i] = time;
            }
            if (elapsed > 0 || double.IsNaN(_lastTime))
                _lastTime = time;
        }

        // ---------- New pieces ----------

        void Spawn(MemberFailure f, Vec3[] positions, double time)
        {
            if (!_memberIndex.TryGetValue(f.MemberId, out int k))
                throw new ArgumentException($"Failed member {f.MemberId} is not in this bridge", nameof(f));
            var m = _bridge.Input.Members[k];
            if (m.HiddenBrace)
            {
                if (_trianglesOf[k] != null)
                {
                    foreach (var (deck, tri) in _trianglesOf[k])
                    {
                        if (_torn.Add((deck, tri)))
                            SpawnPlate(k, deck, tri, positions, time);
                    }
                }
                return;
            }

            int a = _bridge.NodeIndex[m.NodeA], b = _bridge.NodeIndex[m.NodeB];
            Vec3 pa = positions[a], pb = positions[b];
            Vec3 at = Vec3.Lerp(pa, pb, f.FailurePoint);
            Vec3 axis = pb - pa;
            if (axis.LengthSquared < 1e-12)
                return;
            axis = axis.Normalized();
            // The broken ends drop: half A turns its +axis end down, half B its -axis end.
            Vec3 tumble = Horizontal(Vec3.Cross(axis, Vec3.UnitY), Vec3.UnitZ) * Constants.CollapseEffects.SpinRate;
            SpawnRod(k, pa, at, _velocity[a] - axis * Constants.CollapseEffects.KickSpeed, -tumble, time);
            SpawnRod(k, at, pb, _velocity[b] + axis * Constants.CollapseEffects.KickSpeed, tumble, time);
        }

        void SpawnRod(int member, Vec3 from, Vec3 to, Vec3 velocity, Vec3 spin, double time)
        {
            if (Vec3.Distance(from, to) < 1e-4 || _fragments.Count >= Constants.CollapseEffects.MaxFragments)
                return;
            var f = new Fragment(2) { SourceMember = member, BornAt = time, Center = (from + to) * 0.5, Velocity = velocity, Spin = spin };
            f.Offsets[0] = from - f.Center;
            f.Offsets[1] = to - f.Center;
            _fragments.Add(f);
        }

        void SpawnPlate(int member, int deck, int tri, Vec3[] positions, double time)
        {
            if (_fragments.Count >= Constants.CollapseEffects.MaxFragments)
                return;
            var t = _bridge.Decks[deck].Triangles;
            Vec3 p0 = positions[t[tri]], p1 = positions[t[tri + 1]], p2 = positions[t[tri + 2]];
            var f = new Fragment(3)
            {
                Plate = true,
                SourceMember = member,
                BornAt = time,
                Center = (p0 + p1 + p2) / 3,
                Velocity = (_velocity[t[tri]] + _velocity[t[tri + 1]] + _velocity[t[tri + 2]]) / 3,
                Spin = Horizontal(Vec3.Cross(Vec3.UnitY, p1 - p0), Vec3.UnitX) * (Constants.CollapseEffects.SpinRate * 0.5),
            };
            f.Offsets[0] = p0 - f.Center;
            f.Offsets[1] = p1 - f.Center;
            f.Offsets[2] = p2 - f.Center;
            _fragments.Add(f);
        }

        static Vec3 Horizontal(Vec3 v, Vec3 fallback) => v.LengthSquared > 1e-12 ? v.Normalized() : fallback;

        // ---------- Falling ----------

        /// <summary>One fixed step. False once the piece has shrunk away.</summary>
        bool Step(Fragment f)
        {
            double h = _dt;
            double ground = Ground(f.Center);
            f.Velocity += _gravity * h;
            f.Center += f.Velocity * h;
            double angle = f.Spin.Length * h;
            if (angle > 0)
            {
                Vec3 k = f.Spin / f.Spin.Length;
                for (int i = 0; i < f.Offsets.Length; i++)
                    f.Offsets[i] = Rotate(f.Offsets[i], k, angle);
            }

            double lowest = double.MaxValue;
            for (int i = 0; i < f.PointCount; i++)
                lowest = Math.Min(lowest, f.Point(i).Y);
            if (lowest < ground)
            {
                f.Center += new Vec3(0, ground - lowest, 0);
                double keep = Constants.CollapseEffects.FloorGrip;
                f.Velocity = new Vec3(f.Velocity.X * keep, Math.Max(0, f.Velocity.Y), f.Velocity.Z * keep);
                f.Spin *= keep;
                if (double.IsNaN(f.LandedAt) && f.Velocity.Length < Constants.CollapseEffects.SettleSpeed)
                    f.LandedAt = _stepped;
            }

            double age = _stepped - f.BornAt;
            double since = !double.IsNaN(f.LandedAt) ? _stepped - f.LandedAt
                         : age > Constants.CollapseEffects.MaxSeconds ? age - Constants.CollapseEffects.MaxSeconds + Constants.CollapseEffects.LingerSeconds
                         : 0;
            f.Scale = ShrinkFactor(since);
            if (!f.Center.IsFinite || !f.Velocity.IsFinite)
                throw new InvalidOperationException("Fragment went NaN or Infinity");
            return f.Scale > 0;
        }

        /// <summary>The floor, or the top of a pad the piece is above (it lands on pads instead of passing through).</summary>
        double Ground(Vec3 center)
        {
            double ground = _floor;
            if (_bridge.Pads == null)
                return ground;
            foreach (var pad in _bridge.Pads)
            {
                if (center.Y >= pad.Height && pad.Over(center))
                    ground = Math.Max(ground, pad.Height);
            }
            return ground;
        }

        /// <summary>1 while lingering, then down to 0 over the shrink time.</summary>
        static double ShrinkFactor(double secondsSinceSettled)
        {
            double t = (secondsSinceSettled - Constants.CollapseEffects.LingerSeconds) / Constants.CollapseEffects.ShrinkSeconds;
            return t <= 0 ? 1 : t >= 1 ? 0 : 1 - t;
        }

        /// <summary>Rodrigues: v turned by angle about the unit axis k.</summary>
        static Vec3 Rotate(Vec3 v, Vec3 k, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return v * c + Vec3.Cross(k, v) * s + k * (Vec3.Dot(k, v) * (1 - c));
        }
    }
}
