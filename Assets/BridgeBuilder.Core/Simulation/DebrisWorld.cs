using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Pieces that break away from every support (spec 2.7's progressive collapse, after the linear solver can
    /// no longer hold them), simulated as stiff stick structures with position-based dynamics in bridge-world
    /// units. Each member and each pair of nodes two members apart is held at its length, so a broken-off
    /// truss falls as a piece instead of a rope. Pieces land on the floor and the pads.
    /// </summary>
    public sealed class DebrisWorld
    {
        readonly IFrameIntegrator _frame;
        readonly IReadOnlyList<Pad> _pads;
        readonly double _scale;
        readonly double _floor;
        readonly double _gravity;
        readonly int[] _slot;
        readonly List<int> _node = new List<int>();
        readonly List<Vec3> _pos = new List<Vec3>();
        readonly List<Vec3> _prev = new List<Vec3>();
        readonly List<Vec3> _vel = new List<Vec3>();
        readonly List<double> _invMass = new List<double>();
        readonly List<Vec3> _force = new List<Vec3>();
        readonly List<(int a, int b, double rest)> _links = new List<(int, int, double)>();

        public DebrisWorld(IFrameIntegrator frame, IReadOnlyList<Pad> pads, double scale, double floorBridgeY, double gravity)
        {
            _frame = frame;
            _pads = pads ?? Array.Empty<Pad>();
            _scale = scale;
            _floor = floorBridgeY;
            _gravity = gravity;
            _slot = new int[frame.NodeCount];
            for (int i = 0; i < _slot.Length; i++)
                _slot[i] = -1;
        }

        public bool Contains(int node) => _slot[node] >= 0;
        public Vec3 Position(int node) => _pos[_slot[node]];
        public int Count => _node.Count;

        /// <summary>Takes over these nodes at their current positions and velocities.</summary>
        public void Add(List<int> nodes)
        {
            foreach (int i in nodes)
            {
                if (_slot[i] >= 0)
                    continue;
                _slot[i] = _node.Count;
                _node.Add(i);
                Vec3 p = _frame.Position(i);
                _pos.Add(p);
                _prev.Add(p);
                _vel.Add(_frame.Velocity(i));
                _invMass.Add(1.0 / Math.Max(1.0, _frame.NodeMass(i)));
                _force.Add(Vec3.Zero);
            }

            var set = new HashSet<int>(nodes);
            var linked = new HashSet<(int, int)>();
            foreach (int v in nodes)
            {
                var neighbors = new List<int>();
                foreach (int k in _frame.MembersAt(v))
                {
                    if (!_frame.Active[k])
                        continue;
                    int other = _frame.MemberNodeA(k) == v ? _frame.MemberNodeB(k) : _frame.MemberNodeA(k);
                    if (!set.Contains(other))
                        continue;
                    neighbors.Add(other);
                    Link(v, other, linked);
                }
                for (int x = 0; x < neighbors.Count; x++)
                {
                    for (int y = x + 1; y < neighbors.Count; y++)
                        Link(neighbors[x], neighbors[y], linked);
                }
            }
        }

        void Link(int a, int b, HashSet<(int, int)> linked)
        {
            if (!linked.Add(a < b ? (a, b) : (b, a)))
                return;
            int sa = _slot[a], sb = _slot[b];
            _links.Add((sa, sb, Vec3.Distance(_pos[sa], _pos[sb])));
        }

        public void ClearForces()
        {
            for (int i = 0; i < _force.Count; i++)
                _force[i] = Vec3.Zero;
        }

        public void AddForce(int node, Vec3 force)
        {
            int s = _slot[node];
            if (s >= 0)
                _force[s] += force;
        }

        public void Step(double dt, int substeps = 10)
        {
            int count = _node.Count;
            if (count == 0)
                return;
            double h = dt / substeps;
            var gravity = new Vec3(0, -_gravity, 0);
            for (int sub = 0; sub < substeps; sub++)
            {
                for (int i = 0; i < count; i++)
                {
                    _prev[i] = _pos[i];
                    _vel[i] += (gravity + _force[i] * _invMass[i]) * h;
                    _pos[i] += _vel[i] * h;
                }
                foreach (var (a, b, rest) in _links)
                {
                    Vec3 d = _pos[a] - _pos[b];
                    double len = d.Length;
                    if (len < 1e-9)
                        continue;
                    double wa = _invMass[a], wb = _invMass[b];
                    double corr = (len - rest) / (len * (wa + wb));
                    _pos[a] -= d * (corr * wa);
                    _pos[b] += d * (corr * wb);
                }
                for (int i = 0; i < count; i++)
                {
                    Land(i);
                    _vel[i] = (_pos[i] - _prev[i]) / h;
                }
            }
        }

        void Land(int i)
        {
            Vec3 p = _pos[i];
            double floor = _floor;
            foreach (var pad in _pads)
            {
                double top = pad.Height * _scale;
                if (_prev[i].Y >= top - 0.5 && pad.Over(p / _scale))
                    floor = Math.Max(floor, top);
            }
            if (p.Y >= floor)
                return;
            Vec3 prev = _prev[i];
            const double keep = 0.4;      // friction: keep 40% of the sliding motion on landing
            _pos[i] = new Vec3(prev.X + (p.X - prev.X) * keep, floor, prev.Z + (p.Z - prev.Z) * keep);
        }
    }
}
