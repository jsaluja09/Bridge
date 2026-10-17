using System;
using System.Collections.Generic;
using System.Numerics;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Pieces that break away from every support, simulated as stiff XPBD stick structures in bridge-world
    /// units. Besides each element, every pair of nodes two elements apart is held at its distance, so a
    /// broken-off truss falls as a rigid-ish piece instead of a rope. Pieces land on the floor and the surfaces.
    /// </summary>
    public sealed class DebrisWorld
    {
        readonly BridgeStructure _st;
        readonly int[] _slot;                 // structure node -> debris index, -1 if not debris
        readonly List<int> _node = new List<int>();
        readonly List<Vector3> _pos = new List<Vector3>();
        readonly List<Vector3> _prev = new List<Vector3>();
        readonly List<Vector3> _vel = new List<Vector3>();
        readonly List<float> _invMass = new List<float>();
        readonly List<Vector3> _force = new List<Vector3>();
        readonly List<(int a, int b, float rest)> _links = new List<(int, int, float)>();

        public DebrisWorld(BridgeStructure st)
        {
            _st = st;
            _slot = new int[st.NodeCount];
            for (int i = 0; i < _slot.Length; i++)
                _slot[i] = -1;
        }

        public bool Contains(int node) => _slot[node] >= 0;
        public Vector3 Position(int node) => _pos[_slot[node]];
        public int Count => _node.Count;

        /// <summary>Takes over these nodes at their current positions and velocities.</summary>
        public void Add(List<int> nodes, Func<int, Vector3> position, Func<int, Vector3> velocity, bool[] elementActive)
        {
            foreach (int i in nodes)
            {
                if (_slot[i] >= 0)
                    continue;
                _slot[i] = _node.Count;
                _node.Add(i);
                Vector3 p = position(i);
                _pos.Add(p);
                _prev.Add(p);
                _vel.Add(velocity(i));
                _invMass.Add(1f / Math.Max(1f, _st.Mass[i]));
                _force.Add(Vector3.Zero);
            }

            // Links: each active element, plus each pair of nodes sharing a neighbor.
            var set = new HashSet<int>(nodes);
            var linked = new HashSet<(int, int)>();
            foreach (int v in nodes)
            {
                var neighbors = new List<int>();
                for (int e = _st.NodeElementStart[v]; e < _st.NodeElementStart[v + 1]; e++)
                {
                    int k = _st.NodeElements[e];
                    if (!elementActive[k])
                        continue;
                    int other = _st.ElemA[k] == v ? _st.ElemB[k] : _st.ElemA[k];
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
            var key = a < b ? (a, b) : (b, a);
            if (!linked.Add(key))
                return;
            int sa = _slot[a], sb = _slot[b];
            _links.Add((sa, sb, Vector3.Distance(_pos[sa], _pos[sb])));
        }

        public void ClearForces()
        {
            for (int i = 0; i < _force.Count; i++)
                _force[i] = Vector3.Zero;
        }

        public void AddForce(int node, Vector3 force)
        {
            int s = _slot[node];
            if (s >= 0)
                _force[s] += force;
        }

        public void Step(float dt)
        {
            int count = _node.Count;
            if (count == 0)
                return;
            var s = _st.Settings;
            int substeps = Math.Max(1, s.DebrisSubsteps);
            float h = dt / substeps;
            var gravity = new Vector3(0f, -s.Gravity, 0f);

            for (int sub = 0; sub < substeps; sub++)
            {
                for (int i = 0; i < count; i++)
                {
                    _prev[i] = _pos[i];
                    _vel[i] += h * (gravity + _force[i] * _invMass[i]);
                    _pos[i] += h * _vel[i];
                }

                foreach (var (a, b, rest) in _links)
                {
                    Vector3 d = _pos[a] - _pos[b];
                    float len = d.Length();
                    if (len < 1e-6f)
                        continue;
                    float wa = _invMass[a], wb = _invMass[b];
                    float corr = (len - rest) / (len * (wa + wb));
                    _pos[a] -= d * (corr * wa);
                    _pos[b] += d * (corr * wb);
                }

                for (int i = 0; i < count; i++)
                {
                    Collide(i);
                    _vel[i] = (_pos[i] - _prev[i]) / h;
                }
            }
        }

        void Collide(int i)
        {
            Vector3 p = _pos[i];
            float floor = _st.GroundY;
            foreach (var surface in _st.Surfaces)
            {
                if (_prev[i].Y >= surface.Height - 0.05f && surface.Contains(p))
                    floor = Math.Max(floor, surface.Height);
            }
            if (p.Y >= floor)
                return;

            // Land: stop sinking, and let friction drag the horizontal motion.
            Vector3 prev = _prev[i];
            float keep = 1f - _st.Settings.DebrisFriction;
            p.X = prev.X + (p.X - prev.X) * keep;
            p.Z = prev.Z + (p.Z - prev.Z) * keep;
            p.Y = floor;
            _pos[i] = p;
        }
    }
}
