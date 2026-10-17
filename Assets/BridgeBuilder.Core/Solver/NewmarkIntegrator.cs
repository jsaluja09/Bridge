using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Newmark time integration of M a + C v + K u = F with Rayleigh damping C = alpha M + beta K (spec 2.8).
    /// Average acceleration (beta 1/4, gamma 1/2) is unconditionally stable. The effective stiffness is factored
    /// once per timestep size; each step is a back-substitution. The linear solver comes from a factory so
    /// tests can count Factor calls.
    /// </summary>
    public sealed class NewmarkSystem
    {
        readonly int _n;
        readonly int _band;
        readonly double[] _mass;
        readonly List<(int row, int col, double value)> _k;     // lower triangle incl. diagonal
        readonly bool[] _constrained;
        readonly double _alpha, _beta;
        readonly Func<int, int, ILinearSolver> _factory;
        ILinearSolver _solver;
        double _factoredDt = -1;
        readonly double[] _rhs, _x, _kx, _next;

        public readonly double[] U, V, A;
        public int Size => _n;

        public NewmarkSystem(int n, int halfBandwidth, double[] mass, List<(int row, int col, double value)> lowerStiffness,
                             double rayleighAlpha, double rayleighBeta, Func<int, int, ILinearSolver> factory, bool[] constrained = null)
        {
            _n = n;
            _band = halfBandwidth;
            _mass = mass;
            _k = lowerStiffness;
            _alpha = rayleighAlpha;
            _beta = rayleighBeta;
            _factory = factory ?? ((order, band) => new BandedMatrix(order, band));
            _constrained = constrained ?? new bool[n];
            U = new double[n];
            V = new double[n];
            A = new double[n];
            _rhs = new double[n];
            _x = new double[n];
            _kx = new double[n];
            _next = new double[n];
        }

        static double Gamma => Constants.Dynamics.NewmarkGamma;
        static double BetaN => Constants.Dynamics.NewmarkBeta;

        /// <summary>y = K x.</summary>
        void MultiplyK(double[] x, double[] y)
        {
            Array.Clear(y, 0, _n);
            foreach (var (r, c, v) in _k)
            {
                y[r] += v * x[c];
                if (r != c)
                    y[c] += v * x[r];
            }
        }

        ILinearSolver Assemble(double kScale, double massScale)
        {
            var solver = _factory(_n, _band);
            foreach (var (r, c, v) in _k)
            {
                solver.Add(r, c, kScale * v);
                if (r != c)
                    solver.Add(c, r, kScale * v);
            }
            for (int i = 0; i < _n; i++)
            {
                if (_constrained[i])
                    solver.Add(i, i, 1.0);
                else if (massScale != 0)
                    solver.Add(i, i, massScale * _mass[i]);
            }
            solver.Factor();
            return solver;
        }

        /// <summary>Static equilibrium K u = F; velocity and acceleration reset to zero.</summary>
        public void SolveStatic(double[] force)
        {
            var solver = Assemble(1.0, 0.0);
            for (int i = 0; i < _n; i++)
                _rhs[i] = _constrained[i] ? 0 : force[i];
            solver.Solve(_rhs, U);
            Array.Clear(V, 0, _n);
            Array.Clear(A, 0, _n);
        }

        /// <summary>Sets the initial acceleration from equilibrium: M a0 = F - C v0 - K u0.</summary>
        public void InitializeAcceleration(double[] force)
        {
            MultiplyK(U, _kx);
            MultiplyK(V, _x);
            for (int i = 0; i < _n; i++)
            {
                if (_constrained[i] || _mass[i] <= 0)
                {
                    A[i] = 0;
                    continue;
                }
                double damping = _alpha * _mass[i] * V[i] + _beta * _x[i];
                A[i] = (force[i] - _kx[i] - damping) / _mass[i];
            }
        }

        public void Step(double[] force, double dt)
        {
            if (!(dt > 0))
                throw new ArgumentOutOfRangeException(nameof(dt));
            double a0 = 1 / (BetaN * dt * dt), a1 = Gamma / (BetaN * dt), a2 = 1 / (BetaN * dt);
            double a3 = 1 / (2 * BetaN) - 1, a4 = Gamma / BetaN - 1, a5 = dt / 2 * (Gamma / BetaN - 2);

            if (_solver == null || dt != _factoredDt)
            {
                _solver = Assemble(1 + a1 * _beta, a0 + a1 * _alpha);
                _factoredDt = dt;
            }

            for (int i = 0; i < _n; i++)
                _x[i] = a1 * U[i] + a4 * V[i] + a5 * A[i];
            MultiplyK(_x, _kx);
            for (int i = 0; i < _n; i++)
            {
                if (_constrained[i])
                {
                    _rhs[i] = 0;
                    continue;
                }
                double inertia = _mass[i] * (a0 * U[i] + a2 * V[i] + a3 * A[i]);
                double damping = _alpha * _mass[i] * _x[i] + _beta * _kx[i];
                _rhs[i] = force[i] + inertia + damping;
            }
            _solver.Solve(_rhs, _next);

            for (int i = 0; i < _n; i++)
            {
                double aNew = a0 * (_next[i] - U[i]) - a2 * V[i] - a3 * A[i];
                V[i] += dt * ((1 - Gamma) * A[i] + Gamma * aNew);
                A[i] = aNew;
                U[i] = _next[i];
            }
        }

        /// <summary>Rayleigh coefficients giving damping ratio zeta at two frequencies (Hz).</summary>
        public static (double alpha, double beta) Rayleigh(double zeta, double lowHz, double highHz)
        {
            double w1 = 2 * Math.PI * lowHz, w2 = 2 * Math.PI * highHz;
            return (2 * zeta * w1 * w2 / (w1 + w2), 2 * zeta / (w1 + w2));
        }
    }

    /// <summary>
    /// The bridge in time (spec 2.7, 2.8, 2.12): assembles FrameElement stiffness with reverse Cuthill-McKee
    /// ordering into a banded system, steps it with Newmark, checks every member with MemberCapacity, and
    /// removes failed members (worst first, a few per step) so load redistributes. Nodes that lose every path
    /// to a support are reported as detached. Bridge-world units; node and member order follow SolverInput.
    /// </summary>
    public sealed class FrameDynamics : IFrameIntegrator
    {
        readonly SolverInput _input;
        readonly Func<int, int, ILinearSolver> _factory;
        readonly double _alpha, _beta;
        readonly int _n, _m;
        readonly int[] _a, _b;
        readonly bool[] _fixed;
        readonly SectionProperties[] _section;
        readonly double[] _length;
        readonly double[][] _localK;
        readonly Mat3[] _axes;
        readonly double[][] _worldK;
        readonly double[] _nodeMass, _nodeRotary;
        readonly List<int>[] _nodeMembers;
        readonly double[] _local = new double[12];
        readonly double[] _end = new double[12];

        public Vec3[] Rest { get; }                       // bridge world
        public double[] BucklingLength { get; }
        public bool[] Active { get; }                     // not broken
        public bool[] InFrame { get; }                    // active and attached to a support
        public bool[] NodeInFrame { get; }
        public MemberForce[] Forces { get; }
        public double[] Utilization { get; }
        public FailureMode[] Mode { get; }
        public FailureMode[] BrokenMode { get; }
        public double[] BucklingUtilization { get; }
        public double[] U { get; }                        // 6 per node, bridge world
        public double[] Vel { get; }
        public double[] Acc { get; }
        public double Scale { get; }

        NewmarkSystem _system;
        int[] _slot;                                      // node index -> first equation, -1 if not in frame
        bool _dirty = true;

        public int NodeCount => _n;
        public int Equations => _system?.Size ?? 0;
        public int HalfBandwidth { get; private set; }

        public FrameDynamics(SolverInput input, Func<int, int, ILinearSolver> factory = null, double dampingRatio = Constants.Dynamics.DampingRatio)
        {
            _input = input;
            _factory = factory;
            (_alpha, _beta) = NewmarkSystem.Rayleigh(dampingRatio, Constants.Dynamics.RayleighLowHz, Constants.Dynamics.RayleighHighHz);
            Scale = input.ScaleFactor > 0 ? input.ScaleFactor : 1.0;
            _n = input.Nodes.Count;
            _m = input.Members.Count;

            var index = new Dictionary<int, int>();
            Rest = new Vec3[_n];
            _fixed = new bool[_n];
            for (int i = 0; i < _n; i++)
            {
                index[input.Nodes[i].Id] = i;
                Rest[i] = input.Nodes[i].Position * Scale;
                _fixed[i] = input.Nodes[i].Fixed;
            }
            var sections = new Dictionary<int, SectionProperties>();
            foreach (var s in input.Sections)
                sections[s.Id] = s;

            _a = new int[_m];
            _b = new int[_m];
            _section = new SectionProperties[_m];
            _length = new double[_m];
            _localK = new double[_m][];
            _axes = new Mat3[_m];
            _worldK = new double[_m][];
            _nodeMass = new double[_n];
            _nodeRotary = new double[_n];
            _nodeMembers = new List<int>[_n];
            for (int i = 0; i < _n; i++)
                _nodeMembers[i] = new List<int>();

            Active = new bool[_m];
            InFrame = new bool[_m];
            NodeInFrame = new bool[_n];
            Forces = new MemberForce[_m];
            Utilization = new double[_m];
            Mode = new FailureMode[_m];
            BrokenMode = new FailureMode[_m];
            BucklingUtilization = new double[_m];
            U = new double[_n * 6];
            Vel = new double[_n * 6];
            Acc = new double[_n * 6];

            for (int k = 0; k < _m; k++)
            {
                var mem = input.Members[k];
                int a = index[mem.NodeA], b = index[mem.NodeB];
                _a[k] = a;
                _b[k] = b;
                _section[k] = sections[mem.SectionId];
                _length[k] = Vec3.Distance(Rest[a], Rest[b]);
                _localK[k] = FrameElement.LocalStiffness(_section[k], _length[k]);
                _axes[k] = FrameElement.LocalAxes(Rest[a], Rest[b]);
                _worldK[k] = FrameElement.WorldStiffness(_section[k], Rest[a], Rest[b]);
                _nodeMembers[a].Add(k);
                _nodeMembers[b].Add(k);
                Active[k] = true;
                Forces[k] = new MemberForce { MemberId = mem.Id };

                double mass = _section[k].Density * _section[k].A * _length[k];
                _nodeMass[a] += mass / 2;
                _nodeMass[b] += mass / 2;
                double rot = mass / 2 * _length[k] * _length[k] / 12;
                _nodeRotary[a] += rot;
                _nodeRotary[b] += rot;
            }
            BucklingLength = BucklingLengths.Compute(input, Scale);
        }

        public double NodeMass(int node) => _nodeMass[node];
        public int MemberNodeA(int k) => _a[k];
        public int MemberNodeB(int k) => _b[k];
        public bool NodeFixed(int node) => _fixed[node];
        public IReadOnlyList<int> MembersAt(int node) => _nodeMembers[node];


        /// <summary>
        /// Finds what is still attached to a support and rebuilds the system. Returns nodes that were attached
        /// before (or ever, the first time) and now are not.
        /// </summary>
        public List<int> Rebuild(bool firstTime)
        {
            var reached = new bool[_n];
            var queue = new Queue<int>();
            for (int i = 0; i < _n; i++)
            {
                if (!_fixed[i])
                    continue;
                foreach (int k in _nodeMembers[i])
                {
                    if (Active[k])
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
                foreach (int k in _nodeMembers[v])
                {
                    if (!Active[k])
                        continue;
                    int other = _a[k] == v ? _b[k] : _a[k];
                    if (!reached[other])
                    {
                        reached[other] = true;
                        queue.Enqueue(other);
                    }
                }
            }

            var detached = new List<int>();
            for (int i = 0; i < _n; i++)
            {
                if ((NodeInFrame[i] || firstTime) && !reached[i] && !_fixed[i])
                    detached.Add(i);
                NodeInFrame[i] = reached[i];
            }
            for (int k = 0; k < _m; k++)
            {
                InFrame[k] = Active[k] && reached[_a[k]] && reached[_b[k]];
                if (!InFrame[k])
                {
                    Utilization[k] = 0;
                    Mode[k] = FailureMode.None;
                }
            }
            BuildSystem();
            return detached;
        }

        void BuildSystem()
        {
            // Order the attached nodes by reverse Cuthill-McKee for a narrow band.
            var local = new int[_n];
            var nodes = new List<int>();
            for (int i = 0; i < _n; i++)
            {
                local[i] = -1;
                if (NodeInFrame[i])
                {
                    local[i] = nodes.Count;
                    nodes.Add(i);
                }
            }
            var edges = new List<(int, int)>();
            for (int k = 0; k < _m; k++)
            {
                if (InFrame[k])
                    edges.Add((local[_a[k]], local[_b[k]]));
            }
            var permutation = ReverseCuthillMcKee.Order(nodes.Count, edges);
            int width = nodes.Count > 0 ? ReverseCuthillMcKee.Bandwidth(permutation, edges) : 0;
            int dof = Constants.Solver.DofPerNode;

            _slot = new int[_n];
            for (int i = 0; i < _n; i++)
                _slot[i] = -1;
            for (int p = 0; p < permutation.Length; p++)
                _slot[nodes[permutation[p]]] = p * dof;

            int size = nodes.Count * dof;
            HalfBandwidth = width * dof + dof - 1;
            var mass = new double[size];
            var constrained = new bool[size];
            foreach (int i in nodes)
            {
                for (int d = 0; d < dof; d++)
                {
                    constrained[_slot[i] + d] = _fixed[i];
                    mass[_slot[i] + d] = d < 3 ? _nodeMass[i] : _nodeRotary[i];
                }
            }

            var stiffness = new List<(int, int, double)>();
            for (int k = 0; k < _m; k++)
            {
                if (!InFrame[k])
                    continue;
                var kg = _worldK[k];
                for (int r = 0; r < 12; r++)
                {
                    int gr = _slot[r < 6 ? _a[k] : _b[k]] + r % 6;
                    if (constrained[gr])
                        continue;
                    for (int c = 0; c < 12; c++)
                    {
                        int gc = _slot[c < 6 ? _a[k] : _b[k]] + c % 6;
                        if (gc > gr || constrained[gc] || kg[r * 12 + c] == 0)
                            continue;
                        stiffness.Add((gr, gc, kg[r * 12 + c]));
                    }
                }
            }

            _system = new NewmarkSystem(size, HalfBandwidth, mass, stiffness, _alpha, _beta, _factory, constrained);
            Gather(U, _system.U);
            Gather(Vel, _system.V);
            Gather(Acc, _system.A);
            _dirty = false;
        }

        void Gather(double[] full, double[] reduced)
        {
            for (int i = 0; i < _n; i++)
            {
                if (_slot[i] < 0)
                    continue;
                for (int d = 0; d < 6; d++)
                    reduced[_slot[i] + d] = _fixed[i] ? 0 : full[i * 6 + d];
            }
        }

        void Scatter(double[] reduced, double[] full)
        {
            for (int i = 0; i < _n; i++)
            {
                if (_slot[i] < 0)
                    continue;
                for (int d = 0; d < 6; d++)
                    full[i * 6 + d] = reduced[_slot[i] + d];
            }
        }

        double[] LoadVector(Vec3[] loads)
        {
            var f = new double[_system.Size];
            for (int i = 0; i < _n; i++)
            {
                if (_slot[i] < 0 || _fixed[i])
                    continue;
                f[_slot[i]] = loads[i].X;
                f[_slot[i] + 1] = loads[i].Y;
                f[_slot[i] + 2] = loads[i].Z;
            }
            return f;
        }

        /// <summary>Static equilibrium under these node loads (bridge-world newtons).</summary>
        public void SolveStatic(Vec3[] loads)
        {
            if (_dirty || _system == null)
                Rebuild(_system == null);
            _system.SolveStatic(LoadVector(loads));
            Scatter(_system.U, U);
            Array.Clear(Vel, 0, Vel.Length);
            Array.Clear(Acc, 0, Acc.Length);
            UpdateForces();
        }

        /// <summary>One Newmark step. Refactors only if the structure changed since the last step.</summary>
        public void Step(Vec3[] loads, double dt)
        {
            if (_dirty || _system == null)
                Rebuild(_system == null);
            _system.Step(LoadVector(loads), dt);
            Scatter(_system.U, U);
            Scatter(_system.V, Vel);
            Scatter(_system.A, Acc);
            UpdateForces();
        }

        void UpdateForces()
        {
            for (int k = 0; k < _m; k++)
            {
                if (!InFrame[k])
                    continue;
                ComputeEndForces(k, Forces[k]);
                var cap = MemberCapacity.Evaluate(Forces[k], _section[k], BucklingLength[k]);
                Forces[k].Utilization = cap.Utilization;
                Utilization[k] = cap.Utilization;
                Mode[k] = cap.Governing;
                BucklingUtilization[k] = cap.BucklingUtilization;
                if (!double.IsFinite(cap.Utilization))
                    throw new InvalidOperationException("Non-finite member force");
            }
        }

        void ComputeEndForces(int k, MemberForce f)
        {
            var r = _axes[k];
            int ia = _a[k] * 6, ib = _b[k] * 6;
            for (int blk = 0; blk < 4; blk++)
            {
                int src = (blk < 2 ? ia : ib) + (blk % 2) * 3;
                double x = U[src], y = U[src + 1], z = U[src + 2];
                _local[blk * 3] = r.M00 * x + r.M01 * y + r.M02 * z;
                _local[blk * 3 + 1] = r.M10 * x + r.M11 * y + r.M12 * z;
                _local[blk * 3 + 2] = r.M20 * x + r.M21 * y + r.M22 * z;
            }
            var kl = _localK[k];
            for (int i = 0; i < 12; i++)
            {
                double sum = 0;
                int row = i * 12;
                for (int j = 0; j < 12; j++)
                    sum += kl[row + j] * _local[j];
                _end[i] = sum;
            }
            f.Axial = _end[6];
            f.ShearLocalYAtA = _end[1];
            f.ShearLocalZAtA = _end[2];
            f.MomentLocalYAtA = _end[4];
            f.MomentLocalZAtA = _end[5];
            f.ShearLocalYAtB = _end[7];
            f.ShearLocalZAtB = _end[8];
            f.MomentLocalYAtB = _end[10];
            f.MomentLocalZAtB = _end[11];
        }

        /// <summary>
        /// Removes the worst failed members (utilization >= 1), at most maxPerStep. Returns their indices.
        /// The next Step refactors once.
        /// </summary>
        public List<int> RemoveFailures(int maxPerStep = Constants.Dynamics.MaxFailuresPerStep)
        {
            var failed = new List<int>();
            for (int k = 0; k < _m; k++)
            {
                if (InFrame[k] && Utilization[k] >= 1.0)
                    failed.Add(k);
            }
            failed.Sort((x, y) => Utilization[y].CompareTo(Utilization[x]));
            if (failed.Count > maxPerStep)
                failed.RemoveRange(maxPerStep, failed.Count - maxPerStep);
            foreach (int k in failed)
            {
                Active[k] = false;
                BrokenMode[k] = Mode[k];
            }
            if (failed.Count > 0)
                _dirty = true;
            return failed;
        }

        /// <summary>After RemoveFailures: rebuild now and report what fell off.</summary>
        public List<int> DetachAfterFailures() => _dirty ? Rebuild(false) : new List<int>();

        public Vec3 Displacement(int node) => new Vec3(U[node * 6], U[node * 6 + 1], U[node * 6 + 2]);
        public Vec3 Velocity(int node) => new Vec3(Vel[node * 6], Vel[node * 6 + 1], Vel[node * 6 + 2]);
        public Vec3 Position(int node) => Rest[node] + Displacement(node);
    }
}
