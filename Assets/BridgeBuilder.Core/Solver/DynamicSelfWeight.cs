using System;
using System.Collections.Generic;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// The bridge's own weight in the dynamic run (spec 2.6, 2.7, 2.8, and 2.10). A standing member weighs
    /// <c>Density * A * L * gravity</c>, where L is its length in the bridge-world input, and the weight is
    /// split equally between its two end nodes, pointing down (-Y). Area and density come from the member's
    /// <see cref="SectionProperties"/>. A removed member weighs nothing. These loads are the translational
    /// part of the <see cref="NewmarkIntegrator"/> lumped mass times gravity, and with no member removed they
    /// equal <see cref="DeadLoadPreview.SelfWeight(SolverInput, double)"/> of the room input.
    /// <see cref="NewmarkIntegrator"/> applies only the loads it is given, so a run resets with
    /// <c>Reset(bridgeInput, selfWeight)</c> to start at rest under its own weight, passes the self-weight
    /// plus the wheel loads to every step, and after a failure recomputes the self-weight from every
    /// <see cref="StepOutput.FailedMemberIds"/> so far. A piece cut loose then falls at gravity.
    /// </summary>
    public static class DynamicSelfWeight
    {
        /// <summary>Self-weight of a bridge-world input at <see cref="Constants.Loads.Gravity"/>.</summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A node position, a member length, an area, or a density is not positive and finite, or a load is not finite.
        /// </exception>
        /// <exception cref="ArgumentException">The input is inconsistent, or a removed id names no member.</exception>
        public static NodalLoad[] Loads(SolverInput bridgeInput, IReadOnlyCollection<int> removedMemberIds)
        {
            return Loads(bridgeInput, removedMemberIds, Constants.Loads.Gravity);
        }

        /// <summary>
        /// Self-weight of a bridge-world input as nodal loads, N: one load per node in
        /// <see cref="SolverInput.Nodes"/> order, holding half the weight of every attached member not in
        /// <paramref name="removedMemberIds"/>, along -Y, with no moment. A node whose members are all removed
        /// gets a zero load. Loads on fixed nodes are kept. Positions are used as given and
        /// <see cref="SolverInput.ScaleFactor"/> is not applied, so pass the output of
        /// <see cref="RoomBridgeAdapter.ToBridge(SolverInput)"/>. Every member is checked, removed or not.
        /// The input is not modified.
        /// </summary>
        /// <param name="bridgeInput">The bridge-world input the integrator was reset with.</param>
        /// <param name="removedMemberIds">Ids of the members that have failed so far. Repeats are ignored.</param>
        /// <param name="gravity">Gravitational acceleration, m/s^2.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Gravity, a node position, a member length, an area, or a density is not positive and finite, or a load is not finite.
        /// </exception>
        /// <exception cref="ArgumentException">The input is inconsistent, or a removed id names no member.</exception>
        public static NodalLoad[] Loads(SolverInput bridgeInput, IReadOnlyCollection<int> removedMemberIds, double gravity)
        {
            if (bridgeInput == null)
            {
                throw new ArgumentNullException(nameof(bridgeInput));
            }

            if (removedMemberIds == null)
            {
                throw new ArgumentNullException(nameof(removedMemberIds));
            }

            RequirePositive(gravity, nameof(gravity), "Gravity");
            Node[] nodes = bridgeInput.Nodes ?? throw new ArgumentException("The input has no node array.", nameof(bridgeInput));
            Member[] members = bridgeInput.Members ?? throw new ArgumentException("The input has no member array.", nameof(bridgeInput));
            Dictionary<int, int> nodeIndexById = IndexNodes(nodes);
            Dictionary<int, SectionProperties> sectionsById = IndexSections(bridgeInput.Sections);
            var memberIds = new HashSet<int>();
            var nodeAOf = new int[members.Length];
            var nodeBOf = new int[members.Length];
            var weights = new double[members.Length];
            for (int m = 0; m < members.Length; m++)
            {
                Member member = members[m] ?? throw new ArgumentException("The member array holds a null entry.", nameof(bridgeInput));
                if (!memberIds.Add(member.Id))
                {
                    throw new ArgumentException("Member id " + Format(member.Id) + " is repeated.", nameof(bridgeInput));
                }

                nodeAOf[m] = RequireNode(nodeIndexById, member.NodeA, member);
                nodeBOf[m] = RequireNode(nodeIndexById, member.NodeB, member);
                if (!sectionsById.TryGetValue(member.SectionId, out SectionProperties section))
                {
                    throw new ArgumentException("Member " + Format(member.Id) + " references missing section " + Format(member.SectionId) + ".", nameof(bridgeInput));
                }

                double length = Vec3.Distance(nodes[nodeAOf[m]].Position, nodes[nodeBOf[m]].Position);
                RequirePositive(length, nameof(bridgeInput), "The length of member " + Format(member.Id));
                weights[m] = Weight(section, length, gravity);
            }

            var removed = new HashSet<int>();
            foreach (int id in removedMemberIds)
            {
                if (!memberIds.Contains(id))
                {
                    throw new ArgumentException("Removed member id " + Format(id) + " is not in the input.", nameof(removedMemberIds));
                }

                removed.Add(id);
            }

            var forceY = new double[nodes.Length];
            for (int m = 0; m < members.Length; m++)
            {
                if (removed.Contains(members[m].Id))
                {
                    continue;
                }

                double half = 0.5 * weights[m];
                forceY[nodeAOf[m]] -= half;
                forceY[nodeBOf[m]] -= half;
            }

            var loads = new NodalLoad[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                if (double.IsInfinity(forceY[i]))
                {
                    throw new ArgumentOutOfRangeException(nameof(bridgeInput), forceY[i], "The self-weight on node " + Format(nodes[i].Id) + " is not finite.");
                }

                loads[i] = new NodalLoad { NodeId = nodes[i].Id, Force = new Vec3(0.0, forceY[i], 0.0), Moment = Vec3.Zero };
            }

            return loads;
        }

        private static double Weight(SectionProperties section, double length, double gravity)
        {
            string name = "Section " + Format(section.Id);
            RequirePositive(section.A, "bridgeInput", name + " area");
            RequirePositive(section.Density, "bridgeInput", name + " density");
            double weight = section.Density * section.A * length * gravity;
            if (double.IsInfinity(weight))
            {
                throw new ArgumentOutOfRangeException("bridgeInput", weight, name + " gives a member weight that is not finite.");
            }

            return weight;
        }

        private static Dictionary<int, int> IndexNodes(Node[] nodes)
        {
            var indexById = new Dictionary<int, int>(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                Node node = nodes[i] ?? throw new ArgumentException("The node array holds a null entry.", "bridgeInput");
                Vec3 position = node.Position;
                if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z))
                {
                    throw new ArgumentOutOfRangeException("bridgeInput", position, "Node " + Format(node.Id) + " has a non-finite position.");
                }

                if (indexById.ContainsKey(node.Id))
                {
                    throw new ArgumentException("Node id " + Format(node.Id) + " is repeated.", "bridgeInput");
                }

                indexById.Add(node.Id, i);
            }

            return indexById;
        }

        private static Dictionary<int, SectionProperties> IndexSections(SectionProperties[] sections)
        {
            if (sections == null)
            {
                throw new ArgumentException("The input has no section array.", "bridgeInput");
            }

            var byId = new Dictionary<int, SectionProperties>(sections.Length);
            foreach (SectionProperties section in sections)
            {
                if (section == null)
                {
                    throw new ArgumentException("The section array holds a null entry.", "bridgeInput");
                }

                if (byId.ContainsKey(section.Id))
                {
                    throw new ArgumentException("Section id " + Format(section.Id) + " is repeated.", "bridgeInput");
                }

                byId.Add(section.Id, section);
            }

            return byId;
        }

        private static int RequireNode(Dictionary<int, int> nodeIndexById, int nodeId, Member member)
        {
            if (!nodeIndexById.TryGetValue(nodeId, out int index))
            {
                throw new ArgumentException("Member " + Format(member.Id) + " references missing node " + Format(nodeId) + ".", "bridgeInput");
            }

            return index;
        }

        private static void RequirePositive(double value, string paramName, string description)
        {
            if (!(value > 0.0) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(paramName, value, description + " must be positive and finite.");
            }
        }

        private static string Format(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }
    }
}
