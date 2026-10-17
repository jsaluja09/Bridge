using System;
using System.Collections.Generic;
using System.Numerics;

namespace BridgeBuilder.Core
{
    public enum MemberKind
    {
        Drawn,      // a line the player drew (or its mirrored twin)
        Lateral,    // automatic ties and braces between the two sides
    }

    /// <summary>
    /// Pin-jointed truss solved with XPBD distance constraints and small substeps.
    /// Each member's real axial force comes out of the solve, so utilization and breaking use actual forces.
    /// Allocates only in Build; Step allocates nothing.
    /// </summary>
    public sealed class BridgeSolver
    {
        readonly BridgeSettings _s;

        // Nodes
        public int NodeCount { get; private set; }
        public Vector3[] Positions;
        Vector3[] _prev;
        Vector3[] _vel;
        float[] _baseMass;
        bool[] _fixed;
        Vector3[] _extForce;
        float[] _extMass;
        float[] _invMass;

        // Members
        public int MemberCount { get; private set; }
        public int[] MemberA;
        public int[] MemberB;
        public MemberKind[] Kind;
        public bool[] Active;
        public float[] Force;          // average axial force over the last step, tension positive
        public float[] Utilization;    // 1 = failing
        float[] _rest;
        float[] _compliance;
        float[] _tensionCap;
        float[] _compressionCap;
        float[] _lambda;
        float[] _forceSum;
        float[] _bending;              // moment from the car this step

        readonly List<int> _breakCandidates = new List<int>();

        public float GravityScale = 1f;
        public float DampingRate;
        public bool Exploded { get; private set; }
        public int BrokenCount { get; private set; }
        public float PeakUtilization { get; private set; }

        /// <summary>Members that broke during the last Step.</summary>
        public readonly List<int> BrokenThisStep = new List<int>();

        public BridgeSolver(BridgeSettings settings)
        {
            _s = settings;
            DampingRate = settings.Damping;
            _byUtilizationDesc = (x, y) => Utilization[y].CompareTo(Utilization[x]);
        }

        readonly Comparison<int> _byUtilizationDesc;

        public void Build(IReadOnlyList<Vector3> nodes, IReadOnlyList<bool> isFixed,
                          IReadOnlyList<(int a, int b, MemberKind kind)> members)
        {
            NodeCount = nodes.Count;
            Positions = new Vector3[NodeCount];
            _prev = new Vector3[NodeCount];
            _vel = new Vector3[NodeCount];
            _baseMass = new float[NodeCount];
            _fixed = new bool[NodeCount];
            _extForce = new Vector3[NodeCount];
            _extMass = new float[NodeCount];
            _invMass = new float[NodeCount];
            for (int i = 0; i < NodeCount; i++)
            {
                Positions[i] = nodes[i];
                _fixed[i] = isFixed[i];
            }

            MemberCount = members.Count;
            MemberA = new int[MemberCount];
            MemberB = new int[MemberCount];
            Kind = new MemberKind[MemberCount];
            Active = new bool[MemberCount];
            Force = new float[MemberCount];
            Utilization = new float[MemberCount];
            _rest = new float[MemberCount];
            _compliance = new float[MemberCount];
            _tensionCap = new float[MemberCount];
            _compressionCap = new float[MemberCount];
            _lambda = new float[MemberCount];
            _forceSum = new float[MemberCount];
            _bending = new float[MemberCount];

            for (int m = 0; m < MemberCount; m++)
            {
                var (a, b, kind) = members[m];
                MemberA[m] = a;
                MemberB[m] = b;
                Kind[m] = kind;
                Active[m] = true;
                float len = Math.Max(1e-3f, Vector3.Distance(nodes[a], nodes[b]));
                _rest[m] = len;
                _compliance[m] = len / _s.AxialStiffness;
                _tensionCap[m] = _s.TensionLimit;
                _compressionCap[m] = _s.CompressionCapacity(len);

                // Lateral bracing is light so the drawn members carry the story.
                float mass = _s.LinearDensity * len * (kind == MemberKind.Lateral ? 0.25f : 1f);
                _baseMass[a] += mass * 0.5f;
                _baseMass[b] += mass * 0.5f;
            }

            for (int i = 0; i < NodeCount; i++)
                _baseMass[i] = Math.Max(_baseMass[i], 0.005f);

            Exploded = false;
            BrokenCount = 0;
            PeakUtilization = 0f;
        }

        public float RestLength(int member) => _rest[member];
        public bool IsFixed(int node) => _fixed[node];

        public void ClearLoads()
        {
            Array.Clear(_extForce, 0, NodeCount);
            Array.Clear(_extMass, 0, NodeCount);
            Array.Clear(_bending, 0, MemberCount);
        }

        /// <summary>Adds a point load (and the mass that moves with it) to a node.</summary>
        public void AddLoad(int node, Vector3 force, float mass)
        {
            _extForce[node] += force;
            _extMass[node] += mass;
        }

        /// <summary>Adds bending in a member from a transverse point load (the car).</summary>
        public void AddBending(int member, float moment) => _bending[member] += moment;

        public void Step(float dt)
        {
            BrokenThisStep.Clear();
            if (Exploded)
                return;

            int substeps = Math.Max(1, _s.Substeps);
            int iterations = Math.Max(1, _s.Iterations);
            float h = dt / substeps;
            float h2 = h * h;
            Vector3 gravity = new Vector3(0f, -_s.Gravity * GravityScale, 0f);
            float damp = Math.Max(0f, 1f - DampingRate * h);

            for (int i = 0; i < NodeCount; i++)
                _invMass[i] = _fixed[i] ? 0f : 1f / (_baseMass[i] + _extMass[i]);
            Array.Clear(_forceSum, 0, MemberCount);

            for (int sub = 0; sub < substeps; sub++)
            {
                for (int i = 0; i < NodeCount; i++)
                {
                    _prev[i] = Positions[i];
                    if (_fixed[i])
                        continue;
                    _vel[i] += h * (gravity + _extForce[i] * _invMass[i]);
                    Positions[i] += h * _vel[i];
                }

                Array.Clear(_lambda, 0, MemberCount);
                for (int it = 0; it < iterations; it++)
                {
                    for (int m = 0; m < MemberCount; m++)
                    {
                        if (!Active[m])
                            continue;
                        int a = MemberA[m], b = MemberB[m];
                        float wa = _invMass[a], wb = _invMass[b];
                        float wSum = wa + wb;
                        if (wSum <= 0f)
                            continue;
                        Vector3 d = Positions[a] - Positions[b];
                        float len = d.Length();
                        if (len < 1e-6f)
                            continue;
                        Vector3 n = d / len;
                        float c = len - _rest[m];
                        float alpha = _compliance[m] / h2;
                        float dl = (-c - alpha * _lambda[m]) / (wSum + alpha);
                        _lambda[m] += dl;
                        Positions[a] += wa * dl * n;
                        Positions[b] -= wb * dl * n;
                    }
                }

                for (int i = 0; i < NodeCount; i++)
                {
                    if (_fixed[i])
                        continue;
                    _vel[i] = (Positions[i] - _prev[i]) / h * damp;
                }

                for (int m = 0; m < MemberCount; m++)
                    _forceSum[m] += -_lambda[m] / h2;
            }

            UpdateUtilization(substeps);
            BreakWorst();
            CheckFinite();
        }

        void UpdateUtilization(int substeps)
        {
            _breakCandidates.Clear();
            for (int m = 0; m < MemberCount; m++)
            {
                if (!Active[m])
                {
                    Force[m] = 0f;
                    Utilization[m] = 0f;
                    continue;
                }
                float f = _forceSum[m] / substeps;
                Force[m] = f;
                float axial = f >= 0f ? f / _tensionCap[m] : -f / _compressionCap[m];
                float u = axial + _bending[m] / _s.BendingLimit;
                Utilization[m] = u;
                if (Kind[m] == MemberKind.Drawn)
                {
                    if (u > PeakUtilization)
                        PeakUtilization = u;
                    if (u > 1f)
                        _breakCandidates.Add(m);
                }
            }
        }

        void BreakWorst()
        {
            if (_breakCandidates.Count == 0)
                return;
            _breakCandidates.Sort(_byUtilizationDesc);
            int n = Math.Min(_breakCandidates.Count, Math.Max(1, _s.MaxBreaksPerStep));
            for (int i = 0; i < n; i++)
            {
                int m = _breakCandidates[i];
                Active[m] = false;
                BrokenThisStep.Add(m);
                BrokenCount++;
            }
        }

        void CheckFinite()
        {
            for (int i = 0; i < NodeCount; i++)
            {
                Vector3 p = Positions[i];
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsNaN(p.Z) ||
                    float.IsInfinity(p.X) || float.IsInfinity(p.Y) || float.IsInfinity(p.Z))
                {
                    Exploded = true;
                    return;
                }
            }
        }
    }
}
