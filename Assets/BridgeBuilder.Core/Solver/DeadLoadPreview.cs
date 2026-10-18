using System;
using System.Collections.Generic;

namespace BridgeBuilder.Core
{
    public enum PreviewStatus
    {
        Completed,
        Cancelled,
    }

    public sealed class PreviewResult
    {
        public PreviewStatus Status { get; set; }
        public NodalLoad[] SelfWeight { get; set; } = Array.Empty<NodalLoad>();
        public double[] Utilizations { get; set; } = Array.Empty<double>();     // per SolverInput member
        public FailureMode[] Modes { get; set; } = Array.Empty<FailureMode>();
        public StaticSolveResult Solve { get; set; }
    }

    /// <summary>
    /// Dead-load preview (spec 2.10): one static solve under the bridge's own weight, colored like a test run.
    /// Self-weight per member is bridge length x area x density x gravity, half to each node, pointing down.
    /// Polls shouldCancel before solving and skips the solve when it is set.
    /// </summary>
    public static class DeadLoadPreview
    {
        /// <summary>
        /// Self-weight per node. With <paramref name="standing"/> (per SolverInput member), a member that is not
        /// standing weighs nothing, so a run can drop a broken member's weight from the nodes it hung from
        /// (bridge-core DynamicSelfWeight).
        /// </summary>
        public static NodalLoad[] SelfWeight(SolverInput input, double gravity, double scale, IReadOnlyList<bool> standing = null)
        {
            var index = new Dictionary<int, int>();
            for (int i = 0; i < input.Nodes.Count; i++)
                index[input.Nodes[i].Id] = i;
            var sections = new Dictionary<int, SectionProperties>();
            foreach (var s in input.Sections)
                sections[s.Id] = s;

            var down = new double[input.Nodes.Count];
            for (int k = 0; k < input.Members.Count; k++)
            {
                if (standing != null && !standing[k])
                    continue;
                var m = input.Members[k];
                var s = sections[m.SectionId];
                double length = Vec3.Distance(input.Nodes[index[m.NodeA]].Position, input.Nodes[index[m.NodeB]].Position) * scale;
                double weight = length * s.A * s.Density * gravity;
                down[index[m.NodeA]] += weight / 2;
                down[index[m.NodeB]] += weight / 2;
            }
            var loads = new NodalLoad[input.Nodes.Count];
            for (int i = 0; i < loads.Length; i++)
                loads[i] = new NodalLoad(input.Nodes[i].Id, new Vec3(0, -down[i], 0));
            return loads;
        }

        public static NodalLoad[] SelfWeight(SolverInput input) => SelfWeight(input, Constants.Loads.Gravity, Constants.Scale.RoomToBridge);

        public static PreviewResult Run(SolverInput input, IStaticSolver solver, Func<bool> shouldCancel) =>
            Run(input, solver, shouldCancel, Constants.Loads.Gravity, input.ScaleFactor > 0 ? input.ScaleFactor : Constants.Scale.RoomToBridge);

        public static PreviewResult Run(SolverInput input, IStaticSolver solver, Func<bool> shouldCancel, double gravity, double scale)
        {
            var result = new PreviewResult { SelfWeight = SelfWeight(input, gravity, scale) };
            if (shouldCancel != null && shouldCancel())
            {
                result.Status = PreviewStatus.Cancelled;
                return result;
            }

            result.Solve = solver.Solve(input, result.SelfWeight);
            var lengths = BucklingLengths.Compute(input, scale);
            var sections = new Dictionary<int, SectionProperties>();
            foreach (var s in input.Sections)
                sections[s.Id] = s;
            result.Utilizations = new double[input.Members.Count];
            result.Modes = new FailureMode[input.Members.Count];
            for (int k = 0; k < input.Members.Count; k++)
            {
                var f = result.Solve.MemberForces[k];
                if (f == null)
                    continue;      // a piece not connected to any support (the banded solver leaves it out)
                var cap = MemberCapacity.Evaluate(f, sections[input.Members[k].SectionId], lengths[k]);
                result.Utilizations[k] = cap.Utilization;
                result.Modes[k] = cap.Governing;
            }
            result.Status = PreviewStatus.Completed;
            return result;
        }
    }

    /// <summary>
    /// A member's buckling length: its run of line elements between junctions (supports, line ends, or nodes
    /// where three or more members meet), in bridge-world meters. A drawn strut buckles as a whole, not per
    /// 10 cm element.
    /// </summary>
    public static class BucklingLengths
    {
        public static double[] Compute(SolverInput input, double scale)
        {
            var index = new Dictionary<int, int>();
            for (int i = 0; i < input.Nodes.Count; i++)
                index[input.Nodes[i].Id] = i;
            int m = input.Members.Count;
            var length = new double[m];
            var degree = new int[input.Nodes.Count];
            for (int k = 0; k < m; k++)
            {
                var mem = input.Members[k];
                length[k] = Vec3.Distance(input.Nodes[index[mem.NodeA]].Position, input.Nodes[index[mem.NodeB]].Position) * scale;
                degree[index[mem.NodeA]]++;
                degree[index[mem.NodeB]]++;
            }

            var result = (double[])length.Clone();
            var byLine = new Dictionary<int, List<int>>();
            for (int k = 0; k < m; k++)
            {
                var mem = input.Members[k];
                if (mem.HiddenBrace || mem.LineId < 0)
                    continue;
                if (!byLine.TryGetValue(mem.LineId, out var list))
                    byLine[mem.LineId] = list = new List<int>();
                list.Add(k);
            }
            foreach (var chain in byLine.Values)
            {
                int start = 0;
                for (int i = 0; i < chain.Count; i++)
                {
                    var mem = input.Members[chain[i]];
                    int joint = index[mem.NodeB];
                    bool end = i == chain.Count - 1 || input.Nodes[joint].Fixed || degree[joint] >= 3 ||
                               input.Members[chain[i + 1]].NodeA != mem.NodeB;
                    if (!end)
                        continue;
                    double total = 0;
                    for (int j = start; j <= i; j++)
                        total += length[chain[j]];
                    for (int j = start; j <= i; j++)
                        result[chain[j]] = total;
                    start = i + 1;
                }
            }
            return result;
        }
    }

    /// <summary>
    /// IStaticSolver on the banded path (spec 2.12), for the headset. Pieces not connected to any support are
    /// left out: their displacements are zero and their member forces null.
    /// </summary>
    public sealed class BandedStaticSolver : IStaticSolver
    {
        public StaticSolveResult Solve(SolverInput input, NodalLoad[] loads)
        {
            var frame = new FrameDynamics(input);
            frame.Rebuild(true);
            var index = new Dictionary<int, int>();
            for (int i = 0; i < input.Nodes.Count; i++)
                index[input.Nodes[i].Id] = i;
            var force = new Vec3[input.Nodes.Count];
            foreach (var l in loads)
                force[index[l.NodeId]] += l.Force;
            frame.SolveStatic(force);

            double scale = frame.Scale;
            var result = new StaticSolveResult { Displacements = new Vec3[input.Nodes.Count], MemberForces = new MemberForce[input.Members.Count] };
            for (int i = 0; i < input.Nodes.Count; i++)
                result.Displacements[i] = frame.Displacement(i) / scale;
            for (int k = 0; k < input.Members.Count; k++)
            {
                if (frame.InFrame[k])
                    result.MemberForces[k] = frame.Forces[k];
            }
            return result;
        }
    }
}
