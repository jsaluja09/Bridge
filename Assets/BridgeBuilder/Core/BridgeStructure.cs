using System.Collections.Generic;
using System.Numerics;

namespace BridgeBuilder.Core
{
    /// <summary>
    /// The 3D bridge built from a design: the drawn side, a mirrored twin at road width,
    /// a tie between each node and its twin, and an X-brace across every member and its twin.
    /// Bracing every member/twin face makes each drawn triangle a rigid prism, so the bridge
    /// can't fall over sideways and only the drawn shape decides whether it holds.
    /// </summary>
    public sealed class BridgeStructure
    {
        public readonly BridgeSolver Solver;
        public readonly BridgeFrame Frame;
        public readonly int DesignNodeCount;
        public readonly int DesignMemberCount;
        public readonly float FarU;
        public readonly float FarV;

        /// <summary>Drawn members (side 0 indices) flat enough to drive on. Twin is index + DesignMemberCount.</summary>
        public readonly List<int> RoadMembers = new List<int>();

        BridgeStructure(BridgeSettings settings, BridgeFrame frame, int nodes, int members, float farU, float farV)
        {
            Solver = new BridgeSolver(settings);
            Frame = frame;
            DesignNodeCount = nodes;
            DesignMemberCount = members;
            FarU = farU;
            FarV = farV;
        }

        public int TwinMember(int member) => member + DesignMemberCount;

        public static BridgeStructure Build(BridgeDesign design, BridgeFrame frame, BridgeSettings settings)
        {
            int n = design.Nodes.Count;
            int m = design.Members.Count;
            var st = new BridgeStructure(settings, frame, n, m, design.FarU, design.FarV);

            var positions = new List<Vector3>(2 * n);
            var isFixed = new List<bool>(2 * n);
            for (int side = 0; side < 2; side++)
            {
                foreach (var node in design.Nodes)
                {
                    positions.Add(frame.ToWorld(node.Position, side));
                    isFixed.Add(node.Anchor);
                }
            }

            var members = new List<(int, int, MemberKind)>(4 * m + n);
            for (int side = 0; side < 2; side++)
            {
                foreach (var dm in design.Members)
                    members.Add((dm.A + side * n, dm.B + side * n, MemberKind.Drawn));
            }

            var used = new bool[n];
            foreach (var dm in design.Members)
            {
                used[dm.A] = true;
                used[dm.B] = true;
            }
            for (int i = 0; i < n; i++)
            {
                if (used[i] && !design.Nodes[i].Anchor)
                    members.Add((i, i + n, MemberKind.Lateral));
            }
            foreach (var dm in design.Members)
            {
                if (design.Nodes[dm.A].Anchor && design.Nodes[dm.B].Anchor)
                    continue;
                members.Add((dm.A, dm.B + n, MemberKind.Lateral));
                members.Add((dm.B, dm.A + n, MemberKind.Lateral));
            }

            st.Solver.Build(positions, isFixed, members);

            for (int i = 0; i < m; i++)
            {
                if (design.IsRoad(i))
                    st.RoadMembers.Add(i);
            }
            return st;
        }
    }
}
