using System;
using System.Collections.Generic;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// A static solver that ignores what is not supported (spec 2.10 and 11.1), so a line that touches
    /// nothing does not stop the dead-load preview. It solves only
    /// <see cref="SupportPartition.Split"/>'s supported input with the wrapped solver, which never sees
    /// the loads on unsupported nodes, and maps the result back to the full input. Unsupported nodes get
    /// zero displacement and rotation, and unsupported members get zero forces. Pass it to
    /// <see cref="DeadLoadPreview.Run(SolverInput, IStaticSolver, Func{bool})"/> or
    /// <see cref="PreviewScheduler"/> in place of the solver it wraps.
    /// </summary>
    public sealed class SupportedOnlyStaticSolver : IStaticSolver
    {
        private readonly IStaticSolver solver;

        /// <exception cref="ArgumentNullException"><paramref name="solver"/> is null.</exception>
        public SupportedOnlyStaticSolver(IStaticSolver solver)
        {
            this.solver = solver ?? throw new ArgumentNullException(nameof(solver));
        }

        /// <summary>
        /// Solves the supported part of <paramref name="input"/> and returns one displacement and
        /// rotation per node and one force per member, in the full input's order. Supported entries are
        /// the wrapped solver's, unchanged. When no member is supported, every node left is fixed, so the
        /// wrapped solver is not called and the result is all zero. The input and loads are not modified.
        /// </summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">
        /// The input is inconsistent (see <see cref="SupportPartition.Split"/>), or a load is null, not
        /// finite, or names a missing node. The wrapped solver may reject the supported part for its own reasons.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The wrapped solver returned a result that does not match the supported input. The wrapped solver
        /// may also throw this, for example for a mechanism.
        /// </exception>
        public StaticSolveResult Solve(SolverInput input, NodalLoad[] loads)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (loads == null)
            {
                throw new ArgumentNullException(nameof(loads));
            }

            SupportPartitionResult partition = SupportPartition.Split(input);
            Node[] nodes = input.Nodes;
            Member[] members = input.Members;
            var knownNodeIds = new HashSet<int>();
            foreach (Node node in nodes)
            {
                knownNodeIds.Add(node.Id);
            }

            var supportedNodeIds = new HashSet<int>();
            foreach (Node node in partition.SupportedInput.Nodes)
            {
                supportedNodeIds.Add(node.Id);
            }

            var supportedLoads = new List<NodalLoad>(loads.Length);
            foreach (NodalLoad load in loads)
            {
                if (load == null)
                {
                    throw new ArgumentException("Loads must not contain null.", nameof(loads));
                }

                if (!IsFinite(load.Force) || !IsFinite(load.Moment))
                {
                    throw new ArgumentException("Load on node " + Format(load.NodeId) + " has a NaN or infinite component.", nameof(loads));
                }

                if (!knownNodeIds.Contains(load.NodeId))
                {
                    throw new ArgumentException("Load references missing node " + Format(load.NodeId) + ".", nameof(loads));
                }

                if (supportedNodeIds.Contains(load.NodeId))
                {
                    supportedLoads.Add(load);
                }
            }

            var result = new StaticSolveResult
            {
                Displacements = new Vec3[nodes.Length],
                Rotations = new Vec3[nodes.Length],
                MemberForces = new MemberForce[members.Length],
            };
            for (int m = 0; m < members.Length; m++)
            {
                result.MemberForces[m] = new MemberForce { MemberId = members[m].Id };
            }

            if (!partition.HasSupportedMembers)
            {
                return result;
            }

            StaticSolveResult supported = solver.Solve(partition.SupportedInput, supportedLoads.ToArray());
            RequireMatchingResult(supported, partition.SupportedInput);
            for (int i = 0; i < partition.SupportedNodeIndices.Length; i++)
            {
                int node = partition.SupportedNodeIndices[i];
                result.Displacements[node] = supported.Displacements[i];
                result.Rotations[node] = supported.Rotations[i];
            }

            for (int k = 0; k < partition.SupportedMemberIndices.Length; k++)
            {
                result.MemberForces[partition.SupportedMemberIndices[k]] = supported.MemberForces[k];
            }

            return result;
        }

        private static void RequireMatchingResult(StaticSolveResult result, SolverInput supportedInput)
        {
            if (result == null)
            {
                throw new InvalidOperationException("The wrapped static solver returned no result.");
            }

            int nodeCount = supportedInput.Nodes.Length;
            if (result.Displacements == null || result.Displacements.Length != nodeCount
                || result.Rotations == null || result.Rotations.Length != nodeCount)
            {
                throw new InvalidOperationException("The wrapped static solver must return one displacement and one rotation per supported node.");
            }

            Member[] members = supportedInput.Members;
            if (result.MemberForces == null || result.MemberForces.Length != members.Length)
            {
                throw new InvalidOperationException("The wrapped static solver must return one member force per supported member.");
            }

            for (int k = 0; k < members.Length; k++)
            {
                MemberForce force = result.MemberForces[k];
                if (force == null || force.MemberId != members[k].Id)
                {
                    throw new InvalidOperationException("The wrapped static solver must return member forces in SolverInput.Members order.");
                }
            }
        }

        private static bool IsFinite(Vec3 v)
        {
            return double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
        }

        private static string Format(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
    }
}
