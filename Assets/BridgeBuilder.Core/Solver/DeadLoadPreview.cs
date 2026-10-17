using System;
using System.Collections.Generic;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>Outcome of one dead-load preview run.</summary>
    public enum DeadLoadPreviewStatus
    {
        /// <summary>The static solve ran and every member has a utilization.</summary>
        Completed,

        /// <summary>The caller asked to stop, so the result holds no solve.</summary>
        Cancelled,
    }

    /// <summary>
    /// Result of one dead-load preview (spec 2.10). <c>Utilizations[k]</c> belongs to member
    /// <c>MemberIds[k]</c>, in <see cref="SolverInput.Members"/> order. When <see cref="Status"/> is
    /// <see cref="DeadLoadPreviewStatus.Cancelled"/> the arrays are empty and <see cref="StaticResult"/> is null.
    /// </summary>
    public class DeadLoadPreviewResult
    {
        public DeadLoadPreviewStatus Status { get; set; }

        /// <summary>The self-weight that was solved, bridge-world N, one load per node in <see cref="SolverInput.Nodes"/> order.</summary>
        public NodalLoad[] SelfWeight { get; set; } = Array.Empty<NodalLoad>();

        /// <summary>
        /// The static solve in room units: translations divided by the input's scale factor. Rotations
        /// and member forces (bridge-world N and N*m, with <see cref="MemberForce.Utilization"/> left 0)
        /// are as the solver returned them.
        /// </summary>
        public StaticSolveResult StaticResult { get; set; }

        public int[] MemberIds { get; set; } = Array.Empty<int>();

        /// <summary>Worst capacity ratio per member from <see cref="MemberCapacity"/>: 0 unloaded, 1 or more failed.</summary>
        public double[] Utilizations { get; set; } = Array.Empty<double>();
    }

    /// <summary>
    /// Dead-load preview (spec 2.10): one static solve of the bridge under its own weight, with each
    /// member's utilization on the same scale as a test run. A member weighs
    /// <c>length_bridge * A * density * gravity</c>, where <c>length_bridge</c> is its length after the
    /// room-to-bridge scale (spec 2.6), and the weight is split equally between its two nodes, pointing
    /// down (-Y). Area and density come from the member's <see cref="SectionProperties"/>. Hidden braces
    /// weigh like any other member, and fills add nothing beyond their bracing. Loads on fixed nodes are
    /// kept and go straight into the supports. Everything runs on the calling thread.
    /// </summary>
    public static class DeadLoadPreview
    {
        /// <summary>
        /// Weight of one member, N, at <see cref="Constants.Scale.RoomToBridge"/> and
        /// <see cref="Constants.Loads.Gravity"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The length, area, or density is not positive and finite, or the weight is not finite.</exception>
        public static double MemberWeight(double roomLength, SectionProperties section)
        {
            return MemberWeight(roomLength, section, Constants.Scale.RoomToBridge, Constants.Loads.Gravity);
        }

        /// <summary>
        /// Weight of one member, N: <c>(roomLength * scale) * A * density * gravity</c>.
        /// </summary>
        /// <param name="roomLength">Member length in room units, m.</param>
        /// <param name="section">Supplies the area and density.</param>
        /// <param name="scale">Room-to-bridge scale factor.</param>
        /// <param name="gravity">Gravitational acceleration, m/s^2.</param>
        /// <exception cref="ArgumentNullException"><paramref name="section"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The length, scale, gravity, area, or density is not positive and finite, or the weight is not finite.
        /// </exception>
        public static double MemberWeight(double roomLength, SectionProperties section, double scale, double gravity)
        {
            if (section == null)
            {
                throw new ArgumentNullException(nameof(section));
            }

            RequirePositive(roomLength, nameof(roomLength), "The member length");
            RequirePositive(gravity, nameof(gravity), "Gravity");
            double bridgeLength = RoomBridgeAdapter.ToBridgeLength(roomLength, scale);
            return Weight(bridgeLength, section, gravity, nameof(section), "The section");
        }

        /// <summary>Self-weight of a room-unit input at <see cref="Constants.Loads.Gravity"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="roomInput"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The scale factor, a member length, an area, or a density is not positive and finite, or a load is not finite.
        /// </exception>
        /// <exception cref="ArgumentException">The input is inconsistent.</exception>
        public static NodalLoad[] SelfWeight(SolverInput roomInput)
        {
            return SelfWeight(roomInput, Constants.Loads.Gravity);
        }

        /// <summary>
        /// Self-weight of a room-unit input as bridge-world nodal loads, N: one load per node in
        /// <see cref="SolverInput.Nodes"/> order, holding half the weight of every attached member along
        /// -Y and no moment. Member lengths are measured after scaling by the input's own
        /// <see cref="SolverInput.ScaleFactor"/>. The input is not modified.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="roomInput"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Gravity, the scale factor, a member length, an area, or a density is not positive and finite, or a load is not finite.
        /// </exception>
        /// <exception cref="ArgumentException">The input is inconsistent.</exception>
        public static NodalLoad[] SelfWeight(SolverInput roomInput, double gravity)
        {
            RequirePositive(gravity, nameof(gravity), "Gravity");
            return SelfWeight(BridgeFrame.FromRoom(roomInput), gravity);
        }

        /// <summary>Runs the preview at <see cref="Constants.Loads.Gravity"/>.</summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A length, section property, or scale factor is not positive and finite.</exception>
        /// <exception cref="ArgumentException">The input is inconsistent.</exception>
        /// <exception cref="InvalidOperationException">The solver returned a result that does not match the input or is not finite.</exception>
        public static DeadLoadPreviewResult Run(SolverInput roomInput, IStaticSolver solver, Func<bool> shouldCancel)
        {
            return Run(roomInput, solver, shouldCancel, Constants.Loads.Gravity);
        }

        /// <summary>
        /// Solves a room-unit input under its own weight with <paramref name="solver"/> and maps each
        /// member's forces to a utilization with <see cref="MemberCapacity.Evaluate(MemberForce, SectionProperties, double)"/>
        /// at its bridge-world length. The solver receives the bridge-world copy of the input from
        /// <see cref="RoomBridgeAdapter.ToBridge(SolverInput)"/>. <paramref name="shouldCancel"/> is polled
        /// before the solve, and the solver is not called when it returns true. It is polled again after
        /// the solve, and a true result discards that solve. Either way the status is
        /// <see cref="DeadLoadPreviewStatus.Cancelled"/>. Exceptions from the solver, such as a mechanism,
        /// and from <see cref="MemberCapacity"/> reach the caller.
        /// </summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Gravity, the scale factor, a member length, or a section property is not positive and finite.
        /// </exception>
        /// <exception cref="ArgumentException">The input is inconsistent.</exception>
        /// <exception cref="InvalidOperationException">The solver returned a result that does not match the input or is not finite.</exception>
        public static DeadLoadPreviewResult Run(SolverInput roomInput, IStaticSolver solver, Func<bool> shouldCancel, double gravity)
        {
            if (solver == null)
            {
                throw new ArgumentNullException(nameof(solver));
            }

            if (shouldCancel == null)
            {
                throw new ArgumentNullException(nameof(shouldCancel));
            }

            RequirePositive(gravity, nameof(gravity), "Gravity");
            BridgeFrame frame = BridgeFrame.FromRoom(roomInput);
            NodalLoad[] loads = SelfWeight(frame, gravity);
            if (shouldCancel())
            {
                return Cancelled();
            }

            StaticSolveResult bridgeResult = solver.Solve(frame.Input, loads);

            // The solve cannot be interrupted, so a cancel that arrived during it discards the result.
            if (shouldCancel())
            {
                return Cancelled();
            }

            RequireMatchingResult(bridgeResult, frame);
            Member[] members = frame.Input.Members;
            var memberIds = new int[members.Length];
            var utilizations = new double[members.Length];
            for (int m = 0; m < members.Length; m++)
            {
                double utilization = MemberCapacity.Evaluate(bridgeResult.MemberForces[m], frame.Sections[m], frame.Lengths[m]).Utilization;
                if (!double.IsFinite(utilization))
                {
                    throw new InvalidOperationException("Member " + Format(members[m].Id) + " has a non-finite utilization.");
                }

                memberIds[m] = members[m].Id;
                utilizations[m] = utilization;
            }

            return new DeadLoadPreviewResult
            {
                Status = DeadLoadPreviewStatus.Completed,
                SelfWeight = loads,
                StaticResult = RoomBridgeAdapter.ToRoom(bridgeResult, roomInput.ScaleFactor),
                MemberIds = memberIds,
                Utilizations = utilizations,
            };
        }

        private static DeadLoadPreviewResult Cancelled()
        {
            return new DeadLoadPreviewResult { Status = DeadLoadPreviewStatus.Cancelled };
        }

        private static NodalLoad[] SelfWeight(BridgeFrame frame, double gravity)
        {
            Node[] nodes = frame.Input.Nodes;
            Member[] members = frame.Input.Members;
            var forceY = new double[nodes.Length];
            for (int m = 0; m < members.Length; m++)
            {
                double half = 0.5 * Weight(frame.Lengths[m], frame.Sections[m], gravity, "roomInput", "Section " + Format(frame.Sections[m].Id));
                forceY[frame.NodeA[m]] -= half;
                forceY[frame.NodeB[m]] -= half;
            }

            var loads = new NodalLoad[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                if (double.IsInfinity(forceY[i]))
                {
                    throw new ArgumentOutOfRangeException("roomInput", forceY[i], "The self-weight on node " + Format(nodes[i].Id) + " is not finite.");
                }

                loads[i] = new NodalLoad { NodeId = nodes[i].Id, Force = new Vec3(0.0, forceY[i], 0.0), Moment = Vec3.Zero };
            }

            return loads;
        }

        private static double Weight(double bridgeLength, SectionProperties section, double gravity, string paramName, string sectionName)
        {
            RequirePositive(section.A, paramName, sectionName + " area");
            RequirePositive(section.Density, paramName, sectionName + " density");
            double weight = bridgeLength * section.A * section.Density * gravity;
            if (double.IsInfinity(weight))
            {
                throw new ArgumentOutOfRangeException(paramName, weight, sectionName + " gives a member weight that is not finite.");
            }

            return weight;
        }

        private static void RequireMatchingResult(StaticSolveResult result, BridgeFrame frame)
        {
            if (result == null)
            {
                throw new InvalidOperationException("The static solver returned no result.");
            }

            Node[] nodes = frame.Input.Nodes;
            if (result.Displacements == null || result.Displacements.Length != nodes.Length
                || result.Rotations == null || result.Rotations.Length != nodes.Length)
            {
                throw new InvalidOperationException("The static solver must return one displacement and one rotation per node.");
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                if (!IsFinite(result.Displacements[i]) || !IsFinite(result.Rotations[i]))
                {
                    throw new InvalidOperationException("The static solver returned a non-finite displacement or rotation for node " + Format(nodes[i].Id) + ".");
                }
            }

            Member[] members = frame.Input.Members;
            if (result.MemberForces == null || result.MemberForces.Length != members.Length)
            {
                throw new InvalidOperationException("The static solver must return one member force per member.");
            }

            for (int m = 0; m < members.Length; m++)
            {
                MemberForce force = result.MemberForces[m];
                if (force == null || force.MemberId != members[m].Id)
                {
                    throw new InvalidOperationException("The static solver must return member forces in SolverInput.Members order.");
                }

                if (!IsFinite(force))
                {
                    throw new InvalidOperationException("The static solver returned a non-finite force in member " + Format(members[m].Id) + ".");
                }
            }
        }

        private static void RequirePositive(double value, string paramName, string description)
        {
            if (!(value > 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(paramName, value, description + " must be positive and finite.");
            }
        }

        private static bool IsFinite(Vec3 v)
        {
            return double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
        }

        private static bool IsFinite(MemberForce force)
        {
            return double.IsFinite(force.Axial)
                && double.IsFinite(force.ShearLocalYAtA) && double.IsFinite(force.ShearLocalZAtA)
                && double.IsFinite(force.MomentLocalYAtA) && double.IsFinite(force.MomentLocalZAtA)
                && double.IsFinite(force.ShearLocalYAtB) && double.IsFinite(force.ShearLocalZAtB)
                && double.IsFinite(force.MomentLocalYAtB) && double.IsFinite(force.MomentLocalZAtB);
        }

        private static string Format(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The bridge-world copy of an input with each member's node indices, section, and length resolved.</summary>
        private sealed class BridgeFrame
        {
            private BridgeFrame(SolverInput input, int[] nodeA, int[] nodeB, SectionProperties[] sections, double[] lengths)
            {
                Input = input;
                NodeA = nodeA;
                NodeB = nodeB;
                Sections = sections;
                Lengths = lengths;
            }

            public SolverInput Input { get; }

            /// <summary>Index into <see cref="SolverInput.Nodes"/> of each member's end A.</summary>
            public int[] NodeA { get; }

            public int[] NodeB { get; }

            public SectionProperties[] Sections { get; }

            /// <summary>Bridge-world member lengths, m.</summary>
            public double[] Lengths { get; }

            public static BridgeFrame FromRoom(SolverInput roomInput)
            {
                SolverInput input = RoomBridgeAdapter.ToBridge(roomInput);
                Node[] nodes = input.Nodes;
                Member[] members = input.Members ?? throw new ArgumentException("The input has no member array.", nameof(roomInput));
                Dictionary<int, int> nodeIndexById = IndexNodes(nodes);
                Dictionary<int, SectionProperties> sectionsById = IndexSections(input.Sections);

                var nodeA = new int[members.Length];
                var nodeB = new int[members.Length];
                var sections = new SectionProperties[members.Length];
                var lengths = new double[members.Length];
                for (int m = 0; m < members.Length; m++)
                {
                    Member member = members[m] ?? throw new ArgumentException("The member array holds a null entry.", nameof(roomInput));
                    nodeA[m] = RequireNode(nodeIndexById, member.NodeA, member);
                    nodeB[m] = RequireNode(nodeIndexById, member.NodeB, member);
                    if (!sectionsById.TryGetValue(member.SectionId, out sections[m]))
                    {
                        throw new ArgumentException("Member " + Format(member.Id) + " references missing section " + Format(member.SectionId) + ".", nameof(roomInput));
                    }

                    lengths[m] = Vec3.Distance(nodes[nodeA[m]].Position, nodes[nodeB[m]].Position);
                    RequirePositive(lengths[m], nameof(roomInput), "The bridge-world length of member " + Format(member.Id));
                }

                return new BridgeFrame(input, nodeA, nodeB, sections, lengths);
            }

            private static Dictionary<int, int> IndexNodes(Node[] nodes)
            {
                var indexById = new Dictionary<int, int>(nodes.Length);
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (indexById.ContainsKey(nodes[i].Id))
                    {
                        throw new ArgumentException("Node id " + Format(nodes[i].Id) + " is repeated.", "roomInput");
                    }

                    indexById.Add(nodes[i].Id, i);
                }

                return indexById;
            }

            private static Dictionary<int, SectionProperties> IndexSections(SectionProperties[] sections)
            {
                if (sections == null)
                {
                    throw new ArgumentException("The input has no section array.", "roomInput");
                }

                var byId = new Dictionary<int, SectionProperties>(sections.Length);
                foreach (SectionProperties section in sections)
                {
                    if (section == null)
                    {
                        throw new ArgumentException("The section array holds a null entry.", "roomInput");
                    }

                    if (byId.ContainsKey(section.Id))
                    {
                        throw new ArgumentException("Section id " + Format(section.Id) + " is repeated.", "roomInput");
                    }

                    byId.Add(section.Id, section);
                }

                return byId;
            }

            private static int RequireNode(Dictionary<int, int> nodeIndexById, int nodeId, Member member)
            {
                if (!nodeIndexById.TryGetValue(nodeId, out int index))
                {
                    throw new ArgumentException("Member " + Format(member.Id) + " references missing node " + Format(nodeId) + ".", "roomInput");
                }

                return index;
            }
        }
    }
}
