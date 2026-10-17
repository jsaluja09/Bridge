using System;
using System.Collections.Generic;
using System.Globalization;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// Linear static solve of a rigid-jointed frame with fixed supports (spec 2.5 and 2.7). Every
    /// member is a <see cref="FrameElement"/>. A fixed node restrains all six degrees of freedom, and
    /// those equations are removed from the system exactly. Positions are used as given and results
    /// come back in the same length units; <see cref="SolverInput.ScaleFactor"/> is not applied here.
    /// A load on a fixed node goes straight into the support. Fills are not read: their hidden
    /// bracing arrives as ordinary members.
    /// </summary>
    public sealed class StaticSolver : IStaticSolver
    {
        /// <exception cref="ArgumentException">The input is inconsistent, a section stiffness is not positive and finite, or a load component is NaN or infinite.</exception>
        /// <exception cref="InvalidOperationException">The structure is a mechanism (the stiffness is not positive definite) or the solve overflowed.</exception>
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

            Node[] nodes = input.Nodes ?? throw new ArgumentException("Nodes must not be null.", nameof(input));
            Member[] members = input.Members ?? throw new ArgumentException("Members must not be null.", nameof(input));
            Dictionary<int, int> nodeIndexById = IndexNodes(nodes);
            Dictionary<int, SectionProperties> sectionsById = IndexSections(input.Sections);

            const int dofPerNode = Constants.Solver.DofPerNode;
            var equationOfDof = new int[nodes.Length * dofPerNode];
            int equationCount = 0;
            for (int node = 0; node < nodes.Length; node++)
            {
                for (int dof = 0; dof < dofPerNode; dof++)
                {
                    equationOfDof[node * dofPerNode + dof] = nodes[node].Fixed ? -1 : equationCount++;
                }
            }

            var rhs = new double[equationCount];
            foreach (NodalLoad load in loads)
            {
                if (load == null)
                {
                    throw new ArgumentException("Loads must not contain null.", nameof(loads));
                }

                if (!FrameElement.IsFinite(load.Force) || !FrameElement.IsFinite(load.Moment))
                {
                    throw new ArgumentException("Load on node " + Format(load.NodeId) + " has a NaN or infinite component.", nameof(loads));
                }

                if (!nodeIndexById.TryGetValue(load.NodeId, out int node))
                {
                    throw new ArgumentException("Load references missing node " + Format(load.NodeId) + ".", nameof(loads));
                }

                AddLoad(rhs, equationOfDof, node * dofPerNode, load.Force);
                AddLoad(rhs, equationOfDof, node * dofPerNode + 3, load.Moment);
            }

            var elementDofs = new int[members.Length][];
            var elementSections = new SectionProperties[members.Length];
            var system = new DenseCholesky(equationCount);
            for (int m = 0; m < members.Length; m++)
            {
                Member member = members[m] ?? throw new ArgumentException("Members must not contain null.", nameof(input));
                int nodeA = RequireNode(nodeIndexById, member.NodeA, member);
                int nodeB = RequireNode(nodeIndexById, member.NodeB, member);
                if (!sectionsById.TryGetValue(member.SectionId, out SectionProperties section))
                {
                    throw new ArgumentException("Member " + Format(member.Id) + " references missing section " + Format(member.SectionId) + ".", nameof(input));
                }

                double[,] stiffness = FrameElement.WorldStiffness(nodes[nodeA].Position, nodes[nodeB].Position, section);
                int[] dofs = ElementDofs(nodeA, nodeB);
                for (int row = 0; row < FrameElement.DofCount; row++)
                {
                    int equationRow = equationOfDof[dofs[row]];
                    if (equationRow < 0)
                    {
                        continue;
                    }

                    for (int column = 0; column < FrameElement.DofCount; column++)
                    {
                        int equationColumn = equationOfDof[dofs[column]];
                        if (equationColumn >= 0 && stiffness[row, column] != 0.0)
                        {
                            system.Add(equationRow, equationColumn, stiffness[row, column]);
                        }
                    }
                }

                elementDofs[m] = dofs;
                elementSections[m] = section;
            }

            var solution = new double[equationCount];
            if (equationCount > 0)
            {
                system.Factor();
                system.Solve(rhs, solution);
            }

            var dofValues = new double[equationOfDof.Length];
            for (int dof = 0; dof < equationOfDof.Length; dof++)
            {
                int equation = equationOfDof[dof];
                double value = equation < 0 ? 0.0 : solution[equation];
                if (!double.IsFinite(value))
                {
                    throw new InvalidOperationException("The static solve produced a non-finite displacement.");
                }

                dofValues[dof] = value;
            }

            var result = new StaticSolveResult
            {
                Displacements = new Vec3[nodes.Length],
                Rotations = new Vec3[nodes.Length],
                MemberForces = new MemberForce[members.Length],
            };
            for (int node = 0; node < nodes.Length; node++)
            {
                int first = node * dofPerNode;
                result.Displacements[node] = new Vec3(dofValues[first], dofValues[first + 1], dofValues[first + 2]);
                result.Rotations[node] = new Vec3(dofValues[first + 3], dofValues[first + 4], dofValues[first + 5]);
            }

            var elementValues = new double[FrameElement.DofCount];
            for (int m = 0; m < members.Length; m++)
            {
                for (int i = 0; i < FrameElement.DofCount; i++)
                {
                    elementValues[i] = dofValues[elementDofs[m][i]];
                }

                Member member = members[m];
                Vec3 positionA = nodes[nodeIndexById[member.NodeA]].Position;
                Vec3 positionB = nodes[nodeIndexById[member.NodeB]].Position;
                MemberForce force = FrameElement.EndForces(member.Id, positionA, positionB, elementSections[m], elementValues);
                if (!IsFinite(force))
                {
                    throw new InvalidOperationException("The static solve produced a non-finite force in member " + Format(member.Id) + ".");
                }

                result.MemberForces[m] = force;
            }

            return result;
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

        private static int[] ElementDofs(int nodeA, int nodeB)
        {
            const int dofPerNode = Constants.Solver.DofPerNode;
            var dofs = new int[FrameElement.DofCount];
            for (int dof = 0; dof < dofPerNode; dof++)
            {
                dofs[dof] = nodeA * dofPerNode + dof;
                dofs[dofPerNode + dof] = nodeB * dofPerNode + dof;
            }

            return dofs;
        }

        private static void AddLoad(double[] rhs, int[] equationOfDof, int firstDof, Vec3 value)
        {
            AddLoad(rhs, equationOfDof[firstDof], value.X);
            AddLoad(rhs, equationOfDof[firstDof + 1], value.Y);
            AddLoad(rhs, equationOfDof[firstDof + 2], value.Z);
        }

        private static void AddLoad(double[] rhs, int equation, double value)
        {
            if (equation >= 0)
            {
                rhs[equation] += value;
            }
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

        /// <summary>
        /// Dense symmetric positive definite system. Stores the full matrix, as callers add both
        /// (i, j) and (j, i), and factors its lower triangle in place as L * L^T.
        /// </summary>
        private sealed class DenseCholesky : ILinearSolver
        {
            private readonly int order;
            private readonly double[] entries;
            private bool factored;

            public DenseCholesky(int order)
            {
                this.order = order;
                entries = new double[order * order];
            }

            public void Add(int row, int column, double value)
            {
                if (row < 0 || row >= order)
                {
                    throw new ArgumentOutOfRangeException(nameof(row));
                }

                if (column < 0 || column >= order)
                {
                    throw new ArgumentOutOfRangeException(nameof(column));
                }

                if (factored)
                {
                    throw new InvalidOperationException("The system is already factored.");
                }

                entries[row * order + column] += value;
            }

            public void Factor()
            {
                if (factored)
                {
                    throw new InvalidOperationException("The system is already factored.");
                }

                for (int j = 0; j < order; j++)
                {
                    int rowJ = j * order;
                    double pivot = entries[rowJ + j];
                    for (int k = 0; k < j; k++)
                    {
                        pivot -= entries[rowJ + k] * entries[rowJ + k];
                    }

                    if (!(pivot > 0.0) || double.IsInfinity(pivot))
                    {
                        throw new InvalidOperationException("The stiffness matrix is not positive definite, so the structure is a mechanism.");
                    }

                    double diagonal = Math.Sqrt(pivot);
                    entries[rowJ + j] = diagonal;
                    for (int i = j + 1; i < order; i++)
                    {
                        int rowI = i * order;
                        double sum = entries[rowI + j];
                        for (int k = 0; k < j; k++)
                        {
                            sum -= entries[rowI + k] * entries[rowJ + k];
                        }

                        entries[rowI + j] = sum / diagonal;
                    }
                }

                factored = true;
            }

            public void Solve(double[] rhs, double[] solution)
            {
                if (!factored)
                {
                    throw new InvalidOperationException("Factor the system before solving it.");
                }

                if (rhs == null)
                {
                    throw new ArgumentNullException(nameof(rhs));
                }

                if (solution == null)
                {
                    throw new ArgumentNullException(nameof(solution));
                }

                if (rhs.Length != order || solution.Length != order)
                {
                    throw new ArgumentException("The right-hand side and solution must match the system order.");
                }

                var y = new double[order];
                for (int i = 0; i < order; i++)
                {
                    int rowI = i * order;
                    double sum = rhs[i];
                    for (int k = 0; k < i; k++)
                    {
                        sum -= entries[rowI + k] * y[k];
                    }

                    y[i] = sum / entries[rowI + i];
                }

                for (int i = order - 1; i >= 0; i--)
                {
                    double sum = y[i];
                    for (int k = i + 1; k < order; k++)
                    {
                        sum -= entries[k * order + i] * y[k];
                    }

                    y[i] = sum / entries[i * order + i];
                }

                Array.Copy(y, solution, order);
            }
        }
    }
}
