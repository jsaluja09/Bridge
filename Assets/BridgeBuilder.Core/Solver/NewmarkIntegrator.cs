using System;
using System.Collections.Generic;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Newmark constants for one timestep in Chopra's notation for linear systems, with beta and gamma
    /// from <see cref="Constants.Dynamics"/>. For mass m, damping c, and stiffness k, a step from
    /// (u, v, a) solves <c>keff * u1 = peff</c> with <c>keff = k + A0 * m + A1 * c</c> and
    /// <c>peff = p1 + m * (A0 * u + A2 * v + A3 * a) + c * (A1 * u + A4 * v + A5 * a)</c>, then sets
    /// <c>v1 = A1 * (u1 - u) - A4 * v - A5 * a</c> and <c>a1 = A0 * (u1 - u) - A2 * v - A3 * a</c>.
    /// </summary>
    public readonly struct NewmarkCoefficients
    {
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="dt"/> is not positive and finite, or so small that A0 overflows.</exception>
        public NewmarkCoefficients(double dt)
        {
            if (!(dt > 0.0) || double.IsInfinity(dt))
            {
                throw new ArgumentOutOfRangeException(nameof(dt), dt, "The timestep must be positive and finite.");
            }

            const double beta = Constants.Dynamics.NewmarkBeta;
            const double gamma = Constants.Dynamics.NewmarkGamma;
            Dt = dt;
            A0 = 1.0 / (beta * dt * dt);
            A1 = gamma / (beta * dt);
            A2 = 1.0 / (beta * dt);
            A3 = 1.0 / (2.0 * beta) - 1.0;
            A4 = gamma / beta - 1.0;
            A5 = dt * (gamma / (2.0 * beta) - 1.0);
            if (double.IsInfinity(A0))
            {
                throw new ArgumentOutOfRangeException(nameof(dt), dt, "The timestep is too small.");
            }
        }

        /// <summary>Timestep, s.</summary>
        public double Dt { get; }

        /// <summary><c>1 / (beta * dt^2)</c>.</summary>
        public double A0 { get; }

        /// <summary><c>gamma / (beta * dt)</c>.</summary>
        public double A1 { get; }

        /// <summary><c>1 / (beta * dt)</c>.</summary>
        public double A2 { get; }

        /// <summary><c>1 / (2 * beta) - 1</c>.</summary>
        public double A3 { get; }

        /// <summary><c>gamma / beta - 1</c>.</summary>
        public double A4 { get; }

        /// <summary><c>dt * (gamma / (2 * beta) - 1)</c>.</summary>
        public double A5 { get; }

        internal double InertiaTerm(double displacement, double velocity, double acceleration)
        {
            return A0 * displacement + A2 * velocity + A3 * acceleration;
        }

        internal double DampingTerm(double displacement, double velocity, double acceleration)
        {
            return A1 * displacement + A4 * velocity + A5 * acceleration;
        }

        internal double Velocity(double increment, double velocity, double acceleration)
        {
            return A1 * increment - A4 * velocity - A5 * acceleration;
        }

        internal double Acceleration(double increment, double velocity, double acceleration)
        {
            return A0 * increment - A2 * velocity - A3 * acceleration;
        }
    }

    /// <summary>Displacement, velocity, and acceleration of a single-degree-of-freedom oscillator.</summary>
    public readonly struct SingleDofState
    {
        public SingleDofState(double displacement, double velocity, double acceleration)
        {
            Displacement = displacement;
            Velocity = velocity;
            Acceleration = acceleration;
        }

        public double Displacement { get; }

        public double Velocity { get; }

        public double Acceleration { get; }
    }

    /// <summary>
    /// Implicit dynamics of a rigid-jointed frame with progressive member failure (spec 2.7, 2.8, and
    /// 2.12). Each step is one Newmark average-acceleration step of <c>M a + C v + K u = p</c>:
    /// <list type="bullet">
    /// <item>K assembles <see cref="FrameElement.WorldStiffness"/> of the members still standing.</item>
    /// <item>M is lumped. Each member's mass, density * A * L, is split equally between its end nodes,
    /// with rotary inertia m * L^2 / 24 at each end, which is half the member turning about its end
    /// node, (m / 2) * (L / 2)^2 / 3. It is applied about all three axes so M stays diagonal.</item>
    /// <item>C = (2 * zeta / omega1) * K is stiffness-proportional damping: ratio zeta in the
    /// fundamental mode, more in higher modes, and none on the rigid-body motion of a collapsed piece.
    /// omega1 is <see cref="FundamentalFrequency"/>, estimated once at reset.</item>
    /// </list>
    /// Equations are numbered node by node in reverse Cuthill-McKee order and assembled into an
    /// <see cref="ILinearSolver"/> from the factory. The system is factored at reset, on the first
    /// step, when a member fails, and when dt or the damping ratio changes; every other step is one
    /// back-substitution. After each solve every member is checked with <see cref="MemberCapacity"/>.
    /// Members at utilization 1 or more are removed, the system is refactored, and the step is solved
    /// again from the same starting state, until no member fails. A node left with no member keeps its
    /// last displacement and leaves the system. Positions and results are in the units of the input,
    /// bridge-world after <see cref="RoomBridgeAdapter.ToBridge"/>; <see cref="SolverInput.ScaleFactor"/>
    /// is not applied here. Only the loads passed in act on the bridge, so self-weight and wheel loads
    /// come from the caller. Fills are not read: their hidden bracing arrives as ordinary members.
    /// </summary>
    public sealed class NewmarkIntegrator
    {
        private const int DofPerNode = Constants.Solver.DofPerNode;
        private const int ElementDofCount = FrameElement.DofCount;

        private readonly Func<int, int, ILinearSolver> createSolver;

        private bool ready;
        private int nodeCount;
        private Dictionary<int, int> nodeIndexById;
        private Member[] members;
        private int[][] memberDofs;
        private Vec3[] memberEndA;
        private Vec3[] memberEndB;
        private SectionProperties[] memberSections;
        private double[] memberLengths;
        private double[] memberMasses;
        private double[][,] memberStiffness;
        private bool[] memberActive;
        private int[] bandOrder;

        private int[] equationOfDof;
        private int equationCount;
        private int halfBandwidth;
        private double[] lumpedMass;

        private double[] displacement;
        private double[] velocity;
        private double[] acceleration;

        private ILinearSolver effectiveSystem;
        private double effectiveDt;
        private double effectiveStiffnessDamping;

        /// <summary>Uses <see cref="BandedMatrix"/> for every factorization.</summary>
        public NewmarkIntegrator()
            : this((order, halfBandwidth) => new BandedMatrix(order, halfBandwidth))
        {
        }

        /// <param name="createSolver">
        /// Returns a new, empty system for (order, half-bandwidth). It is called once per factorization,
        /// because <see cref="ILinearSolver"/> cannot be cleared.
        /// </param>
        public NewmarkIntegrator(Func<int, int, ILinearSolver> createSolver)
        {
            this.createSolver = createSolver ?? throw new ArgumentNullException(nameof(createSolver));
        }

        /// <summary>One step at <see cref="Constants.Dynamics.SolverRateHz"/>, s.</summary>
        public static double DefaultTimestep => 1.0 / Constants.Dynamics.SolverRateHz;

        /// <summary>
        /// Fundamental circular frequency of the structure at reset, rad/s, by Rayleigh's method: the
        /// static deflection under a uniform acceleration along each global axis, keeping the lowest
        /// quotient. It is an upper bound on the true value. 0 when no node can move.
        /// </summary>
        public double FundamentalFrequency { get; private set; }

        /// <summary>The ratio <c>c / (2 * sqrt(k * m))</c> turned into a viscous damping coefficient c.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The mass is not positive and finite, the stiffness is negative or not finite, or the damping ratio is negative or not finite.</exception>
        public static double DampingCoefficient(double mass, double stiffness, double dampingRatio)
        {
            RequirePositive(mass, nameof(mass), "The mass");
            RequireNonNegative(stiffness, nameof(stiffness), "The stiffness");
            RequireNonNegative(dampingRatio, nameof(dampingRatio), "The damping ratio");
            return 2.0 * dampingRatio * Math.Sqrt(stiffness * mass);
        }

        /// <summary>
        /// One Newmark step of the oscillator <c>m * a + c * v + k * u = force</c>, with c from
        /// <see cref="DampingCoefficient"/>. <paramref name="force"/> acts at the end of the step.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">An argument is out of range or not finite.</exception>
        /// <exception cref="InvalidOperationException">The step overflowed.</exception>
        public static SingleDofState StepSingleDof(double mass, double stiffness, double dampingRatio, double force, SingleDofState state, double dt)
        {
            var coefficients = new NewmarkCoefficients(dt);
            double damping = DampingCoefficient(mass, stiffness, dampingRatio);
            RequireFinite(force, nameof(force), "The force");
            RequireFinite(state.Displacement, nameof(state), "The displacement");
            RequireFinite(state.Velocity, nameof(state), "The velocity");
            RequireFinite(state.Acceleration, nameof(state), "The acceleration");

            double effectiveStiffness = stiffness + coefficients.A0 * mass + coefficients.A1 * damping;
            double effectiveLoad = force
                + mass * coefficients.InertiaTerm(state.Displacement, state.Velocity, state.Acceleration)
                + damping * coefficients.DampingTerm(state.Displacement, state.Velocity, state.Acceleration);
            double displacement = effectiveLoad / effectiveStiffness;
            double increment = displacement - state.Displacement;
            var next = new SingleDofState(
                displacement,
                coefficients.Velocity(increment, state.Velocity, state.Acceleration),
                coefficients.Acceleration(increment, state.Velocity, state.Acceleration));
            if (!double.IsFinite(next.Displacement) || !double.IsFinite(next.Velocity) || !double.IsFinite(next.Acceleration))
            {
                throw new InvalidOperationException("The single-degree-of-freedom step produced a non-finite value.");
            }

            return next;
        }

        /// <summary>Starts a run at rest and unloaded, with every member standing.</summary>
        /// <exception cref="ArgumentException">The input is inconsistent, or a section is invalid or has no positive density.</exception>
        /// <exception cref="InvalidOperationException">The structure is a mechanism.</exception>
        public void Reset(SolverInput input)
        {
            Reset(input, Array.Empty<NodalLoad>());
        }

        /// <summary>
        /// Starts a run with every member standing, at rest in static equilibrium under
        /// <paramref name="initialLoads"/>, so stepping with the same loads keeps the bridge still.
        /// The input is read, never modified, and later changes to it are not seen.
        /// </summary>
        /// <exception cref="ArgumentException">The input is inconsistent, a section is invalid or has no positive density, or a load is not finite or names a missing node.</exception>
        /// <exception cref="InvalidOperationException">The structure is a mechanism.</exception>
        public void Reset(SolverInput input, NodalLoad[] initialLoads)
        {
            ready = false;
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (initialLoads == null)
            {
                throw new ArgumentNullException(nameof(initialLoads));
            }

            Node[] nodes = input.Nodes ?? throw new ArgumentException("Nodes must not be null.", nameof(input));
            Member[] inputMembers = input.Members ?? throw new ArgumentException("Members must not be null.", nameof(input));
            Dictionary<int, int> indexById = IndexNodes(nodes);
            Dictionary<int, SectionProperties> sectionsById = IndexSections(input.Sections);

            int memberCount = inputMembers.Length;
            var dofs = new int[memberCount][];
            var endA = new Vec3[memberCount];
            var endB = new Vec3[memberCount];
            var sections = new SectionProperties[memberCount];
            var lengths = new double[memberCount];
            var masses = new double[memberCount];
            var stiffness = new double[memberCount][,];
            for (int m = 0; m < memberCount; m++)
            {
                Member member = inputMembers[m] ?? throw new ArgumentException("Members must not contain null.", nameof(input));
                int nodeA = RequireNode(indexById, member.NodeA, member);
                int nodeB = RequireNode(indexById, member.NodeB, member);
                if (!sectionsById.TryGetValue(member.SectionId, out SectionProperties section))
                {
                    throw new ArgumentException("Member " + Format(member.Id) + " references missing section " + Format(member.SectionId) + ".", nameof(input));
                }

                endA[m] = nodes[nodeA].Position;
                endB[m] = nodes[nodeB].Position;
                stiffness[m] = FrameElement.WorldStiffness(endA[m], endB[m], section);
                lengths[m] = Vec3.Distance(endA[m], endB[m]);
                MemberCapacity.Evaluate(0.0, 0.0, 0.0, section, lengths[m]);
                masses[m] = section.Density * section.A * lengths[m];
                if (!(masses[m] > 0.0) || double.IsInfinity(masses[m]))
                {
                    throw new ArgumentException("Section " + Format(section.Id) + " needs a positive, finite density for dynamics.", nameof(input));
                }

                sections[m] = section;
                dofs[m] = ElementDofs(nodeA, nodeB);
            }

            nodeCount = nodes.Length;
            nodeIndexById = indexById;
            members = inputMembers;
            memberDofs = dofs;
            memberEndA = endA;
            memberEndB = endB;
            memberSections = sections;
            memberLengths = lengths;
            memberMasses = masses;
            memberStiffness = stiffness;
            memberActive = new bool[memberCount];
            for (int m = 0; m < memberCount; m++)
            {
                memberActive[m] = true;
            }

            bandOrder = BandOrder(nodes, dofs);
            double[] initialLoad = LoadVector(initialLoads, nameof(initialLoads));
            int dofCount = nodeCount * DofPerNode;
            displacement = new double[dofCount];
            velocity = new double[dofCount];
            acceleration = new double[dofCount];
            RebuildEquations();

            FundamentalFrequency = 0.0;
            if (equationCount > 0)
            {
                ILinearSolver stiffnessSystem = Assemble(1.0, 0.0);
                FundamentalFrequency = EstimateFundamentalFrequency(stiffnessSystem);
                SolveStatic(stiffnessSystem, initialLoad);
            }

            ready = true;
        }

        /// <summary>Advances one step with damping ratio <see cref="Constants.Dynamics.DampingRatio"/>.</summary>
        /// <exception cref="ArgumentException">An argument is invalid; see <see cref="Step(NodalLoad[], double, double)"/>.</exception>
        /// <exception cref="InvalidOperationException"><see cref="Reset(SolverInput)"/> has not succeeded, or the step produced a non-finite value.</exception>
        public StepOutput Step(NodalLoad[] loads, double dt)
        {
            return Step(loads, dt, Constants.Dynamics.DampingRatio);
        }

        /// <summary>
        /// Advances <paramref name="dt"/> seconds with <paramref name="loads"/> acting at the end of the
        /// step, removing every member that fails. The output lists the standing members' forces with
        /// their utilization and the ids removed during this step; <see cref="StepOutput.CarState"/> is null.
        /// </summary>
        /// <param name="loads">External nodal loads at the end of the step. A load on a fixed node, or on a node with no member left, goes nowhere.</param>
        /// <param name="dt">Timestep, s.</param>
        /// <param name="dampingRatio">Fraction of critical damping in the fundamental mode.</param>
        /// <exception cref="ArgumentNullException"><paramref name="loads"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="dt"/> is not positive and finite, or <paramref name="dampingRatio"/> is negative or not finite.</exception>
        /// <exception cref="ArgumentException">A load is null, not finite, or names a missing node.</exception>
        /// <exception cref="InvalidOperationException">
        /// <see cref="Reset(SolverInput)"/> has not succeeded, or the step produced a non-finite value.
        /// After this exception the integrator needs a new reset.
        /// </exception>
        public StepOutput Step(NodalLoad[] loads, double dt, double dampingRatio)
        {
            if (!ready)
            {
                throw new InvalidOperationException("Reset the integrator before stepping it.");
            }

            if (loads == null)
            {
                throw new ArgumentNullException(nameof(loads));
            }

            var coefficients = new NewmarkCoefficients(dt);
            RequireNonNegative(dampingRatio, nameof(dampingRatio), "The damping ratio");
            double stiffnessDamping = FundamentalFrequency > 0.0 ? 2.0 * dampingRatio / FundamentalFrequency : 0.0;
            double[] load = LoadVector(loads, nameof(loads));

            try
            {
                var failedIds = new List<int>();
                while (true)
                {
                    EnsureEffectiveSystem(coefficients, stiffnessDamping);
                    double[] next = SolveStep(coefficients, stiffnessDamping, load);
                    List<MemberForce> forces = EvaluateMembers(next, out List<int> failedMembers);
                    if (failedMembers.Count == 0)
                    {
                        Commit(coefficients, next);
                        return Output(forces, failedIds);
                    }

                    foreach (int m in failedMembers)
                    {
                        memberActive[m] = false;
                        failedIds.Add(members[m].Id);
                    }

                    RebuildEquations();
                }
            }
            catch
            {
                ready = false;
                throw;
            }
        }

        private void RebuildEquations()
        {
            int dofCount = nodeCount * DofPerNode;
            var mass = new double[dofCount];
            var connected = new bool[nodeCount];
            for (int m = 0; m < members.Length; m++)
            {
                if (!memberActive[m])
                {
                    continue;
                }

                double half = 0.5 * memberMasses[m];
                double rotary = memberMasses[m] * memberLengths[m] * memberLengths[m] / 24.0;
                foreach (int first in new[] { memberDofs[m][0], memberDofs[m][DofPerNode] })
                {
                    for (int dof = 0; dof < 3; dof++)
                    {
                        mass[first + dof] += half;
                        mass[first + 3 + dof] += rotary;
                    }

                    connected[first / DofPerNode] = true;
                }
            }

            var equations = new int[dofCount];
            for (int dof = 0; dof < dofCount; dof++)
            {
                equations[dof] = -1;
            }

            int count = 0;
            foreach (int node in bandOrder)
            {
                if (connected[node])
                {
                    for (int dof = 0; dof < DofPerNode; dof++)
                    {
                        equations[node * DofPerNode + dof] = count++;
                    }
                }
            }

            int bandwidth = 0;
            for (int m = 0; m < members.Length; m++)
            {
                if (!memberActive[m])
                {
                    continue;
                }

                int low = int.MaxValue;
                int high = -1;
                foreach (int dof in memberDofs[m])
                {
                    int equation = equations[dof];
                    if (equation >= 0)
                    {
                        low = Math.Min(low, equation);
                        high = Math.Max(high, equation);
                    }
                }

                if (high >= 0)
                {
                    bandwidth = Math.Max(bandwidth, high - low);
                }
            }

            lumpedMass = mass;
            equationOfDof = equations;
            equationCount = count;
            halfBandwidth = bandwidth;
            effectiveSystem = null;
        }

        /// <summary>Assembles <c>stiffnessScale * K + massScale * M</c> over the standing members and factors it.</summary>
        private ILinearSolver Assemble(double stiffnessScale, double massScale)
        {
            ILinearSolver system = createSolver(equationCount, halfBandwidth)
                ?? throw new InvalidOperationException("The solver factory returned null.");
            for (int m = 0; m < members.Length; m++)
            {
                if (!memberActive[m])
                {
                    continue;
                }

                double[,] k = memberStiffness[m];
                int[] dofs = memberDofs[m];
                for (int row = 0; row < ElementDofCount; row++)
                {
                    int equationRow = equationOfDof[dofs[row]];
                    if (equationRow < 0)
                    {
                        continue;
                    }

                    for (int column = 0; column < ElementDofCount; column++)
                    {
                        int equationColumn = equationOfDof[dofs[column]];
                        if (equationColumn >= 0 && k[row, column] != 0.0)
                        {
                            system.Add(equationRow, equationColumn, stiffnessScale * k[row, column]);
                        }
                    }
                }
            }

            if (massScale != 0.0)
            {
                for (int dof = 0; dof < equationOfDof.Length; dof++)
                {
                    int equation = equationOfDof[dof];
                    if (equation >= 0)
                    {
                        system.Add(equation, equation, massScale * lumpedMass[dof]);
                    }
                }
            }

            system.Factor();
            return system;
        }

        private void EnsureEffectiveSystem(NewmarkCoefficients coefficients, double stiffnessDamping)
        {
            if (equationCount == 0
                || (effectiveSystem != null && effectiveDt == coefficients.Dt && effectiveStiffnessDamping == stiffnessDamping))
            {
                return;
            }

            effectiveSystem = null;
            effectiveSystem = Assemble(1.0 + coefficients.A1 * stiffnessDamping, coefficients.A0);
            effectiveDt = coefficients.Dt;
            effectiveStiffnessDamping = stiffnessDamping;
        }

        private double EstimateFundamentalFrequency(ILinearSolver stiffnessSystem)
        {
            double lowest = double.PositiveInfinity;
            var load = new double[equationCount];
            var deflection = new double[equationCount];
            for (int axis = 0; axis < 3; axis++)
            {
                for (int dof = 0; dof < equationOfDof.Length; dof++)
                {
                    int equation = equationOfDof[dof];
                    if (equation >= 0)
                    {
                        load[equation] = dof % DofPerNode == axis ? lumpedMass[dof] : 0.0;
                    }
                }

                stiffnessSystem.Solve(load, deflection);
                double work = 0.0;
                double inertia = 0.0;
                for (int dof = 0; dof < equationOfDof.Length; dof++)
                {
                    int equation = equationOfDof[dof];
                    if (equation >= 0)
                    {
                        work += deflection[equation] * load[equation];
                        inertia += lumpedMass[dof] * deflection[equation] * deflection[equation];
                    }
                }

                double quotient = work / inertia;
                if (!(quotient > 0.0) || double.IsInfinity(quotient))
                {
                    throw new InvalidOperationException("The stiffness matrix is not positive definite, so the structure is a mechanism.");
                }

                lowest = Math.Min(lowest, quotient);
            }

            return Math.Sqrt(lowest);
        }

        private void SolveStatic(ILinearSolver stiffnessSystem, double[] load)
        {
            var rhs = new double[equationCount];
            bool loaded = false;
            for (int dof = 0; dof < equationOfDof.Length; dof++)
            {
                int equation = equationOfDof[dof];
                if (equation >= 0)
                {
                    rhs[equation] = load[dof];
                    loaded |= load[dof] != 0.0;
                }
            }

            if (!loaded)
            {
                return;
            }

            var solution = new double[equationCount];
            stiffnessSystem.Solve(rhs, solution);
            for (int dof = 0; dof < equationOfDof.Length; dof++)
            {
                int equation = equationOfDof[dof];
                if (equation >= 0)
                {
                    if (!double.IsFinite(solution[equation]))
                    {
                        throw new InvalidOperationException("The initial static solve produced a non-finite displacement.");
                    }

                    displacement[dof] = solution[equation];
                }
            }
        }

        /// <summary>Displacements at the end of the step for every degree of freedom, from the committed state.</summary>
        private double[] SolveStep(NewmarkCoefficients coefficients, double stiffnessDamping, double[] load)
        {
            var next = (double[])displacement.Clone();
            if (equationCount == 0)
            {
                return next;
            }

            var rhs = new double[equationCount];
            for (int dof = 0; dof < equationOfDof.Length; dof++)
            {
                int equation = equationOfDof[dof];
                if (equation >= 0)
                {
                    rhs[equation] = load[dof] + lumpedMass[dof] * coefficients.InertiaTerm(displacement[dof], velocity[dof], acceleration[dof]);
                }
            }

            if (stiffnessDamping != 0.0)
            {
                var dampingTerm = new double[equationOfDof.Length];
                for (int dof = 0; dof < dampingTerm.Length; dof++)
                {
                    dampingTerm[dof] = coefficients.DampingTerm(displacement[dof], velocity[dof], acceleration[dof]);
                }

                for (int m = 0; m < members.Length; m++)
                {
                    if (!memberActive[m])
                    {
                        continue;
                    }

                    double[,] k = memberStiffness[m];
                    int[] dofs = memberDofs[m];
                    for (int row = 0; row < ElementDofCount; row++)
                    {
                        int equation = equationOfDof[dofs[row]];
                        if (equation < 0)
                        {
                            continue;
                        }

                        double sum = 0.0;
                        for (int column = 0; column < ElementDofCount; column++)
                        {
                            sum += k[row, column] * dampingTerm[dofs[column]];
                        }

                        rhs[equation] += stiffnessDamping * sum;
                    }
                }
            }

            var solution = new double[equationCount];
            effectiveSystem.Solve(rhs, solution);
            for (int dof = 0; dof < equationOfDof.Length; dof++)
            {
                int equation = equationOfDof[dof];
                if (equation >= 0)
                {
                    if (!double.IsFinite(solution[equation]))
                    {
                        throw new InvalidOperationException("The dynamic step produced a non-finite displacement.");
                    }

                    next[dof] = solution[equation];
                }
            }

            return next;
        }

        /// <summary>Forces of the standing members that hold, in member order; members at utilization 1 or more go to <paramref name="failedMembers"/> instead.</summary>
        private List<MemberForce> EvaluateMembers(double[] next, out List<int> failedMembers)
        {
            var forces = new List<MemberForce>(members.Length);
            failedMembers = new List<int>();
            var elementDisplacements = new double[ElementDofCount];
            for (int m = 0; m < members.Length; m++)
            {
                if (!memberActive[m])
                {
                    continue;
                }

                int[] dofs = memberDofs[m];
                for (int i = 0; i < ElementDofCount; i++)
                {
                    elementDisplacements[i] = next[dofs[i]];
                }

                MemberForce force = FrameElement.EndForces(members[m].Id, memberEndA[m], memberEndB[m], memberSections[m], elementDisplacements);
                if (!IsFinite(force))
                {
                    throw new InvalidOperationException("The dynamic step produced a non-finite force in member " + Format(members[m].Id) + ".");
                }

                MemberCapacityResult capacity = MemberCapacity.Evaluate(force, memberSections[m], memberLengths[m]);
                if (!double.IsFinite(capacity.Utilization))
                {
                    throw new InvalidOperationException("The dynamic step produced a non-finite utilization in member " + Format(members[m].Id) + ".");
                }

                force.Utilization = capacity.Utilization;
                if (capacity.Failed)
                {
                    failedMembers.Add(m);
                }
                else
                {
                    forces.Add(force);
                }
            }

            return forces;
        }

        private void Commit(NewmarkCoefficients coefficients, double[] next)
        {
            var nextVelocity = new double[next.Length];
            var nextAcceleration = new double[next.Length];
            for (int dof = 0; dof < next.Length; dof++)
            {
                if (equationOfDof[dof] < 0)
                {
                    continue;
                }

                double increment = next[dof] - displacement[dof];
                nextVelocity[dof] = coefficients.Velocity(increment, velocity[dof], acceleration[dof]);
                nextAcceleration[dof] = coefficients.Acceleration(increment, velocity[dof], acceleration[dof]);
                if (!double.IsFinite(nextVelocity[dof]) || !double.IsFinite(nextAcceleration[dof]))
                {
                    throw new InvalidOperationException("The dynamic step produced a non-finite velocity or acceleration.");
                }
            }

            displacement = next;
            velocity = nextVelocity;
            acceleration = nextAcceleration;
        }

        private StepOutput Output(List<MemberForce> forces, List<int> failedIds)
        {
            var output = new StepOutput
            {
                Displacements = new Vec3[nodeCount],
                Rotations = new Vec3[nodeCount],
                MemberForces = forces.ToArray(),
                FailedMemberIds = failedIds.ToArray(),
            };
            for (int node = 0; node < nodeCount; node++)
            {
                int first = node * DofPerNode;
                output.Displacements[node] = new Vec3(displacement[first], displacement[first + 1], displacement[first + 2]);
                output.Rotations[node] = new Vec3(displacement[first + 3], displacement[first + 4], displacement[first + 5]);
            }

            return output;
        }

        private double[] LoadVector(NodalLoad[] loads, string paramName)
        {
            var vector = new double[nodeCount * DofPerNode];
            foreach (NodalLoad load in loads)
            {
                if (load == null)
                {
                    throw new ArgumentException("Loads must not contain null.", paramName);
                }

                if (!FrameElement.IsFinite(load.Force) || !FrameElement.IsFinite(load.Moment))
                {
                    throw new ArgumentException("Load on node " + Format(load.NodeId) + " has a NaN or infinite component.", paramName);
                }

                if (!nodeIndexById.TryGetValue(load.NodeId, out int node))
                {
                    throw new ArgumentException("Load references missing node " + Format(load.NodeId) + ".", paramName);
                }

                int first = node * DofPerNode;
                vector[first] += load.Force.X;
                vector[first + 1] += load.Force.Y;
                vector[first + 2] += load.Force.Z;
                vector[first + 3] += load.Moment.X;
                vector[first + 4] += load.Moment.Y;
                vector[first + 5] += load.Moment.Z;
            }

            return vector;
        }

        /// <summary>Free node indices in reverse Cuthill-McKee order over the members joining free nodes.</summary>
        private static int[] BandOrder(Node[] nodes, int[][] memberDofs)
        {
            var freeNodes = new List<int>();
            var vertexOfNode = new int[nodes.Length];
            for (int node = 0; node < nodes.Length; node++)
            {
                vertexOfNode[node] = nodes[node].Fixed ? -1 : freeNodes.Count;
                if (!nodes[node].Fixed)
                {
                    freeNodes.Add(node);
                }
            }

            var edges = new List<(int A, int B)>();
            foreach (int[] dofs in memberDofs)
            {
                int a = vertexOfNode[dofs[0] / DofPerNode];
                int b = vertexOfNode[dofs[DofPerNode] / DofPerNode];
                if (a >= 0 && b >= 0)
                {
                    edges.Add((a, b));
                }
            }

            int[] permutation = ReverseCuthillMcKee.Compute(freeNodes.Count, edges);
            var order = new int[permutation.Length];
            for (int newIndex = 0; newIndex < permutation.Length; newIndex++)
            {
                order[newIndex] = freeNodes[permutation[newIndex]];
            }

            return order;
        }

        private static int[] ElementDofs(int nodeA, int nodeB)
        {
            var dofs = new int[ElementDofCount];
            for (int dof = 0; dof < DofPerNode; dof++)
            {
                dofs[dof] = nodeA * DofPerNode + dof;
                dofs[DofPerNode + dof] = nodeB * DofPerNode + dof;
            }

            return dofs;
        }

        private static Dictionary<int, int> IndexNodes(Node[] nodes)
        {
            var indexById = new Dictionary<int, int>(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                Node node = nodes[i] ?? throw new ArgumentException("Nodes must not contain null.", "input");
                if (!FrameElement.IsFinite(node.Position))
                {
                    throw new ArgumentException("Node " + Format(node.Id) + " has a non-finite position.", "input");
                }

                if (indexById.ContainsKey(node.Id))
                {
                    throw new ArgumentException("Node id " + Format(node.Id) + " is repeated.", "input");
                }

                indexById.Add(node.Id, i);
            }

            return indexById;
        }

        private static Dictionary<int, SectionProperties> IndexSections(SectionProperties[] sections)
        {
            if (sections == null)
            {
                throw new ArgumentException("Sections must not be null.", "input");
            }

            var byId = new Dictionary<int, SectionProperties>(sections.Length);
            foreach (SectionProperties section in sections)
            {
                if (section == null)
                {
                    throw new ArgumentException("Sections must not contain null.", "input");
                }

                if (byId.ContainsKey(section.Id))
                {
                    throw new ArgumentException("Section id " + Format(section.Id) + " is repeated.", "input");
                }

                byId.Add(section.Id, section);
            }

            return byId;
        }

        private static int RequireNode(Dictionary<int, int> nodeIndexById, int nodeId, Member member)
        {
            if (!nodeIndexById.TryGetValue(nodeId, out int index))
            {
                throw new ArgumentException("Member " + Format(member.Id) + " references missing node " + Format(nodeId) + ".", "input");
            }

            return index;
        }

        private static bool IsFinite(MemberForce force)
        {
            return double.IsFinite(force.Axial)
                && double.IsFinite(force.ShearLocalYAtA) && double.IsFinite(force.ShearLocalZAtA)
                && double.IsFinite(force.MomentLocalYAtA) && double.IsFinite(force.MomentLocalZAtA)
                && double.IsFinite(force.ShearLocalYAtB) && double.IsFinite(force.ShearLocalZAtB)
                && double.IsFinite(force.MomentLocalYAtB) && double.IsFinite(force.MomentLocalZAtB);
        }

        private static void RequireFinite(double value, string paramName, string description)
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(paramName, value, description + " must be finite.");
            }
        }

        private static void RequirePositive(double value, string paramName, string description)
        {
            if (!(value > 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(paramName, value, description + " must be positive and finite.");
            }
        }

        private static void RequireNonNegative(double value, string paramName, string description)
        {
            if (!(value >= 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(paramName, value, description + " must be finite and not negative.");
            }
        }

        private static string Format(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
    }
}
