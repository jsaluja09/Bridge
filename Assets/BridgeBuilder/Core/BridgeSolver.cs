using System;
using System.Collections.Generic;
using System.Numerics;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Linear 3D frame solver (spec 2.7, 2.8, 2.12): rigid-jointed beam elements, banded Cholesky with
    /// reverse Cuthill-McKee ordering, Newmark average-acceleration dynamics with Rayleigh damping.
    /// Refactors only at the start and when elements fail. Nodes that lose every path to a support are
    /// reported as detached so the caller can hand them to the debris simulation.
    /// </summary>
    public sealed class BridgeSolver
    {
        readonly BridgeStructure _st;
        readonly BridgeSettings _s;
        readonly int _n;
        readonly int _m;

        // Per element, from the rest geometry.
        readonly double[] _axes;      // 9 per element: local x, y, z as rows
        readonly double[] _ke;        // 144 per element: global stiffness
        readonly double[] _ea, _ei, _gj;

        public readonly bool[] Active;        // not broken
        public readonly bool[] InFrame;       // active and still attached to a support
        public readonly bool[] NodeInFrame;
        public readonly float[] Utilization;
        public readonly float[] AxialForce;   // tension positive, newtons
        public readonly double[] U;           // 6 per node: displacement and rotation
        public readonly double[] V;
        public readonly double[] A;

        readonly int[] _order;
        readonly List<int>[] _adjacency;
        int _equations;
        readonly BandedMatrix _k = new BandedMatrix();
        readonly BandedMatrix _keff = new BandedMatrix();
        readonly BandedMatrix _work = new BandedMatrix();
        double[] _mass = Array.Empty<double>();
        bool[] _constrained = Array.Empty<bool>();
        double[] _u = Array.Empty<double>(), _v = Array.Empty<double>(), _a = Array.Empty<double>();
        double[] _rhs = Array.Empty<double>(), _w = Array.Empty<double>(), _kw = Array.Empty<double>();
        double _dt;

        readonly List<int> _candidates = new List<int>();
        readonly Comparison<int> _byUtilization;

        public bool Singular { get; private set; }
        public int Equations => _equations;
        public int Bandwidth => _k.Band;

        public BridgeSolver(BridgeStructure st)
        {
            _st = st;
            _s = st.Settings;
            _n = st.NodeCount;
            _m = st.ElementCount;

            _axes = new double[_m * 9];
            _ke = new double[_m * 144];
            _ea = new double[_m];
            _ei = new double[_m];
            _gj = new double[_m];
            Active = new bool[_m];
            InFrame = new bool[_m];
            NodeInFrame = new bool[_n];
            Utilization = new float[_m];
            AxialForce = new float[_m];
            U = new double[_n * 6];
            V = new double[_n * 6];
            A = new double[_n * 6];
            _order = new int[_n];
            _adjacency = new List<int>[_n];
            for (int i = 0; i < _n; i++)
                _adjacency[i] = new List<int>();

            for (int k = 0; k < _m; k++)
            {
                Active[k] = true;
                BuildElement(k);
            }
            _byUtilization = (x, y) => Utilization[y].CompareTo(Utilization[x]);
        }

        // ---------- Element stiffness ----------

        void BuildElement(int k)
        {
            Vector3 pa = _st.Rest[_st.ElemA[k]], pb = _st.Rest[_st.ElemB[k]];
            double L = _st.Length[k];
            double ex = (pb.X - pa.X) / L, ey = (pb.Y - pa.Y) / L, ez = (pb.Z - pa.Z) / L;

            // Local y is horizontal where possible; the tube is round, so only consistency matters.
            double rx = 0, ry = 1, rz = 0;
            if (Math.Abs(ey) > 0.99) { rx = 1; ry = 0; }
            double yx = ry * ez - rz * ey, yy = rz * ex - rx * ez, yz = rx * ey - ry * ex;
            double yl = Math.Sqrt(yx * yx + yy * yy + yz * yz);
            yx /= yl; yy /= yl; yz /= yl;
            double zx = ey * yz - ez * yy, zy = ez * yx - ex * yz, zz = ex * yy - ey * yx;

            int o = k * 9;
            _axes[o] = ex; _axes[o + 1] = ey; _axes[o + 2] = ez;
            _axes[o + 3] = yx; _axes[o + 4] = yy; _axes[o + 5] = yz;
            _axes[o + 6] = zx; _axes[o + 7] = zy; _axes[o + 8] = zz;

            double E = _s.YoungsModulus, G = _s.ShearModulus;
            _ea[k] = E * _st.Area;
            _ei[k] = E * _st.Inertia;
            _gj[k] = G * _st.Torsion;

            var kl = new double[144];
            double a = _ea[k] / L, t = _gj[k] / L;
            double a12 = 12 * _ei[k] / (L * L * L), a6 = 6 * _ei[k] / (L * L), a4 = 4 * _ei[k] / L, a2 = 2 * _ei[k] / L;
            void S(int i, int j, double v) { kl[i * 12 + j] = v; kl[j * 12 + i] = v; }
            S(0, 0, a); S(0, 6, -a); S(6, 6, a);
            S(3, 3, t); S(3, 9, -t); S(9, 9, t);
            S(1, 1, a12); S(1, 5, a6); S(1, 7, -a12); S(1, 11, a6);
            S(5, 5, a4); S(5, 7, -a6); S(5, 11, a2);
            S(7, 7, a12); S(7, 11, -a6); S(11, 11, a4);
            S(2, 2, a12); S(2, 4, -a6); S(2, 8, -a12); S(2, 10, -a6);
            S(4, 4, a4); S(4, 8, a6); S(4, 10, a2);
            S(8, 8, a12); S(8, 10, a6); S(10, 10, a4);

            // Global = T^T k T, block by block, with T = diag(R, R, R, R).
            int g = k * 144;
            for (int bi = 0; bi < 4; bi++)
            {
                for (int bj = 0; bj < 4; bj++)
                {
                    for (int r = 0; r < 3; r++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            double sum = 0;
                            for (int p = 0; p < 3; p++)
                            {
                                for (int q = 0; q < 3; q++)
                                    sum += _axes[o + p * 3 + r] * kl[(bi * 3 + p) * 12 + bj * 3 + q] * _axes[o + q * 3 + c];
                            }
                            _ke[g + (bi * 3 + r) * 12 + bj * 3 + c] = sum;
                        }
                    }
                }
            }
        }

        // ---------- Assembly ----------

        /// <summary>
        /// Recomputes which nodes are still attached to a support, renumbers, and reassembles.
        /// Returns nodes that were attached before (or ever, on the first call) and now are not.
        /// </summary>
        public List<int> Rebuild(bool firstTime)
        {
            var detached = new List<int>();
            var reached = new bool[_n];
            var queue = new Queue<int>();
            for (int i = 0; i < _n; i++)
            {
                if (!_st.Fixed[i])
                    continue;
                for (int e = _st.NodeElementStart[i]; e < _st.NodeElementStart[i + 1]; e++)
                {
                    if (Active[_st.NodeElements[e]])
                    {
                        reached[i] = true;
                        queue.Enqueue(i);
                        break;
                    }
                }
            }
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                for (int e = _st.NodeElementStart[v]; e < _st.NodeElementStart[v + 1]; e++)
                {
                    int k = _st.NodeElements[e];
                    if (!Active[k])
                        continue;
                    int other = _st.ElemA[k] == v ? _st.ElemB[k] : _st.ElemA[k];
                    if (!reached[other])
                    {
                        reached[other] = true;
                        queue.Enqueue(other);
                    }
                }
            }

            for (int i = 0; i < _n; i++)
            {
                if ((NodeInFrame[i] || firstTime) && !reached[i] && !_st.Fixed[i])
                    detached.Add(i);
                NodeInFrame[i] = reached[i];
            }
            for (int k = 0; k < _m; k++)
            {
                InFrame[k] = Active[k] && reached[_st.ElemA[k]] && reached[_st.ElemB[k]];
                if (!InFrame[k])
                    Utilization[k] = AxialForce[k] = 0f;
            }

            Assemble();
            return detached;
        }

        void Assemble()
        {
            for (int i = 0; i < _n; i++)
                _adjacency[i].Clear();
            for (int k = 0; k < _m; k++)
            {
                if (!InFrame[k])
                    continue;
                _adjacency[_st.ElemA[k]].Add(_st.ElemB[k]);
                _adjacency[_st.ElemB[k]].Add(_st.ElemA[k]);
            }
            int width = BandedMatrix.ReverseCuthillMcKee(_n, NodeInFrame, _adjacency, _order);

            int frameNodes = 0;
            for (int i = 0; i < _n; i++)
            {
                if (NodeInFrame[i])
                    frameNodes++;
            }
            _equations = frameNodes * 6;
            int band = Math.Max(5, width * 6 + 5);
            _k.Resize(_equations, band);
            EnsureVectors(_equations);

            for (int i = 0; i < _n; i++)
            {
                if (!NodeInFrame[i])
                    continue;
                int b = _order[i] * 6;
                bool fixedNode = _st.Fixed[i];
                for (int d = 0; d < 6; d++)
                {
                    bool constrained = fixedNode && (d < 3 || !_s.PinnedSupports);
                    _constrained[b + d] = constrained;
                    _mass[b + d] = constrained ? 0 : d < 3 ? _st.Mass[i] : _st.RotaryInertia[i];
                }
            }

            var dofs = new int[12];
            for (int k = 0; k < _m; k++)
            {
                if (!InFrame[k])
                    continue;
                int oa = _order[_st.ElemA[k]] * 6, ob = _order[_st.ElemB[k]] * 6;
                for (int d = 0; d < 6; d++)
                {
                    dofs[d] = oa + d;
                    dofs[6 + d] = ob + d;
                }
                int g = k * 144;
                for (int r = 0; r < 12; r++)
                {
                    int i = dofs[r];
                    if (_constrained[i])
                        continue;
                    for (int c = 0; c < 12; c++)
                    {
                        int j = dofs[c];
                        if (j > i || _constrained[j])
                            continue;
                        _k.Add(i, j, _ke[g + r * 12 + c]);
                    }
                }
            }
            for (int i = 0; i < _equations; i++)
            {
                if (_constrained[i])
                    _k.Set(i, i, 1.0);
            }
        }

        void EnsureVectors(int n)
        {
            if (_mass.Length >= n)
            {
                Array.Clear(_constrained, 0, n);
                return;
            }
            _mass = new double[n];
            _constrained = new bool[n];
            _u = new double[n];
            _v = new double[n];
            _a = new double[n];
            _rhs = new double[n];
            _w = new double[n];
            _kw = new double[n];
        }

        // ---------- Solving ----------

        /// <summary>Static equilibrium under these loads (bridge world, newtons per node). Resets velocity.</summary>
        public bool SolveStatic(Vector3[] loads)
        {
            _work.CopyFrom(_k);
            // A whisper of regularization so a pinned, spinnable structure can't make this singular.
            double reg = 1e-9 * MaxDiagonal(_k);
            for (int i = 0; i < _equations; i++)
                _work.Add(i, i, reg);
            if (!_work.Factor())
            {
                Singular = true;
                return false;
            }
            LoadVector(loads, _rhs);
            _work.Solve(_rhs);
            Scatter(_rhs, U);
            Array.Clear(V, 0, V.Length);
            Array.Clear(A, 0, A.Length);
            return true;
        }

        double MaxDiagonal(BandedMatrix m)
        {
            double max = 1;
            for (int i = 0; i < _equations; i++)
                max = Math.Max(max, m.Get(i, i));
            return max;
        }

        /// <summary>Builds and factors the Newmark effective stiffness for this timestep.</summary>
        public bool PrepareDynamics(double dt)
        {
            _dt = dt;
            double a0 = 4 / (dt * dt), a1 = 2 / dt;
            double cK = 1 + a1 * _s.RayleighBeta;
            double cM = a0 + a1 * _s.RayleighAlpha;
            var diag = new double[_equations];
            for (int i = 0; i < _equations; i++)
                diag[i] = cM * _mass[i];
            _keff.SetScaled(_k, cK, diag);
            if (!_keff.Factor())
            {
                Singular = true;
                return false;
            }
            return true;
        }

        /// <summary>One Newmark average-acceleration step: a cheap back-substitution.</summary>
        public void Step(Vector3[] loads)
        {
            double dt = _dt;
            double a0 = 4 / (dt * dt), a1 = 2 / dt, a2 = 4 / dt;
            double alpha = _s.RayleighAlpha, beta = _s.RayleighBeta;
            int n = _equations;

            Gather(U, _u);
            Gather(V, _v);
            Gather(A, _a);
            for (int i = 0; i < n; i++)
                _w[i] = a1 * _u[i] + _v[i];
            _k.Multiply(_w, _kw);

            LoadVector(loads, _rhs);
            for (int i = 0; i < n; i++)
            {
                if (_constrained[i])
                {
                    _rhs[i] = 0;
                    continue;
                }
                _rhs[i] += _mass[i] * (a0 * _u[i] + a2 * _v[i] + _a[i] + alpha * _w[i]) + beta * _kw[i];
            }
            _keff.Solve(_rhs);

            for (int i = 0; i < n; i++)
            {
                double uNew = _rhs[i];
                double aNew = a0 * (uNew - _u[i]) - a2 * _v[i] - _a[i];
                _v[i] += 0.5 * dt * (_a[i] + aNew);
                _a[i] = aNew;
                _u[i] = uNew;
            }
            Scatter(_u, U);
            Scatter(_v, V);
            Scatter(_a, A);
        }

        void LoadVector(Vector3[] loads, double[] f)
        {
            Array.Clear(f, 0, _equations);
            for (int i = 0; i < _n; i++)
            {
                if (!NodeInFrame[i] || _st.Fixed[i])
                    continue;
                int b = _order[i] * 6;
                f[b] = loads[i].X;
                f[b + 1] = loads[i].Y;
                f[b + 2] = loads[i].Z;
            }
        }

        void Gather(double[] full, double[] reduced)
        {
            for (int i = 0; i < _n; i++)
            {
                if (!NodeInFrame[i])
                    continue;
                int b = _order[i] * 6;
                for (int d = 0; d < 6; d++)
                    reduced[b + d] = full[i * 6 + d];
            }
        }

        void Scatter(double[] reduced, double[] full)
        {
            for (int i = 0; i < _n; i++)
            {
                if (!NodeInFrame[i])
                    continue;
                int b = _order[i] * 6;
                for (int d = 0; d < 6; d++)
                    full[i * 6 + d] = reduced[b + d];
            }
        }

        public Vector3 Displacement(int node) => new Vector3((float)U[node * 6], (float)U[node * 6 + 1], (float)U[node * 6 + 2]);
        public Vector3 Velocity(int node) => new Vector3((float)V[node * 6], (float)V[node * 6 + 1], (float)V[node * 6 + 2]);

        // ---------- Capacity (spec 2.7) ----------

        /// <summary>Axial (tension, or compression capped by Euler buckling over the member) plus bending.</summary>
        public void UpdateUtilization()
        {
            for (int k = 0; k < _m; k++)
            {
                if (!InFrame[k])
                    continue;
                ElementForces(k, out double axial, out double moment);
                double axialUtil = axial >= 0
                    ? axial / _st.YieldForce
                    : -axial / Math.Min(_st.YieldForce, _st.EulerLoad(k));
                AxialForce[k] = (float)axial;
                Utilization[k] = (float)(axialUtil + moment / _st.YieldMoment);
            }
        }

        public void ElementForces(int k, out double axial, out double moment)
        {
            int ia = _st.ElemA[k] * 6, ib = _st.ElemB[k] * 6, o = k * 9;
            double L = _st.Length[k];

            // Local displacements: R times each 3-vector.
            double Lx(int b, int r) => _axes[o + r * 3] * U[b] + _axes[o + r * 3 + 1] * U[b + 1] + _axes[o + r * 3 + 2] * U[b + 2];
            double u1 = Lx(ia, 0), v1 = Lx(ia, 1), w1 = Lx(ia, 2);
            double ry1 = Lx(ia + 3, 1), rz1 = Lx(ia + 3, 2);
            double u2 = Lx(ib, 0), v2 = Lx(ib, 1), w2 = Lx(ib, 2);
            double ry2 = Lx(ib + 3, 1), rz2 = Lx(ib + 3, 2);

            double ei = _ei[k];
            double a6 = 6 * ei / (L * L), a4 = 4 * ei / L, a2 = 2 * ei / L;
            axial = _ea[k] / L * (u2 - u1);
            double my1 = -a6 * w1 + a4 * ry1 + a6 * w2 + a2 * ry2;
            double my2 = -a6 * w1 + a2 * ry1 + a6 * w2 + a4 * ry2;
            double mz1 = a6 * v1 + a4 * rz1 - a6 * v2 + a2 * rz2;
            double mz2 = a6 * v1 + a2 * rz1 - a6 * v2 + a4 * rz2;
            moment = Math.Max(Math.Sqrt(my1 * my1 + mz1 * mz1), Math.Sqrt(my2 * my2 + mz2 * mz2));
        }

        /// <summary>Breaks the worst over-utilized elements (up to the per-step limit). Returns how many broke.</summary>
        public int BreakFailures(List<int> broken)
        {
            _candidates.Clear();
            for (int k = 0; k < _m; k++)
            {
                if (InFrame[k] && Utilization[k] > 1f)
                    _candidates.Add(k);
            }
            if (_candidates.Count == 0)
                return 0;
            _candidates.Sort(_byUtilization);
            int count = Math.Min(_candidates.Count, Math.Max(1, _s.MaxFailuresPerStep));
            for (int i = 0; i < count; i++)
            {
                int k = _candidates[i];
                Active[k] = false;
                broken.Add(k);
            }
            return count;
        }
    }
}
