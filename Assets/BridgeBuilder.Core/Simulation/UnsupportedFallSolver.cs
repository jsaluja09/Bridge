using System;
using System.Collections.Generic;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// A dynamic solver that drops what is not supported (spec 2.8 and 11.1), so a line that touches
    /// nothing falls when tested instead of stopping the run as a mechanism. The wrapped solver, normally
    /// the lagged car coupling, runs only <see cref="SupportPartition.Split"/>'s supported input, and its
    /// results are mapped back to the full input unchanged. Every unsupported piece falls as a rigid
    /// body from rest: after <see cref="ElapsedSeconds"/> t, each of its nodes has moved
    /// <c>-0.5 * g * t^2</c> bridge-world meters along Y with no rotation, where g is
    /// <see cref="Constants.Loads.Gravity"/>. Its members carry no force and never fail. Falling pieces
    /// collide with nothing and take no wheel loads (HG-fall). Runs on the calling thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Falling displacements are in the length units of the input given to <see cref="Reset"/>: the
    /// bridge-world drop divided by its <see cref="SolverInput.ScaleFactor"/>. For a room input that is room
    /// units, as <see cref="CoupledStep"/> reports; for a bridge-world input from
    /// <see cref="RoomBridgeAdapter.ToBridge(SolverInput)"/> it is bridge-world meters, as
    /// <see cref="NewmarkIntegrator"/> reports. A free node with no member belongs to no piece and stays
    /// at zero displacement.
    /// </para>
    /// <para>
    /// Cancelling is decided once per step, before anything moves: the wrapped solver is handed a cancel
    /// callback that always returns false, so the wrapped step and the fall clock always advance together.
    /// A cancel that arrives during a step is honoured on the next call.
    /// </para>
    /// </remarks>
    public sealed class UnsupportedFallSolver : IDynamicSolver
    {
        private static readonly Func<bool> NeverCancel = () => false;

        private readonly IDynamicSolver solver;

        private bool ready;
        private int nodeCount;
        private int[] memberIds;
        private bool[] memberSupported;
        private HashSet<int> supportedMemberIds;
        private bool[] nodeFalls;
        private double scaleFactor;
        private StepOutput previous;

        /// <exception cref="ArgumentNullException"><paramref name="solver"/> is null.</exception>
        public UnsupportedFallSolver(IDynamicSolver solver)
        {
            this.solver = solver ?? throw new ArgumentNullException(nameof(solver));
        }

        /// <summary>Sum of the timesteps completed since the last reset, s. A cancelled step adds nothing.</summary>
        public double ElapsedSeconds { get; private set; }

        /// <summary>The split of the input given to the last successful <see cref="Reset"/>, or null before one.</summary>
        public SupportPartitionResult Partition { get; private set; }

        /// <summary>
        /// Starts a run with the unsupported pieces at rest. The whole input, unsupported part included, is
        /// first checked as <see cref="CoupledStep"/> checks its own: with
        /// <see cref="RoomBridgeAdapter.ToBridge(SolverInput)"/> and
        /// <see cref="DynamicSelfWeight.Loads(SolverInput, IReadOnlyCollection{int})"/>. The wrapped solver is
        /// then reset with the supported input and <paramref name="configuration"/>, or not called at all when
        /// no member is supported. The input is read and never modified.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="input"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The input's scale factor, a scaled node position, a member length, an area, or a density is not
        /// positive and finite.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The input is inconsistent (see <see cref="SupportPartition.Split"/>), or a member names a missing section.
        /// </exception>
        /// <remarks>The wrapped solver's own exceptions reach the caller, and the solver then needs a new reset.</remarks>
        public void Reset(SolverInput input, RunConfiguration configuration)
        {
            ready = false;
            Partition = null;
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            double scale = input.ScaleFactor;
            if (!(scale > 0.0) || double.IsInfinity(scale))
            {
                throw new ArgumentOutOfRangeException(nameof(input), scale, "The input's scale factor must be positive and finite.");
            }

            SupportPartitionResult partition = SupportPartition.Split(input);
            DynamicSelfWeight.Loads(RoomBridgeAdapter.ToBridge(input), Array.Empty<int>());
            if (partition.HasSupportedMembers)
            {
                solver.Reset(partition.SupportedInput, configuration);
            }

            Node[] nodes = input.Nodes;
            Member[] members = input.Members;
            var nodeIndexById = new Dictionary<int, int>(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                nodeIndexById.Add(nodes[i].Id, i);
            }

            var supported = new bool[members.Length];
            foreach (int m in partition.SupportedMemberIndices)
            {
                supported[m] = true;
            }

            var ids = new int[members.Length];
            var supportedIds = new HashSet<int>();
            var falls = new bool[nodes.Length];
            for (int m = 0; m < members.Length; m++)
            {
                ids[m] = members[m].Id;
                if (supported[m])
                {
                    supportedIds.Add(members[m].Id);
                }
                else
                {
                    falls[nodeIndexById[members[m].NodeA]] = true;
                    falls[nodeIndexById[members[m].NodeB]] = true;
                }
            }

            nodeCount = nodes.Length;
            memberIds = ids;
            memberSupported = supported;
            supportedMemberIds = supportedIds;
            nodeFalls = falls;
            scaleFactor = scale;
            ElapsedSeconds = 0.0;
            previous = AtRest();
            Partition = partition;
            ready = true;
        }

        /// <summary>
        /// Advances <paramref name="dt"/> seconds. <paramref name="shouldCancel"/> is polled once, first; when
        /// it returns true nothing is stepped, <see cref="ElapsedSeconds"/> stays, and the previous output is
        /// returned. Otherwise the wrapped solver steps with <paramref name="previousWheelLoads"/> unchanged
        /// and a cancel callback that always returns false. Before the first completed step the previous
        /// output is the run at rest: zero displacements and rotations, a zero force for every member, no
        /// failures, and a null car state.
        /// </summary>
        /// <returns>
        /// One displacement and rotation per node in input order; the forces of the standing members in
        /// input order, with zero forces for unsupported members; and the wrapped solver's failed member
        /// ids and car state, which are empty and null when no member is supported.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="shouldCancel"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="dt"/> is not positive and finite.</exception>
        /// <exception cref="InvalidOperationException">
        /// <see cref="Reset"/> has not succeeded, the wrapped solver returned an output that does not match
        /// the supported input, or the fall is no longer finite. After an exception from this step or the
        /// wrapped one, the solver needs a new reset.
        /// </exception>
        public StepOutput Step(WheelLoad[] previousWheelLoads, double dt, Func<bool> shouldCancel)
        {
            if (!ready)
            {
                throw new InvalidOperationException("Reset the solver before stepping it.");
            }

            if (shouldCancel == null)
            {
                throw new ArgumentNullException(nameof(shouldCancel));
            }

            if (!(dt > 0.0) || double.IsInfinity(dt))
            {
                throw new ArgumentOutOfRangeException(nameof(dt), dt, "The timestep must be positive and finite.");
            }

            if (shouldCancel())
            {
                return previous;
            }

            try
            {
                StepOutput supported = null;
                Dictionary<int, MemberForce> supportedForces = null;
                if (Partition.HasSupportedMembers)
                {
                    supported = solver.Step(previousWheelLoads, dt, NeverCancel);
                    supportedForces = RequireMatchingOutput(supported);
                }

                double elapsed = ElapsedSeconds + dt;
                double drop = -0.5 * Constants.Loads.Gravity * elapsed * elapsed / scaleFactor;
                if (!double.IsFinite(drop))
                {
                    throw new InvalidOperationException("The fall of the unsupported pieces is no longer finite.");
                }

                StepOutput output = Output(supported, supportedForces, new Vec3(0.0, drop, 0.0));
                ElapsedSeconds = elapsed;
                previous = output;
                return output;
            }
            catch
            {
                ready = false;
                throw;
            }
        }

        private StepOutput AtRest()
        {
            var forces = new MemberForce[memberIds.Length];
            for (int m = 0; m < forces.Length; m++)
            {
                forces[m] = new MemberForce { MemberId = memberIds[m] };
            }

            return new StepOutput
            {
                Displacements = new Vec3[nodeCount],
                Rotations = new Vec3[nodeCount],
                MemberForces = forces,
            };
        }

        private StepOutput Output(StepOutput supported, Dictionary<int, MemberForce> supportedForces, Vec3 fall)
        {
            var output = new StepOutput
            {
                Displacements = new Vec3[nodeCount],
                Rotations = new Vec3[nodeCount],
                FailedMemberIds = supported?.FailedMemberIds ?? Array.Empty<int>(),
                CarState = supported?.CarState,
            };
            for (int i = 0; i < nodeCount; i++)
            {
                if (nodeFalls[i])
                {
                    output.Displacements[i] = fall;
                }
            }

            if (supported != null)
            {
                int[] supportedNodes = Partition.SupportedNodeIndices;
                for (int i = 0; i < supportedNodes.Length; i++)
                {
                    output.Displacements[supportedNodes[i]] = supported.Displacements[i];
                    output.Rotations[supportedNodes[i]] = supported.Rotations[i];
                }
            }

            var forces = new List<MemberForce>(memberIds.Length);
            for (int m = 0; m < memberIds.Length; m++)
            {
                if (!memberSupported[m])
                {
                    forces.Add(new MemberForce { MemberId = memberIds[m] });
                }
                else if (supportedForces != null && supportedForces.TryGetValue(memberIds[m], out MemberForce force))
                {
                    forces.Add(force);
                }
            }

            output.MemberForces = forces.ToArray();
            return output;
        }

        /// <summary>Checks the wrapped output's shape and returns its member forces by id.</summary>
        private Dictionary<int, MemberForce> RequireMatchingOutput(StepOutput supported)
        {
            if (supported == null)
            {
                throw new InvalidOperationException("The wrapped dynamic solver returned no output.");
            }

            SolverInput supportedInput = Partition.SupportedInput;
            int supportedNodeCount = supportedInput.Nodes.Length;
            if (supported.Displacements == null || supported.Displacements.Length != supportedNodeCount
                || supported.Rotations == null || supported.Rotations.Length != supportedNodeCount)
            {
                throw new InvalidOperationException("The wrapped dynamic solver must return one displacement and one rotation per supported node.");
            }

            if (supported.MemberForces == null)
            {
                throw new InvalidOperationException("The wrapped dynamic solver returned no member forces.");
            }

            var forcesById = new Dictionary<int, MemberForce>(supported.MemberForces.Length);
            foreach (MemberForce force in supported.MemberForces)
            {
                if (force == null || !supportedMemberIds.Contains(force.MemberId) || forcesById.ContainsKey(force.MemberId))
                {
                    throw new InvalidOperationException(
                        "The wrapped dynamic solver returned a force for a missing, repeated, or unsupported member"
                        + (force == null ? "." : " " + Format(force.MemberId) + "."));
                }

                forcesById.Add(force.MemberId, force);
            }

            return forcesById;
        }

        private static string Format(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
    }
}
